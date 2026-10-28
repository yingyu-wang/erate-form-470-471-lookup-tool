using System.Net;
using System.Text;
using ERateLookup.Core;
using Xunit;
using Microsoft.Data.Sqlite;


namespace ERateLookup.Tests;

public class PortfolioChecks
{
    [Fact]
    public async Task PortfolioScenario()
    {
        static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
        var folder = Path.Combine(Path.GetTempPath(), "ERatePortfolio-" + Guid.NewGuid());
        try
        {
            var store = new PortfolioStore(folder); var list = store.CreateList("Territory"); store.SaveEntry(list.Id, new("00123", "District, One", "West"));
            var reopened = new PortfolioStore(folder);
            Check(reopened.Entries(list.Id).Single().Ben == "00123", "Watchlist preserves text BEN and survives restart");
            bool duplicate = false; try { store.SaveEntry(list.Id, new("00123", "Duplicate", "")); } catch (InvalidOperationException) { duplicate = true; }
            Check(duplicate, "Duplicate watchlist BEN rejected");
            store.SaveEntry(list.Id, new("00123", "Updated", "East"), true); store.RenameList(list.Id, "Renamed");
            Check(store.Entries(list.Id).Single().Name == "Updated" && store.Lists().Any(l => l.Name == "Renamed"), "Watchlist rename and entry update");
            var key = new LookupKey("00123", 2027, FilingForm.Form470); var now = DateTimeOffset.UtcNow;
            Filing Filing(string number, string service = "Wireless") => new(number, key.Ben, "District", "Nickname", "2026-10-01", service, "https://example.org/form.pdf", "Certified", "2026-10-29T00:00:00.000");
            var seq1 = store.BeginObservation(); var seq2 = store.BeginObservation();
            Check(store.SaveLive(key, seq2, new(new() { Filing("2") }, now, false)), "Complete live observation saved");
            Check(!store.SaveLive(key, seq1, new(new() { Filing("1") }, now.AddSeconds(-1), false)) && store.Read(key)!.Filings.Single().Number == "2", "Older overlapping observation cannot regress baseline");
            Check(store.Read(key)!.Changes.Count == 0, "First observation creates no false new alerts");
            Check(!store.SaveLive(key, store.BeginObservation(), new(new() { Filing("3") }, now, true)), "Cached result cannot advance baseline");
            store.SaveLive(key, store.BeginObservation(), new(new() { Filing("2", "Switches"), Filing("3") }, now.AddSeconds(1), false));
            Check(store.Read(key)!.Changes.Select(c => c.Kind).Order().SequenceEqual(new[] { "Changed", "New" }), "New and changed filings compared by application identity");
            Check(PortfolioStore.Compare(new[] { Filing("1", "Wireless; Switches") }, new[] { Filing("1", "Switches; Wireless; Wireless") }).Count == 0, "Service order and duplicates do not create changes");
            store.SaveLive(key, store.BeginObservation(), new(new(), now.AddSeconds(2), false));
            Check(store.Read(key)!.Changes.All(c => c.Kind == "No longer returned by this query"), "Missing filings not inferred withdrawn");
            var unread = store.UnreadChanges(key); var lastSequence = unread.Max(e => e.Sequence);
            store.SaveLive(key, store.BeginObservation(), new(new(), now.AddSeconds(3), false));
            Check(store.UnreadChanges(key).Count == unread.Count, "Repeated refresh retains unread changes without duplicate events");
            store.Acknowledge(key, lastSequence); Check(store.UnreadChanges(key).Count == 0, "Acknowledgement is independent of the saved baseline");
            store.RemoveEntry(list.Id, key.Ben); store.DeleteList(list.Id);
            Check(store.Read(key) is not null, "Removing list and membership preserves saved history");
            var parsed = PortfolioSession.ParseCsv("\uFEFFBEN,Name,Tags\r\n00123,\"District, One\",West\r\n00123,Duplicate,\r\nbad,Invalid,\r\n456,\"A \"\"quoted\"\" name\",East\r\n");
            Check(parsed.Entries.Count == 2 && parsed.Errors.Count == 2 && parsed.Entries[1].Name == "A \"quoted\" name", "CSV quoting, duplicate and invalid row preview");
            var imported = store.CreateList("Imports");
            Check(store.ImportEntries(imported.Id, parsed.Entries) == 2 && store.ImportEntries(imported.Id, parsed.Entries) == 0, "Atomic import skips existing BENs");
            bool invalidImport = false; try { store.ImportEntries(imported.Id, new[] { new WatchlistEntry("700", "Valid", ""), new WatchlistEntry("bad", "Invalid", "") }); } catch (ArgumentException) { invalidImport = true; }
            Check(invalidImport && !store.Entries(imported.Id).Any(e => e.Ben == "700"), "Invalid import rolls back all new entries");
            bool badHeader = false; try { PortfolioSession.ParseCsv("BEN,BEN\n1,2"); } catch (ArgumentException) { badHeader = true; }
            Check(badHeader, "Ambiguous CSV headers rejected");
            bool oversized = false; try { PortfolioSession.ParseCsv(new string('x', 1024 * 1024 + 1)); } catch (ArgumentException) { oversized = true; }
            Check(oversized, "Oversized imports rejected");
            bool maxQueries = false; try { PortfolioSession.Queries(Enumerable.Range(1, 500).Select(n => new WatchlistEntry(n.ToString(), "", "")), new[] { 2025, 2026 }, 2); } catch (ArgumentException) { maxQueries = true; }
            Check(maxQueries, "Batch expanded-query limit enforced");
            using var handler = new PortfolioHandler(); using var http = new HttpClient(handler); using var client = new UsacClient(http, Path.Combine(folder, "cache")); var session = new PortfolioSession(client, store);
            var keys = new[] { new LookupKey("1", 2027, FilingForm.Form470), new LookupKey("2", 2027, FilingForm.Form470), new LookupKey("3", 2027, FilingForm.Form470) };
            await session.RefreshAsync(keys, null, default);
            Check(session.Rows.Count == 3 && session.Rows.Any(r => r.Outcome == "Failed") && session.Rows.Any(r => r.Outcome == "SuccessEmpty") && session.Rows.Any(r => r.Outcome == "Success"), "Mixed batch failure, empty and success remain distinct");
            session.LoadSaved(new[] { keys[0] }); Check(session.Rows.Single().Note.StartsWith("Saved / offline"), "Complete saved snapshots open offline");
            var row = session.Rows.Single(); session.Selected.Add(row.Id);
            Check(session.Filter("district", "wireless").Count == 1 && session.Filter("district", "switch").Count == 0 && session.Selected.Count == 1, "Filters combine and preserve hidden selection");
            var exported = session.ExportRows();
            var benColumn = Array.IndexOf(exported[0], "BEN");
            Check(exported.Count == 2 && benColumn == 2 && exported[1][benColumn] == "1", "Selected export contains the intended query identity");
            Check(exported[0].SequenceEqual(ExportSchema.Columns(new ExportColumns { IncludePortfolio = true })), "Portfolio export is the base schema plus four trailing columns");
            Check(!exported[0].Contains("BudgetCycle") && !exported[0].Contains("Customer") && exported[0][^4] == "LookupOutcome" && exported[0][^1] == "AllowableContractDate", "Portfolio export drops budget columns and keeps the four trailing names");
            var count = handler.Count; using var canceled = new CancellationTokenSource(); canceled.Cancel(); await session.RefreshAsync(keys, null, canceled.Token);
            Check(handler.Count == count && session.Rows.All(r => r.Outcome == "Not started"), "Canceled batch does not start pending requests");
            Check(PortfolioInsights.ContractDate(Filing("1")) == "2026-10-29" && PortfolioInsights.ContractDate(Filing("1") with { AllowableContractDate = "n/a" }) == "Unknown", "Source contract dates preserve date precision and missing values");
            Check(!PortfolioSession.ShareText(row with { Filing = Filing("1") with { Pdf = "file:///secret" } }).Contains("file:///"), "Share text excludes non-HTTPS links");
            var amountRow = row with { Query = new("1", 2027, FilingForm.Form471), Filing = row.Filing! with { RequestedAmount = "1200.50" } };
            Check(PortfolioInsights.RequestedTotal(new[] { amountRow, amountRow }) == "1,200.50", "Requested funding counts each application once");
            Check(PortfolioInsights.RequestedTotal(new[] { amountRow, amountRow with { Filing = amountRow.Filing! with { Number = "other", RequestedAmount = "" } } }) == "Unknown", "Missing requested amounts cannot become zero funding");
            var large = Enumerable.Range(1, 5001).Select(n => new Filing(n.ToString(), "1", "District", "", "2026-10-01", "Wireless", "", "Certified")).ToList();
            store.SaveLive(keys[0], store.BeginObservation(), new(large, now.AddSeconds(5), false)); session.LoadSaved(new[] { keys[0] });
            foreach (var item in session.Filter("", "wireless")) session.Selected.Add(item.Id);
            Check(session.ExportRows().Count == 5002 && session.Rows.Count == 5001, "Selection and export cover 5,001 applications beyond visible pages");
            var inFlight = store.BeginObservation(); store.ClearHistory(); Check(store.Read(key) is null && store.Lists().Count > 0, "Clear history preserves watchlists");
            Check(!store.SaveLive(key, inFlight, new(new() { Filing("9") }, now, false)), "An in-flight lookup cannot undo Clear saved history");
            using (var retryHandler = new RetryHandler()) using (var retryHttp = new HttpClient(retryHandler))
            {
                using var retryClient = new UsacClient(retryHttp, Path.Combine(folder, "retry")); await retryClient.GetFilingsAsync("1", 2027, true, default);
                Check(retryHandler.Count == 6, "Rate-limited GET honors bounded retry attempts");
            }
            var future = Path.Combine(folder, "future"); Directory.CreateDirectory(future);
            using (var db = new SqliteConnection("Data Source=" + Path.Combine(future, "portfolio.sqlite3"))) { db.Open(); using var c = db.CreateCommand(); c.CommandText = "PRAGMA user_version=99"; c.ExecuteNonQuery(); }
            bool futureRejected = false; try { _ = new PortfolioStore(future); } catch (InvalidOperationException) { futureRejected = true; }
            Check(futureRejected, "Future-schema portfolio is refused without overwrite");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }

    [Fact]
    public void PortfolioRowStillDeconstructsToSixValues()
    {
        var key = new LookupKey("136910", 2020, FilingForm.Form471);
        var filing = new Filing("201009092", key.Ben, "Webster", "", "", "", "");
        var observed = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var row = new PortfolioRow(key, filing, observed, "Success", "Reported committed amount", "Saved note.", "2099010373");

        var (query, item, at, outcome, change, note) = row;
        Assert.Equal(key, query);
        Assert.Same(filing, item);
        Assert.Equal(observed, at);
        Assert.Equal("Success", outcome);
        Assert.Equal("Reported committed amount", change);
        Assert.Equal("Saved note.", note);
        Assert.Equal("2099010373", row.FundingRequestNumber);

        var (query7, item7, at7, outcome7, change7, note7, fundingRequestNumber) = row;
        Assert.Equal(key, query7);
        Assert.Same(filing, item7);
        Assert.Equal(observed, at7);
        Assert.Equal("Success", outcome7);
        Assert.Equal("Reported committed amount", change7);
        Assert.Equal("Saved note.", note7);
        Assert.Equal("2099010373", fundingRequestNumber);
    }

    [Fact]
    public void FrnSnapshotMigrationPersistsReportedChangesWithoutFalsePositives()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERateFrnPortfolio-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "portfolio.sqlite3");
            using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = """
                    CREATE TABLE watchlists(id TEXT PRIMARY KEY,name TEXT NOT NULL);
                    CREATE TABLE entries(list_id TEXT NOT NULL,ben TEXT NOT NULL,name TEXT NOT NULL,tags TEXT NOT NULL,PRIMARY KEY(list_id,ben));
                    CREATE TABLE sequence(value INTEGER NOT NULL);
                    INSERT INTO sequence VALUES(3);
                    ALTER TABLE sequence ADD COLUMN cleared_through INTEGER NOT NULL DEFAULT 0;
                    CREATE TABLE snapshots(query_key TEXT PRIMARY KEY,sequence INTEGER NOT NULL,payload TEXT NOT NULL,previous_payload TEXT);
                    CREATE TABLE change_events(query_key TEXT NOT NULL,sequence INTEGER NOT NULL,application TEXT NOT NULL,observed_utc TEXT NOT NULL,payload TEXT NOT NULL,acknowledged_utc TEXT,PRIMARY KEY(query_key,sequence,application));
                    CREATE TABLE lookup_outcomes(query_key TEXT PRIMARY KEY,sequence INTEGER NOT NULL,outcome TEXT NOT NULL,observed_utc TEXT NOT NULL);
                    INSERT INTO watchlists VALUES('favorites','Favorites');
                    INSERT INTO entries VALUES('favorites','136910','Webster','');
                    PRAGMA user_version=2;
                    """;
                command.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
            var store = new PortfolioStore(folder, () => now);
            Assert.Equal("Webster", Assert.Single(store.Entries("favorites")).Name);
            using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "PRAGMA user_version";
                Assert.Equal(4, Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='frn_snapshots'";
                Assert.Equal("frn_snapshots", command.ExecuteScalar());
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='form470_revisions'";
                Assert.Equal("form470_revisions", command.ExecuteScalar());
            }

