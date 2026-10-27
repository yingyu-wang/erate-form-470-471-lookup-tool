using System.Globalization;
using System.Text;

namespace ERateLookup.Core;

public sealed record ImportPreview(List<WatchlistEntry> Entries, List<string> Errors);
public sealed record PortfolioRow(LookupKey Query, Filing? Filing, DateTimeOffset? Observed, string Outcome, string Change, string Note, string FundingRequestNumber = "")
{
    public string Id => Outcome == FrnSnapshot.ReportedOutcome && FundingRequestNumber.Length > 0
        ? Query.Id + "/frn/" + FundingRequestNumber
        : Query.Id + "/" + (Filing?.Number ?? "outcome");
    public string Display
    {
        get
        {
            var identity = Filing?.Number ?? Outcome;
            if (FundingRequestNumber.Length > 0)
                identity = "FRN " + FundingRequestNumber + (string.IsNullOrEmpty(Filing?.Number) ? "" : " · application " + Filing.Number);
            return $"BEN {Query.Ben} · FY{Query.Year} · {(int)Query.Form} · {identity}\n{Filing?.Name} · {Filing?.Status} · {Change}\n{Filing?.Services}\n{Note}";
        }
    }
    public override string ToString() => Display.Replace('\n', ' ');
}

/// <summary>One portable portfolio workflow shared by all native interfaces.</summary>
public sealed class PortfolioSession
{
    readonly UsacClient client;
    public PortfolioStore Store
    {
        get;
    }
    public List<PortfolioRow> Rows { get; private set; } = [];
    public const string ClearHistoryPrompt = "Delete all saved filing and FRN snapshots? Watchlists are retained.";
    public HashSet<string> Selected { get; } = [];
    readonly Dictionary<LookupKey, long> displayedSequences = [];
    readonly Dictionary<string, long> displayedFrnSequences = [];
    public PortfolioSession(UsacClient client, PortfolioStore store)
    {
        this.client = client;
        Store = store;
    }
    public void ClearView()
    {
        Rows = [];
        Selected.Clear();
        displayedSequences.Clear();
        displayedFrnSequences.Clear();
    }
    public static List<int> ParseYears(string text)
    {
        var years = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year < 2016 || year > 2100)
                throw new ArgumentException("Enter comma-separated funding years between 2016 and 2100.");
            if (!years.Contains(year))
                years.Add(year);
        }
        if (years.Count is < 1 or > 5)
            throw new ArgumentException("Choose one to five funding years.");
        return years;
    }
    public static List<LookupKey> Queries(IEnumerable<WatchlistEntry> entries, IEnumerable<int> years, int formChoice)
    {
        var forms = formChoice switch
        {
            0 => new[] { FilingForm.Form470 },
            1 => new[] { FilingForm.Form471 },
            2 => new[] { FilingForm.Form470, FilingForm.Form471 },
            _ => throw new ArgumentException("Choose a form.")
        };
        var queries = (from entry in entries from year in years from form in forms select new LookupKey(entry.Ben, year, form)).Distinct().ToList();
        if (queries.Count is < 1 or > 1000)
            throw new ArgumentException("Save at least one BEN and choose no more than 1,000 BEN/year/form queries.");
        foreach (var query in queries)
            query.Validate();
        return queries;
    }
    public void LoadSaved(IEnumerable<LookupKey> queries)
    {
        Rows = [];
        Selected.Clear();
        displayedSequences.Clear();
        displayedFrnSequences.Clear();
        foreach (var query in queries)
        {
            var saved = Store.Read(query);
            if (saved is null)
                Rows.Add(new(query, null, null, "Not saved", "", "No complete saved lookup. Refresh live when connected."));
            else
                Rows.AddRange(ToRows(saved, "Saved / offline"));
        }
        ApplyFrnAnnotations();
    }
    public async Task RefreshAsync(IReadOnlyList<LookupKey> queries, IProgress<string>? progress, CancellationToken ct)
    {
        Rows = [];
        Selected.Clear();
        displayedSequences.Clear();
        displayedFrnSequences.Clear();
        var frnAttempted = new HashSet<string>(StringComparer.Ordinal);
        var frnNotes = new List<(string Ben, int Year, string Note)>();
        var completed = 0;
        foreach (var query in queries)
        {
            if (ct.IsCancellationRequested)
            {
                Rows.Add(new(query, null, null, "Not started", "", "Canceled before this query."));
                continue;
            }
            progress?.Report($"Refreshing {completed + 1}/{queries.Count}: BEN {query.Ben}, FY{query.Year}, Form {(int)query.Form}");
            var seq = Store.BeginObservation();
            var refreshFrn = false;
            try
            {
                var result = await client.GetFilingsAsync(query.Ben, query.Year, query.Form, true, ct);
                ct.ThrowIfCancellationRequested();
                if (Store.SaveLive(query, seq, result, ct))
                {
                    Rows.AddRange(ToRows(Store.Read(query)!, "Live"));
                    Store.RecordOutcome(query, seq, result.Data.Count == 0 ? "SuccessEmpty" : "Success");
                }
                else
                {
                    // A newer observation was saved concurrently, or saved history was
                    // cleared mid-flight: show the current baseline instead of a failure.
                    var current = Store.Read(query);
                    if (current is null)
                        Rows.Add(new(query, null, null, "Not saved", "", "No complete saved lookup. Refresh live when connected."));
                    else
                        Rows.AddRange(ToRows(current, "Live"));
                    Store.RecordOutcome(query, seq, "Superseded");
                }
                refreshFrn = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Store.RecordOutcome(query, seq, "Cancelled");
                Rows.Add(new(query, null, null, "Cancelled", "", "Previous saved results retained. Use Open saved to view them."));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                Store.RecordOutcome(query, seq, "Failed");
                Rows.Add(new(query, null, null, "Failed", "", ex.Message + " Previous saved results retained."));
                refreshFrn = true;
            }
            if (refreshFrn && frnAttempted.Add(query.Ben + "/" + query.Year.ToString(CultureInfo.InvariantCulture)))
            {
                try
                {
                    var frns = await client.GetFundingRequestsAsync(query.Ben, query.Year, true, ct);
                    ct.ThrowIfCancellationRequested();
                    Store.SaveFrns(query, seq, frns, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    frnNotes.Add((query.Ben, query.Year, "FRN status refresh canceled. Previous FRN snapshot retained."));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
                {
                    frnNotes.Add((query.Ben, query.Year, "FRN status refresh failed. Previous FRN snapshot retained. " + ex.Message));
                }
            }
            completed++;
            if (completed < queries.Count && !ct.IsCancellationRequested)
                try
                {
                    await Task.Delay(300, ct);
                }
                catch (OperationCanceledException) { }
        }
        ApplyFrnAnnotations();
        foreach (var (ben, year, note) in frnNotes)
            AppendNote(ben, year, note);
        progress?.Report($"Finished: {completed}/{queries.Count} queries attempted. {Rows.Count(r => r.Outcome == "Failed")} failed. Select results to export; use Open saved for prior data.");
    }
    IEnumerable<PortfolioRow> ToRows(SavedLookup saved, string origin)
    {
        displayedSequences[saved.Key] = saved.Sequence;
        var unread = Store.UnreadChanges(saved.Key).Where(e => !IsFrnChange(e.Change)).GroupBy(e => e.Change.Application).Select(g => g.Last().Change).ToList();
        var note = $"{origin}; observed {saved.ObservedAt.LocalDateTime:g}." + (saved.FirstObservation ? " First observation; comparison starts on the next live refresh." : "");
        if (saved.Filings.Count == 0)
            yield return new(saved.Key, null, saved.ObservedAt, "SuccessEmpty", "", note + " No certified filings found in this published query.");
        foreach (var filing in saved.Filings)
            yield return new(saved.Key, filing, saved.ObservedAt, "Success", unread.FirstOrDefault(c => c.Application == filing.Number)?.Kind ?? "", note);
        foreach (var missing in unread.Where(c => c.Kind == "No longer returned by this query" && !saved.Filings.Any(f => f.Number == c.Application)))
            yield return new(saved.Key, new Filing(missing.Application, saved.Key.Ben, "", "", "", "", "", "Unknown"), saved.ObservedAt, "Not returned", missing.Kind, note + " This does not establish withdrawal or denial.");
    }
    static bool IsFrnChange(FilingChange change) => change.Application.StartsWith("FRN ", StringComparison.Ordinal);
    static string MergeChange(string existing, string kind)
    {
        if (existing.Length == 0)
            return kind;
        var parts = existing.Split("; ", StringSplitOptions.RemoveEmptyEntries);
        return parts.Contains(kind, StringComparer.Ordinal) ? existing : existing + "; " + kind;
    }
    static string AppendAssumption(string note, string kind)
    {
        if (kind != FrnCommitment.ReportedCommittedAmountKind || note.Contains(FrnCommitment.ReportedAmountAssumption, StringComparison.Ordinal))
            return note;
        return note + " " + FrnCommitment.ReportedAmountAssumption;
    }
    void AppendNote(string ben, int year, string message)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Query.Ben == ben && Rows[i].Query.Year == year && !Rows[i].Note.Contains(message, StringComparison.Ordinal))
                Rows[i] = Rows[i] with
                {
                    Note = Rows[i].Note + " " + message
                };
        }
    }
    static LookupKey LookupKeyFromId(string id)
    {
        var parts = id.Split('/');
        return new LookupKey(parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), (FilingForm)int.Parse(parts[2], CultureInfo.InvariantCulture));
    }
    void ApplyFrnAnnotations()
    {
        foreach (var pair in Rows.Select(row => (row.Query.Ben, row.Query.Year)).Distinct().ToList())
        {
            var key = new LookupKey(pair.Ben, pair.Year, FilingForm.Form471);
            var baseline = Store.ReadFrns(key);
            if (baseline is null)
                continue;
            displayedFrnSequences[key.Id] = baseline.Sequence;
            var latest = Store.UnreadChanges(key)
                .Where(item => IsFrnChange(item.Change))
                .GroupBy(item => (item.Change.Application, item.Change.Kind))
                .Select(group => group.Last())
                .ToList();
            if (latest.Count == 0)
                continue;
            foreach (var group in latest.GroupBy(item => item.Change.Application))
            {
                var number = group.Key["FRN ".Length..];
                var current = baseline.Frns.FirstOrDefault(frn => frn.FundingRequestNumber == number);
                var application = current?.ApplicationNumber ?? "";
                var kinds = group.Select(item => item.Change.Kind).Distinct(StringComparer.Ordinal).ToList();
                var matched = false;
                if (application.Length > 0)
                {
                    for (var i = 0; i < Rows.Count; i++)
                    {
                        var row = Rows[i];
                        if (row.Query.Ben != pair.Ben || row.Query.Year != pair.Year || row.Query.Form != FilingForm.Form471 || row.Filing?.Number != application || row.Outcome == FrnSnapshot.ReportedOutcome)
                            continue;
                        matched = true;
                        var change = row.Change;
                        var note = row.Note;
                        foreach (var kind in kinds)
                        {
                            change = MergeChange(change, kind);
                            note = AppendAssumption(note, kind);
                        }
                        if (!note.Contains("FRN " + number, StringComparison.Ordinal))
                            note += " FRN " + number + ".";
                        Rows[i] = row with
                        {
                            Change = change,
                            Note = note
                        };
                    }
                }
                if (matched)
                    continue;
                var anchor = Rows.Select(row => row.Query).FirstOrDefault(item => item.Ben == pair.Ben && item.Year == pair.Year && item.Form == FilingForm.Form471)
                    ?? Rows.Select(row => row.Query).First(item => item.Ben == pair.Ben && item.Year == pair.Year);
                if (Rows.Any(row => row.Id == anchor.Id + "/frn/" + number))
                    continue;
                var noteText = "FRN " + number + ". " + FrnSnapshot.ObservationNote;
                foreach (var kind in kinds)
                    noteText = AppendAssumption(noteText, kind);
                var filing = new Filing(application, pair.Ben, current?.Name ?? "", "", "", "", "", current?.FrnStatus ?? "");
                Rows.Add(new PortfolioRow(anchor, filing, baseline.ObservedAt, FrnSnapshot.ReportedOutcome, string.Join("; ", kinds), noteText, number));
            }
        }
    }
    public List<PortfolioRow> Filter(string keyword, string service, bool changesOnly = false)
    {
        keyword = keyword.Trim();
        service = service.Trim();
        return Rows.Where(r => (!changesOnly || r.Change.Length > 0)
            && (service.Length == 0 || (r.Filing?.Services ?? "").Contains(service, StringComparison.OrdinalIgnoreCase))
            && (keyword.Length == 0 || $"{r.Query.Ben} {r.Filing?.Name} {r.Filing?.Nickname} {r.Filing?.Services} {r.Filing?.Number}".Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();
    }
    public string ChangeHistory()
    {
        var lines = new List<string>();
        foreach (var (key, sequence) in displayedSequences)
            foreach (var item in Store.UnreadChanges(key).Where(e => e.Sequence <= sequence && !IsFrnChange(e.Change)))
                lines.Add($"BEN {key.Ben} · FY{key.Year} · Form {(int)key.Form} · {item.Change.Application}\n{item.Change.Kind} · observed {item.ObservedAt.LocalDateTime:g}\nBefore: {item.Change.Before}\nAfter: {item.Change.After}\n");
        var frnLines = new List<string>();
        foreach (var (id, sequence) in displayedFrnSequences)
        {
            var key = LookupKeyFromId(id);
            foreach (var item in Store.UnreadChanges(key).Where(e => e.Sequence <= sequence && IsFrnChange(e.Change)))
                frnLines.Add($"BEN {key.Ben} · FY{key.Year} · {item.Change.Application}\n{item.Change.Kind} · observed {item.ObservedAt.LocalDateTime:g}\nBefore: {item.Change.Before}\nAfter: {item.Change.After}\n");
        }
        if (frnLines.Count > 0)
        {
            lines.Add(FrnCommitment.ReportedAmountAssumption + "\n");
            lines.AddRange(frnLines);
        }
        return lines.Count == 0 ? "No unread changes in the displayed observations." : string.Join("\n", lines);
    }
    public void AcknowledgeDisplayed()
    {
        foreach (var (key, sequence) in displayedSequences)
            Store.Acknowledge(key, sequence);
        foreach (var (id, sequence) in displayedFrnSequences)
            Store.AcknowledgeFrns(LookupKeyFromId(id), sequence);
        Rows = Rows.Select(r => r with { Change = "" }).ToList();
    }
    public List<string[]> ExportRows(CaliforniaDirectory? directory = null) => CreateExport(directory).Table;

    /// <summary>
    /// Base columns plus <c>LookupOutcome</c>, <c>Change</c>, <c>SourceAndNotes</c>, and <c>AllowableContractDate</c>.
    /// <paramref name="directory"/> is unused. Budget columns stay in the optional budget group and are not written.
    /// </summary>
    public PreparedExport CreateExport(CaliforniaDirectory? directory = null)
    {
        _ = directory;
        var selected = Rows.Where(row => Selected.Contains(row.Id)).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("Select at least one result to export.");
        var columns = new ExportColumns { IncludePortfolio = true };
        var table = new List<string[]> { ExportSchema.Columns(columns).ToArray() };
        var jsonRows = new List<ExportJsonRow>(selected.Count);
        foreach (var row in selected)
        {
            var filing = row.Filing ?? new Filing("", row.Query.Ben, "", "", "", "", "");
            var published = ExportSchema.FromFiling(row.Query.Form, filing, row.Query.Year, false, null, null) with
            {
                LookupOutcome = row.Outcome,
                Change = row.Change,
                SourceAndNotes = row.Note,
                AllowableContractDate = PortfolioInsights.ContractDate(row.Filing),
            };
            if (string.IsNullOrWhiteSpace(filing.Number) && row.FundingRequestNumber.Length == 0)
                published = published with
                {
                    Source = ""
                };
            if (row.Outcome == FrnSnapshot.ReportedOutcome && row.FundingRequestNumber.Length > 0)
                published = published with
                {
                    FundingRequestNumber = row.FundingRequestNumber,
                    Source = UsacSources.FundingRequest(row.FundingRequestNumber, filing.Number)
                };
            table.Add(ExportSchema.ToRow(ExportSchema.WithDisplayedBen(published, row.Query.Ben), columns));
            jsonRows.Add(ExportSchema.ToPortfolioJsonRow(published));
        }

        var years = selected.Select(row => row.Query.Year).Distinct().ToArray();
        var forms = selected.Select(row => (int)row.Query.Form).Distinct().ToArray();
        var bens = selected.Select(row => row.Query.Ben).Distinct(StringComparer.Ordinal).ToArray();
        var observed = selected.Where(row => row.Observed is not null).Select(row => row.Observed!.Value).ToArray();
        return new PreparedExport
        {
            Table = table,
            Metadata = new ExportMetadata
            {
                Form = forms.Length == 1 ? forms[0] : null,
                Linked = false,
                Ben = bens.Length == 1 ? bens[0] : null,
                FundingYear = years.Length == 1 ? years[0] : null,
                FetchedAt = observed.Length == 0 ? DateTimeOffset.UtcNow : observed.Min(),
            },
            JsonRows = jsonRows,
        };
    }
    public string HistorySummary()
    {
        return string.Join("\n", Rows.GroupBy(r => r.Query).Select(g => $"BEN {g.Key.Ben} · FY{g.Key.Year} · Form {(int)g.Key.Form}: {(g.Any(r => r.Outcome is "Failed" or "Not saved" or "Cancelled" or "Not started") ? "Unknown" : g.Count(r => r.Outcome == "Success").ToString(CultureInfo.InvariantCulture))} observed applications; {string.Join(", ", g.Select(r => r.Outcome).Distinct())}. " +
            $"Customer: {string.Join("; ", g.Select(r => r.Filing?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())}. Latest certification: {g.Where(r => r.Outcome == "Success").Select(r => r.Filing?.Certified).OrderDescending().FirstOrDefault() ?? "Unknown"}.\nObserved: {g.Select(r => r.Observed).FirstOrDefault()?.ToLocalTime().ToString("g") ?? "Unknown"}.\nServices: {string.Join("; ", g.Where(r => r.Outcome == "Success").Select(r => r.Filing?.Services).Where(s => !string.IsNullOrEmpty(s)).Distinct())}"));
    }
    public static string ShareText(PortfolioRow r) => $"E-rate Form {(int)r.Query.Form} · FY{r.Query.Year}\nBEN {r.Query.Ben} · {r.Filing?.Name}\nApplication {r.Filing?.Number}\n{r.Filing?.Services}\n{r.Note}" +
        (r.FundingRequestNumber.Length > 0 ? "\nFRN " + r.FundingRequestNumber : "") +
        (Uri.TryCreate(r.Filing?.Pdf, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? "\n" + uri.AbsoluteUri : "");

    public static ImportPreview ParseCsv(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024)
            throw new ArgumentException("CSV exceeds the 1 MiB limit.");
        var records = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, closed = false;
        void Cell()
        {
            if (field.Length > 2000)
                throw new ArgumentException("CSV field exceeds 2,000 characters.");
            row.Add(field.ToString());
            field.Clear();
            closed = false;
            if (row.Count > 50)
                throw new ArgumentException("CSV exceeds 50 columns.");
        }
        void Row()
        {
            Cell();
            records.Add(row);
            row = [];
            if (records.Count > 10000)
                throw new ArgumentException("CSV exceeds 10,000 records.");
        }
        text = text.TrimStart('\uFEFF');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                        closed = true;
                    }
                }
                else
                    field.Append(c);
            }
            else if (c == ',')
                Cell();
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                Row();
            }
            else if (c == '"' && field.Length == 0 && !closed)
                quoted = true;
            else if (closed || c == '"')
                throw new ArgumentException("Malformed quoted CSV field.");
            else
                field.Append(c);
            if (field.Length > 2000)
                throw new ArgumentException("CSV field exceeds 2,000 characters.");
        }
        if (quoted)
            throw new ArgumentException("Unclosed quoted CSV field.");
        if (field.Length > 0 || row.Count > 0 || closed)
            Row();
        if (records.Count == 0)
            throw new ArgumentException("CSV needs a BEN header.");
        var headers = records[0].Select(h => h.Trim().ToUpperInvariant()).ToList();
        if (headers.Count != headers.Distinct().Count() || !headers.Contains("BEN"))
            throw new ArgumentException("CSV needs a BEN header and unique column names.");
        var entries = new List<WatchlistEntry>();
        var errors = new List<string>();
        var seen = new HashSet<string>();
        for (var n = 1; n < records.Count; n++)
        {
            var record = records[n];
            if (record.All(string.IsNullOrWhiteSpace))
                continue;
            string Value(string key)
            {
                var i = headers.IndexOf(key);
                return i < 0 || i >= record.Count ? "" : record[i].Trim();
            }
            var ben = Value("BEN");
            var name = Value("NAME");
            var tags = Value("TAGS");
            if (record.Count != headers.Count || ben.Length is < 1 or > 32 || !ben.All(char.IsAsciiDigit) || name.Length > 200 || tags.Length > 300 || name.Any(char.IsControl) || tags.Any(char.IsControl))
                errors.Add($"Record {n + 1}: invalid BEN, field length, or column count.");
            else if (!seen.Add(ben))
                errors.Add($"Record {n + 1}: duplicate BEN {ben}.");
            else
                entries.Add(new(ben, name, tags));
        }
        if (entries.Count > 500)
            throw new ArgumentException("CSV exceeds 500 distinct BENs.");
        return new(entries, errors);
    }
    public static async Task<ImportPreview> ReadCsvAsync(Stream input, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + count > 1024 * 1024)
                throw new ArgumentException("CSV exceeds 1 MiB.");
            memory.Write(buffer, 0, count);
        }
        ct.ThrowIfCancellationRequested();
        return ParseCsv(new UTF8Encoding(false, true).GetString(memory.ToArray()));
    }
}
