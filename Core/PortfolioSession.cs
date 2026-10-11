using System.Globalization;
using System.Text;

namespace ERate470Lookup.Core;

public sealed record ImportPreview(List<WatchlistEntry> Entries,List<string> Errors);
public sealed record PortfolioRow(LookupKey Query,Filing? Filing,DateTimeOffset? Observed,string Outcome,string Change,string Note)
{
    public string Id => Query.Id+"/"+(Filing?.Number??"outcome");
    public string Display => $"BEN {Query.Ben} · FY{Query.Year} · {(int)Query.Form} · {Filing?.Number??Outcome}\n{Filing?.Name} · {Filing?.Status} · {Change}\n{Filing?.Services}\n{Note}";
    public override string ToString() => Display.Replace('\n',' ');
}

/// <summary>One portable portfolio workflow shared by all native interfaces.</summary>
public sealed class PortfolioSession
{
    readonly UsacClient client;
    public PortfolioStore Store { get; }
    public List<PortfolioRow> Rows { get; private set; }=[];
    public HashSet<string> Selected { get; }=[];
    public PortfolioSession(UsacClient client,PortfolioStore store) { this.client=client;Store=store; }
    public static List<int> ParseYears(string text)
    {
        var years=new List<int>();
        foreach(var part in text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
        {
            if(!int.TryParse(part,NumberStyles.None,CultureInfo.InvariantCulture,out var year)||year<2016||year>2100)
                throw new ArgumentException("Enter comma-separated funding years between 2016 and 2100.");
            if(!years.Contains(year)) years.Add(year);
        }
        if(years.Count is <1 or >5) throw new ArgumentException("Choose one to five funding years.");
        return years;
    }
    public static List<LookupKey> Queries(IEnumerable<WatchlistEntry> entries,IEnumerable<int> years,int formChoice)
    {
        var forms=formChoice switch {0=>new[]{FilingForm.Form470},1=>new[]{FilingForm.Form471},2=>new[]{FilingForm.Form470,FilingForm.Form471},_=>throw new ArgumentException("Choose a form.")};
        var queries=(from entry in entries from year in years from form in forms select new LookupKey(entry.Ben,year,form)).Distinct().ToList();
        if(queries.Count is <1 or >1000) throw new ArgumentException("Save at least one BEN and choose no more than 1,000 BEN/year/form queries.");
        foreach(var query in queries)query.Validate(); return queries;
    }
    public void LoadSaved(IEnumerable<LookupKey> queries)
    {
        Rows=[];Selected.Clear();
        foreach(var query in queries)
        {
            var saved=Store.Read(query);
            if(saved is null) Rows.Add(new(query,null,null,"Not saved","","No complete saved lookup. Refresh live when connected."));
            else Rows.AddRange(ToRows(saved,"Saved / offline"));
        }
    }
    public async Task RefreshAsync(IReadOnlyList<LookupKey> queries,IProgress<string>? progress,CancellationToken ct)
    {
        Rows=[];Selected.Clear(); var completed=0;
        foreach(var query in queries)
        {
            if(ct.IsCancellationRequested) {Rows.Add(new(query,null,null,"Not started","","Canceled before this query."));continue;}
            progress?.Report($"Refreshing {completed+1}/{queries.Count}: BEN {query.Ben}, FY{query.Year}, Form {(int)query.Form}");
            try
            {
                var seq=Store.BeginObservation();
                var result=await client.GetFilingsAsync(query.Ben,query.Year,query.Form,true,ct);
                ct.ThrowIfCancellationRequested();
                if(!Store.SaveLive(query,seq,result)) throw new InvalidOperationException("A newer lookup is already saved; this observation was not applied.");
                Rows.AddRange(ToRows(Store.Read(query)!,"Live"));
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested)
            {Rows.Add(new(query,null,null,"Cancelled","","Previous saved results retained. Use Open saved to view them."));}
            catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {Rows.Add(new(query,null,null,"Failed","",ex.Message+" Previous saved results retained."));}
            completed++;
            if(completed<queries.Count&&!ct.IsCancellationRequested)
                try {await Task.Delay(300,ct);}catch(OperationCanceledException){ }
        }
        progress?.Report($"Finished: {completed}/{queries.Count} queries attempted. {Rows.Count(r=>r.Outcome=="Failed")} failed. Select results to export; use Open saved for prior data.");
    }
    static IEnumerable<PortfolioRow> ToRows(SavedLookup saved,string origin)
    {
        var note=$"{origin}; observed {saved.ObservedAt.LocalDateTime:g}.";
        if(saved.Filings.Count==0)yield return new(saved.Key,null,saved.ObservedAt,"SuccessEmpty","",note+" No certified filings found in this published query.");
        foreach(var filing in saved.Filings)
            yield return new(saved.Key,filing,saved.ObservedAt,"Success",saved.Changes.FirstOrDefault(c=>c.Application==filing.Number)?.Kind??"",note);
        foreach(var missing in saved.Changes.Where(c=>c.Kind=="No longer returned by this query"))
            yield return new(saved.Key,new Filing(missing.Application,saved.Key.Ben,"","","","","","Unknown"),saved.ObservedAt,"Not returned",missing.Kind,note+" This does not establish withdrawal or denial.");
    }
    public List<PortfolioRow> Filter(string keyword,string service,bool changesOnly=false)
    {
        keyword=keyword.Trim();service=service.Trim();
        return Rows.Where(r=>(!changesOnly||r.Change.Length>0)
            && (service.Length==0||(r.Filing?.Services??"").Contains(service,StringComparison.OrdinalIgnoreCase))
            && (keyword.Length==0||$"{r.Query.Ben} {r.Filing?.Name} {r.Filing?.Nickname} {r.Filing?.Services} {r.Filing?.Number}".Contains(keyword,StringComparison.OrdinalIgnoreCase))).ToList();
    }
    public List<string[]> ExportRows()
    {
        var rows=new List<string[]> {new[]{"FundingYear","BEN","FormType","ApplicationNumber","Customer","LookupOutcome","FilingStatus","CertifiedDate","Nickname","Services","PDF","ObservedUTC","Change","SourceAndNotes"}};
        foreach(var r in Rows.Where(r=>Selected.Contains(r.Id))) rows.Add(new[]{r.Query.Year.ToString(CultureInfo.InvariantCulture),r.Query.Ben,((int)r.Query.Form).ToString(CultureInfo.InvariantCulture),r.Filing?.Number??"",r.Filing?.Name??"",r.Outcome,r.Filing?.Status??"",r.Filing?.Certified??"",r.Filing?.Nickname??"",r.Filing?.Services??"",r.Filing?.Pdf??"",r.Observed?.ToString("O")??"",r.Change,r.Note});
        if(rows.Count==1)throw new InvalidOperationException("Select at least one result to export.");return rows;
    }
    public string HistorySummary()
    {
        return string.Join("\n",Rows.GroupBy(r=>r.Query).Select(g=>$"BEN {g.Key.Ben} · FY{g.Key.Year} · Form {(int)g.Key.Form}: {(g.Any(r=>r.Outcome is "Failed" or "Not saved" or "Cancelled" or "Not started")?"Unknown":g.Count(r=>r.Outcome=="Success").ToString(CultureInfo.InvariantCulture))} observed applications; {string.Join(", ",g.Select(r=>r.Outcome).Distinct())}. "+
            $"Observed: {g.Select(r=>r.Observed).FirstOrDefault()?.ToLocalTime().ToString("g")??"Unknown"}.\nServices: {string.Join("; ",g.Where(r=>r.Outcome=="Success").Select(r=>r.Filing?.Services).Where(s=>!string.IsNullOrEmpty(s)).Distinct())}"));
    }
    public static string ShareText(PortfolioRow r) => $"E-rate Form {(int)r.Query.Form} · FY{r.Query.Year}\nBEN {r.Query.Ben} · {r.Filing?.Name}\nApplication {r.Filing?.Number}\n{r.Filing?.Services}\n{r.Note}"+
        (Uri.TryCreate(r.Filing?.Pdf,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps ? "\n"+uri.AbsoluteUri : "");

    public static ImportPreview ParseCsv(string text)
    {
        if(Encoding.UTF8.GetByteCount(text)>1024*1024)throw new ArgumentException("CSV exceeds the 1 MiB limit.");
        var records=new List<List<string>>();var row=new List<string>();var field=new StringBuilder();bool quoted=false,closed=false;
        void Cell(){if(field.Length>2000)throw new ArgumentException("CSV field exceeds 2,000 characters.");row.Add(field.ToString());field.Clear();closed=false;if(row.Count>50)throw new ArgumentException("CSV exceeds 50 columns.");}
        void Row(){Cell();records.Add(row);row=[];if(records.Count>10000)throw new ArgumentException("CSV exceeds 10,000 records.");}
        text=text.TrimStart('\uFEFF');
        for(var i=0;i<text.Length;i++)
        {
            var c=text[i];
            if(quoted){if(c=='"'){if(i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else{quoted=false;closed=true;}}else field.Append(c);}
            else if(c==',' )Cell();
            else if(c is '\r' or '\n'){if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;Row();}
            else if(c=='"'&&field.Length==0&&!closed)quoted=true;
            else if(closed||c=='"')throw new ArgumentException("Malformed quoted CSV field.");
            else field.Append(c);
            if(field.Length>2000)throw new ArgumentException("CSV field exceeds 2,000 characters.");
        }
        if(quoted)throw new ArgumentException("Unclosed quoted CSV field.");
        if(field.Length>0||row.Count>0||closed)Row();
        if(records.Count==0)throw new ArgumentException("CSV needs a BEN header.");
        var headers=records[0].Select(h=>h.Trim().ToUpperInvariant()).ToList();
        if(headers.Count!=headers.Distinct().Count()||!headers.Contains("BEN"))throw new ArgumentException("CSV needs a BEN header and unique column names.");
        var entries=new List<WatchlistEntry>();var errors=new List<string>();var seen=new HashSet<string>();
        for(var n=1;n<records.Count;n++)
        {
            var record=records[n];if(record.All(string.IsNullOrWhiteSpace))continue;
            string Value(string key){var i=headers.IndexOf(key);return i<0||i>=record.Count?"":record[i].Trim();}
            var ben=Value("BEN");var name=Value("NAME");var tags=Value("TAGS");
            if(record.Count!=headers.Count||ben.Length is <1 or >32||!ben.All(char.IsAsciiDigit)||name.Length>200||tags.Length>300||name.Any(char.IsControl)||tags.Any(char.IsControl))
                errors.Add($"Record {n+1}: invalid BEN, field length, or column count.");
            else if(!seen.Add(ben))errors.Add($"Record {n+1}: duplicate BEN {ben}.");
            else entries.Add(new(ben,name,tags));
        }
        if(entries.Count>500)throw new ArgumentException("CSV exceeds 500 distinct BENs.");
        return new(entries,errors);
    }
}