            var key = new LookupKey("136910", 2020, FilingForm.Form470);
            UsacRow Row(string status, string request, string? fcdl, string? revised, string? disbursed, string number = "2099010373", string? version = "Current") => new()
            {
                FundingRequestNumber = number,
                Number = "201009092",
                Ben = "136910",
                Name = "Webster",
                Status = status,
                FundingCommitmentRequest = request,
                FcdlLetterDate = fcdl ?? "",
                RevisedFcdlDate = revised ?? "",
                TotalAuthorizedDisbursement = disbursed ?? "",
                Version = version ?? ""
            };
            var seq = store.BeginObservation();
            Assert.True(store.SaveFrns(key, seq, new QueryResult<List<UsacRow>>(new List<UsacRow> { Row("Funded", "14908.32", "2020-05-09T00:00:00.000", null, "14908.32") }, now, false)));
            var baseline = store.ReadFrns(key);
            Assert.NotNull(baseline);
            Assert.Equal("14908.32", Assert.Single(baseline!.Frns).ReportedCommittedAmount);
            Assert.Empty(store.UnreadChanges(PortfolioStore.FrnKey(key)));
            Assert.False(store.SaveFrns(key, seq, new QueryResult<List<UsacRow>>(new List<UsacRow> { Row("Pending", "1", null, null, null) }, now, true)));
            Assert.Equal("Funded", store.ReadFrns(key)!.Frns[0].FrnStatus);

            var again = store.BeginObservation();
            Assert.True(store.SaveFrns(key, again, new QueryResult<List<UsacRow>>(new List<UsacRow> { Row("Funded", "14908.32", "2020-05-09T00:00:00.000", null, "14908.32", "2099010373", "Current") }, now.AddMinutes(1), false)));
            Assert.Empty(store.UnreadChanges(PortfolioStore.FrnKey(key)));

            var changed = store.BeginObservation();
            Assert.True(store.SaveFrns(key, changed, new QueryResult<List<UsacRow>>(new List<UsacRow>
            {
                Row("Funded", "14000", "2020-05-10T00:00:00.000", "2026-07-14T00:00:00.000", null),
                Row("Pending", "10", null, null, null, "1799022629")
            }, now.AddMinutes(2), false)));
            var unread = store.UnreadChanges(PortfolioStore.FrnKey(key));
            Assert.Equal(new[] { "Disbursed amount", "FCDL date", "New FRN", "Reported committed amount", "Revised FCDL date" }, unread.Select(item => item.Change.Kind).Order(StringComparer.Ordinal).ToArray());
            Assert.All(unread, item => Assert.StartsWith("FRN ", item.Change.Application, StringComparison.Ordinal));
            Assert.DoesNotContain(unread, item => item.Change.Kind.Contains("decision", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("", unread.Single(item => item.Change.Kind == "Disbursed amount").Change.After);
            var repeat = store.BeginObservation();
            store.SaveFrns(key, repeat, new QueryResult<List<UsacRow>>(new List<UsacRow>
            {
                Row("Funded", "14000", "2020-05-10T00:00:00.000", "2026-07-14T00:00:00.000", null),
                Row("Pending", "10", null, null, null, "1799022629")
            }, now.AddMinutes(3), false));
            Assert.Equal(unread.Count, store.UnreadChanges(PortfolioStore.FrnKey(key)).Count);
            store.SaveFrns(key, store.BeginObservation(), new QueryResult<List<UsacRow>>(new List<UsacRow> { Row("Funded", "14000", "2020-05-10T00:00:00.000", "2026-07-14T00:00:00.000", null) }, now.AddMinutes(4), false));
            Assert.Contains(store.UnreadChanges(PortfolioStore.FrnKey(key)), item => item.Change.Kind == "FRN no longer returned by this query");
            Assert.Throws<InvalidOperationException>(() => FrnSnapshot.FromRows("136910", new[] { Row("Funded", "1", null, null, null, "1", "Original") }));
            var wrongBen = Row("Funded", "1", null, null, null);
            wrongBen.Ben = "999";
            Assert.Throws<InvalidOperationException>(() => FrnSnapshot.FromRows("136910", new[] { wrongBen }));
            Assert.Empty(FrnSnapshot.FromRows("136910", new[] { new UsacRow { FundingRequestNumber = "  ", Ben = "136910", Status = "Funded", FundingCommitmentRequest = "5" } }));

            var reopened = new PortfolioStore(folder, () => now);
            Assert.Equal("14000", Assert.Single(reopened.ReadFrns(key)!.Frns).ReportedCommittedAmount);
            reopened.AcknowledgeFrns(key, changed);
            Assert.Contains(reopened.UnreadChanges(PortfolioStore.FrnKey(key)), item => item.Change.Kind == "FRN no longer returned by this query");
            reopened.AcknowledgeFrns(key, reopened.ReadFrns(key)!.Sequence);
            Assert.Empty(reopened.UnreadChanges(PortfolioStore.FrnKey(key)));
            Assert.NotNull(reopened.ReadFrns(key));
            var inFlight = reopened.BeginObservation();
            reopened.ClearHistory();
            Assert.Null(reopened.ReadFrns(key));
            Assert.Contains(reopened.Lists(), list => list.Name == "Favorites");
            Assert.False(reopened.SaveFrns(key, inFlight, new QueryResult<List<UsacRow>>(new List<UsacRow> { Row("Funded", "1", null, null, null) }, now, false)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task RefreshSurfacesReportedFrnChangesWithoutChangingExportColumns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERateFrnSession-" + Guid.NewGuid());
        try
        {
            var store = new PortfolioStore(folder);
            var handler = new FrnRefreshHandler();
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, Path.Combine(folder, "cache"));
            var session = new PortfolioSession(client, store);
            var key = new LookupKey("136910", 2020, FilingForm.Form471);
            await session.RefreshAsync(new[] { key }, null, default);
            Assert.Equal("Success", Assert.Single(session.Rows).Outcome);
            Assert.Equal("", session.Rows[0].Change);
            Assert.DoesNotContain("FRN ", session.ChangeHistory(), StringComparison.Ordinal);

            handler.Frns = """
                [
                  {"funding_request_number":"2099010373","application_number":"201009092","billed_entity_number":"136910","billed_entity_name":"Webster","filing_status":"Funded","funding_commitment_request":"14000.00","fcdl_letter_date":"2020-05-10T00:00:00.000","revised_fcdl_date":"2026-07-14T00:00:00.000","total_authorized_disbursement":"10","form_version":"Current"},
                  {"funding_request_number":"1799022629","application_number":"171000099","billed_entity_number":"136910","billed_entity_name":"Webster","filing_status":"Funded","funding_commitment_request":"10","form_version":"Current"}
                ]
                """;
            await session.RefreshAsync(new[] { key }, null, default);
            var filing = session.Rows.Single(row => row.Outcome == "Success");
            Assert.Contains("Reported committed amount", filing.Change, StringComparison.Ordinal);
            Assert.Contains("FCDL date", filing.Change, StringComparison.Ordinal);
            Assert.Contains("Revised FCDL date", filing.Change, StringComparison.Ordinal);
            Assert.Contains("Disbursed amount", filing.Change, StringComparison.Ordinal);
            Assert.Contains(FrnCommitment.ReportedAmountAssumption, filing.Note, StringComparison.Ordinal);
            Assert.Contains("FRN 2099010373", filing.Display, StringComparison.Ordinal);
            var added = session.Rows.Single(row => row.Outcome == "FrnReported");
            Assert.Contains("FRN status", added.Change, StringComparison.Ordinal);
            Assert.Contains("Reported committed amount", added.Change, StringComparison.Ordinal);
            Assert.Equal("1799022629", added.FundingRequestNumber);
            Assert.Contains("FRN 1799022629", added.Display, StringComparison.Ordinal);
            var history = session.ChangeHistory();
            Assert.Contains(FrnCommitment.ReportedAmountAssumption, history, StringComparison.Ordinal);
            Assert.DoesNotContain("now committed", history, StringComparison.OrdinalIgnoreCase);

            session.Selected.Add(filing.Id);
            session.Selected.Add(added.Id);
            var exported = session.CreateExport();
            Assert.Equal(ExportSchema.Columns(new ExportColumns { IncludePortfolio = true }), exported.Table[0]);
            Assert.DoesNotContain("FrnStatus", exported.Table[0]);
            Assert.DoesNotContain("CommittedAmount", exported.Table[0]);
            Assert.DoesNotContain("DisbursedAmount", exported.Table[0]);
            var filingRow = exported.Table[1];
            Assert.Equal("", filingRow[Array.IndexOf(exported.Table[0], "FundingRequestNumber")]);
            Assert.Contains("Reported committed amount", filingRow[Array.IndexOf(exported.Table[0], "Change")], StringComparison.Ordinal);
            var frnRow = exported.Table[2];
            Assert.Equal("1799022629", frnRow[Array.IndexOf(exported.Table[0], "FundingRequestNumber")]);
            Assert.Equal("FrnReported", frnRow[Array.IndexOf(exported.Table[0], "LookupOutcome")]);
            Assert.DoesNotContain("committedAmount", System.Text.Json.JsonSerializer.Serialize(exported.JsonRows), StringComparison.Ordinal);
            Assert.Equal(2, session.Filter("", "", true).Count);

            session.AcknowledgeDisplayed();
            Assert.All(session.Rows, row => Assert.Equal("", row.Change));
            Assert.Equal("No unread changes in the displayed observations.", session.ChangeHistory());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }
    sealed class FrnRefreshHandler : HttpMessageHandler
    {
        public string Filings = """[{"application_number":"201009092","epc_organization_id":"136910","billed_entity_number":"136910","billed_entity_name":"Webster","form_version":"Current","filing_status":"Committed","certified_date_time":"2020-01-01T00:00:00.000","nickname":"N"}]""";
        public string Frns = """
            [
              {"funding_request_number":"2099010373","application_number":"201009092","billed_entity_number":"136910","billed_entity_name":"Webster","filing_status":"Funded","funding_commitment_request":"14908.32","fcdl_letter_date":"2020-05-09T00:00:00.000","total_authorized_disbursement":"14908.32","form_version":"Current"},
              {"funding_request_number":"1799022629","application_number":"171000099","billed_entity_number":"136910","billed_entity_name":"Webster","filing_status":"Pending","funding_commitment_request":"10","form_version":"Current"}
            ]
            """;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            var query = Uri.UnescapeDataString(uri.Query);
            string body;
            if (path.Contains("/api/views/", StringComparison.Ordinal))
                body = """{"rowsUpdatedAt":1}""";
            else if (query.Contains("count(*)", StringComparison.Ordinal))
            {
                var count = path.Contains("qdmp-ygft", StringComparison.Ordinal)
                    ? System.Text.Json.JsonDocument.Parse(Frns).RootElement.GetArrayLength()
                    : System.Text.Json.JsonDocument.Parse(Filings).RootElement.GetArrayLength();
                body = "[{\"count\":\"" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}]";
            }
            else
                body = path.Contains("qdmp-ygft", StringComparison.Ordinal) ? Frns : Filings;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    sealed class PortfolioHandler : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Count++;
            var uri = request.RequestUri!;
            var query = Uri.UnescapeDataString(uri.Query);
            if (query.Contains("billed_entity_number='2'")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            if (uri.AbsolutePath.Contains("/api/views/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"rowsUpdatedAt\":1}", Encoding.UTF8, "application/json") });
            if (query.Contains("count(*)", StringComparison.Ordinal))
            {
                var count = query.Contains("billed_entity_number='3'") ? "0" : "1";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"count\":\"" + count + "\"}]", Encoding.UTF8, "application/json") });
            }

            var body = query.Contains("billed_entity_number='3'") ? "[]" : """[{"application_number":"10","billed_entity_number":"1","billed_entity_name":"District","fcc_form_470_status":"Certified","function":"Wireless"}]""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    sealed class RetryHandler : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var query = Uri.UnescapeDataString(uri.Query);
            var body = uri.AbsolutePath.Contains("/api/views/", StringComparison.Ordinal)
                ? "{\"rowsUpdatedAt\":1}"
                : query.Contains("count(*)", StringComparison.Ordinal) ? "[{\"count\":\"0\"}]" : "[]";
            var response = new HttpResponseMessage(++Count < 3 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero); return Task.FromResult(response);
        }
    }
}
