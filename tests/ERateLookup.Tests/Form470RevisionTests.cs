using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ERateLookup.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ERateLookup.Tests;

public class Form470RevisionTests
{
    [Fact]
    public void ObservationDiffReusesPublishedFieldMatching()
    {
        var original = new[]
        {
            Row("Original", "1", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null),
            Row("Original", "3", "Cabling", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null)
        };
        var current = new[]
        {
            Row("Current", "1", "Wireless", "Canceled", "bea@example.org", "2026-10-06T00:00:00.000", "https://example.test/addendum.pdf"),
            Row("Current", "4", "WAN", "Canceled", "bea@example.org", "2026-10-06T00:00:00.000", null)
        };

        var published = Form470Diff.Compare(original.Concat(current));
        var observed = Form470Diff.CompareObservations(original, current);

        Assert.Equal(published.Select(Signature), observed.Select(Signature));
        Assert.Contains(observed, change => change.Change == Form470Diff.Added && change.ServiceRequestId == "4" && change.Current.Contains("WAN", StringComparison.Ordinal));
        Assert.Contains(observed, change => change.Change == Form470Diff.Removed && change.ServiceRequestId == "3" && change.Original.Contains("Cabling", StringComparison.Ordinal));
        Assert.Contains(observed, change => change.Change == Form470Diff.Changed && change.Field == "function" && change.Original == "Switches" && change.Current == "Wireless");
        Assert.Contains(observed, change => change.Field == "fcc_form_470_status" && change.Original == "Certified" && change.Current == "Canceled");
        Assert.Contains(observed, change => change.Field == "allowable_contract_date");
        Assert.Contains(observed, change => change.Field == "rfp_documents" && change.Current.Contains("addendum.pdf", StringComparison.Ordinal));
    }

    [Fact]
    public void UnchangedObservationsDoNotReportDifferences()
    {
        var left = new List<UsacRow>
        {
            Row("Current", "2", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", "https://example.test/b.pdf"),
            Row("Current", "1", "Internet", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", "https://example.test/a.pdf"),
            null!
        };
        var right = new List<UsacRow>
        {
            Row("Current", "1", "Internet", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", "https://example.test/a.pdf"),
            Row("Current", "2", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", "https://example.test/b.pdf")
        };
        right[1].InstallationInitial = "false";
        right[1].RfpIdentifier = "false";
        var missingService = Row("Current", "", "Ignored", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null);
        missingService.ServiceRequestId = null!;
        right.Add(missingService);

        Assert.Empty(Form470Diff.CompareObservations(left, right));
        Assert.Empty(Form470Revisions.Differences(left, right));

        var omitted = JsonSerializer.Deserialize<UsacRow>("""{"application_number":"100","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Current","form_nickname":"FY27","fcc_form_470_status":"Certified","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-08-18T22:10:31.000","allowable_contract_date":"2026-09-15T00:00:00.000","contact_name":"Ada","contact_email":null,"service_request_id":"1","service_category":"Category 2","service_type":"Internal Connections","function":"Internet","service_request_rfp_attachment":"0","rfp_documents":null,"installation_initial":null}""")!;
        var blank = Row("Current", "1", "Internet", "Certified", "", "2026-09-15T00:00:00.000", null);
        blank.ContactEmail = null!;
        blank.InstallationInitial = "";
        Assert.Empty(Form470Revisions.Differences([omitted], [blank]));
        Assert.Empty(Form470Revisions.Differences([blank], [omitted]));
        Assert.Contains("status (blank)", Form470Revisions.RevisionSummary(new Form470Revision("100", "16057656", "Example", 2027, "16057656/2027/470", 1, DateTimeOffset.UnixEpoch, [new UsacRow { Number = "100", Version = "Current" }])), StringComparison.Ordinal);
    }

    [Fact]
    public void RevisionHistoryMigratesFromV3AndSurvivesRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERate470Rev-" + Guid.NewGuid().ToString("N"));
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
                    CREATE TABLE sequence(value INTEGER NOT NULL, cleared_through INTEGER NOT NULL DEFAULT 0);
                    INSERT INTO sequence VALUES(4, 0);
                    CREATE TABLE snapshots(query_key TEXT PRIMARY KEY,sequence INTEGER NOT NULL,payload TEXT NOT NULL,previous_payload TEXT);
                    CREATE TABLE change_events(query_key TEXT NOT NULL,sequence INTEGER NOT NULL,application TEXT NOT NULL,observed_utc TEXT NOT NULL,payload TEXT NOT NULL,acknowledged_utc TEXT,PRIMARY KEY(query_key,sequence,application));
                    CREATE TABLE lookup_outcomes(query_key TEXT PRIMARY KEY,sequence INTEGER NOT NULL,outcome TEXT NOT NULL,observed_utc TEXT NOT NULL);
                    CREATE TABLE frn_snapshots(
                      query_key TEXT NOT NULL,
                      funding_request_number TEXT NOT NULL,
                      application_number TEXT NOT NULL,
                      name TEXT NOT NULL,
                      frn_status TEXT NOT NULL,
                      reported_committed_amount TEXT NOT NULL,
                      fcdl_date TEXT NOT NULL,
                      revised_fcdl_date TEXT NOT NULL,
                      disbursed_amount TEXT NOT NULL,
                      sequence INTEGER NOT NULL,
                      observed_utc TEXT NOT NULL,
                      PRIMARY KEY(query_key, funding_request_number));
                    CREATE TABLE frn_observations(query_key TEXT PRIMARY KEY, sequence INTEGER NOT NULL, observed_utc TEXT NOT NULL);
                    INSERT INTO watchlists VALUES('favorites','Favorites');
                    INSERT INTO entries VALUES('favorites','16057656','Example','');
                    INSERT INTO frn_snapshots VALUES('16057656/2020/471','2099010373','201009092','Webster','Funded','14908.32','2020-05-09T00:00:00.000','','14908.32',4,'2026-10-27T12:00:00.0000000+00:00');
                    INSERT INTO frn_observations VALUES('16057656/2020/471',4,'2026-10-27T12:00:00.0000000+00:00');
                    PRAGMA user_version=3;
                    """;
                command.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            var now = new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero);
            var store = new PortfolioStore(folder, () => now);
            using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "PRAGMA user_version";
                Assert.Equal(4, Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture));
                command.CommandText = "SELECT reported_committed_amount FROM frn_snapshots";
                Assert.Equal("14908.32", command.ExecuteScalar());
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='form470_revisions'";
                Assert.Equal("form470_revisions", command.ExecuteScalar());
            }

            Assert.Equal("Example", Assert.Single(store.Entries("favorites")).Name);
            var key = new LookupKey("16057656", 2027, FilingForm.Form470);
            Assert.Throws<ArgumentException>(() => store.SaveForm470Revisions(new LookupKey("16057656", 2027, FilingForm.Form471), 1, Live([Row("Current", "1", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null)], now)));
            Assert.False(store.SaveForm470Revisions(key, store.BeginObservation(), new QueryResult<List<UsacRow>>(BaselineRows(), now, true)));
            Assert.Empty(store.ReadForm470Revisions("100"));

            var first = store.BeginObservation();
            Assert.True(store.SaveForm470Revisions(key, first, Live(BaselineRows(), now)));
            Assert.Empty(store.UnreadChanges(key));
            var repeated = store.BeginObservation();
            Assert.True(store.SaveForm470Revisions(key, repeated, Live(BaselineRows().AsEnumerable().Reverse().ToList(), now.AddMinutes(1))));
            Assert.Empty(store.UnreadChanges(key));
            Assert.Single(store.ReadForm470Revisions("100"));

            var changedAt = now.AddMinutes(2);
            var changed = store.BeginObservation();
            Assert.True(store.SaveForm470Revisions(key, changed, Live(ChangedRows(), changedAt)));
            var unread = store.UnreadChanges(key);
            Assert.Contains(unread, item => item.Change.Kind == "Added · Current · service" && item.Change.After.Contains("WAN", StringComparison.Ordinal));
            Assert.Contains(unread, item => item.Change.Kind == "Removed · Current · service" && item.Change.Before.Contains("Cabling", StringComparison.Ordinal));
            Assert.Contains(unread, item => item.Change.Kind == "Changed · Current · fcc_form_470_status" && item.Change.Before == "Certified" && item.Change.After == "Canceled");
            Assert.Contains(unread, item => item.Change.Kind == "Changed · Current · allowable_contract_date");
            Assert.Contains(unread, item => item.Change.Kind == "Changed · Current · rfp_documents" && item.Change.After.Contains("addendum.pdf", StringComparison.Ordinal));
            Assert.All(unread, item => Assert.Equal("Form 470 100", item.Change.Application));
            Assert.DoesNotContain(unread, item => item.Change.Kind.Contains("substantial", StringComparison.OrdinalIgnoreCase));
            var again = store.BeginObservation();
            store.SaveForm470Revisions(key, again, Live(ChangedRows(), now.AddMinutes(3)));
            Assert.Equal(unread.Count, store.UnreadChanges(key).Count);

            var removed = store.BeginObservation();
            Assert.True(store.SaveForm470Revisions(key, removed, Live([], now.AddMinutes(4))));
            Assert.Contains(store.UnreadChanges(key), item => item.Change.Kind == Form470Revisions.NotReturnedKind && item.Change.After == "");
            Assert.False(store.SaveForm470Revisions(key, first, Live(BaselineRows(), now)));

            var blank = Row("Current", "1", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null);
            blank.Number = "  ";
            Assert.True(store.SaveForm470Revisions(key, store.BeginObservation(), Live([blank], now.AddMinutes(5))));
            Assert.Empty(store.ReadForm470Revisions(""));
            var wrongBen = Row("Current", "9", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null);
            wrongBen.Ben = "999";
            wrongBen.Number = "101";
            Assert.Throws<InvalidOperationException>(() => store.SaveForm470Revisions(key, store.BeginObservation(), Live([wrongBen], now.AddMinutes(6))));

            var reopened = new PortfolioStore(folder, () => now);
            var history = reopened.ReadForm470Revisions("100");
            Assert.Equal(new[] { first, changed, removed }, history.Select(revision => revision.Sequence).ToArray());
            Assert.Equal("Example Schools", history[0].Name);
            Assert.Equal(2027, history[0].FundingYear);
            Assert.Equal(changedAt, history[1].ObservedAt);
            var compared = Form470Revisions.ViewText("100", history.Take(2).Reverse().ToList(), "FY27");
            Assert.Contains(Form470Diff.Disclaimer, compared, StringComparison.Ordinal);
            Assert.Contains("Added · Service · 4 · Current · service", compared, StringComparison.Ordinal);
            Assert.Contains("Removed · Service · 3 · Current · service", compared, StringComparison.Ordinal);
            Assert.Contains("Changed · Application · - · Current · fcc_form_470_status", compared, StringComparison.Ordinal);
            Assert.Contains("[Derived] Changes from observation 1 to 2", compared, StringComparison.Ordinal);
            var text = Form470Revisions.ViewText("100", history.AsEnumerable().Reverse().ToList(), "FY27");
            var oldest = text.IndexOf("1 · sequence " + first.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var middle = text.IndexOf("2 · sequence " + changed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var newest = text.IndexOf("3 · sequence " + removed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.True(oldest >= 0 && oldest < middle && middle < newest);
            Assert.Contains("oldest first", text, StringComparison.Ordinal);
            Assert.Contains("[Derived]", text, StringComparison.Ordinal);
            Assert.Contains(Form470Revisions.NotReturnedKind, text, StringComparison.Ordinal);
            Assert.Contains("does not establish withdrawal", text, StringComparison.Ordinal);
            Assert.Contains("Nickname: FY27", text, StringComparison.Ordinal);
            Assert.DoesNotContain("USAC revision log.", text.Split(Form470Revisions.Disclaimer)[0], StringComparison.Ordinal);
            var summary = Form470Revisions.SelectionSummary("100", history, "FY27");
            Assert.Contains("[Derived]", summary, StringComparison.Ordinal);
            Assert.Contains("3 locally observed revision", summary, StringComparison.Ordinal);

            reopened.Acknowledge(key, removed);
            Assert.Empty(reopened.UnreadChanges(key));
            Assert.Equal(3, reopened.ReadForm470Revisions("100").Count);
            var inFlight = reopened.BeginObservation();
            reopened.ClearHistory();
            Assert.Empty(reopened.ReadForm470Revisions("100"));
            Assert.Contains(reopened.Lists(), list => list.Name == "Favorites");
            Assert.False(reopened.SaveForm470Revisions(key, inFlight, Live(BaselineRows(), now)));
            using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM frn_snapshots";
                Assert.Equal(0, Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task RefreshShowsUnreadRevisionChangesWithoutNewExportColumns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERate470Session-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PortfolioStore(folder);
            var handler = new RevisionHandler();
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, Path.Combine(folder, "cache"));
            var session = new PortfolioSession(client, store);
            var key = new LookupKey("16057656", 2027, FilingForm.Form470);
            await session.RefreshAsync([key], null, default);
            var filing = Assert.Single(session.Rows, row => row.Outcome == "Success");
            Assert.Equal("", filing.Change);
            Assert.Contains("locally observed Form 470 revision", filing.Note, StringComparison.Ordinal);
            Assert.DoesNotContain("Changed", session.ChangeHistory(), StringComparison.Ordinal);
            Assert.Single(store.ReadForm470Revisions("270000266"));

            handler.Changed = true;
            await session.RefreshAsync([key], null, default);
            filing = Assert.Single(session.Rows, row => row.Outcome == "Success");
            Assert.Contains("Changed · Current · fcc_form_470_status", filing.Change, StringComparison.Ordinal);
            Assert.Contains("Added · Current · service", filing.Change, StringComparison.Ordinal);
            Assert.Contains("Removed · Current · service", filing.Change, StringComparison.Ordinal);
            Assert.Contains("Changed · Current · rfp_documents", filing.Change, StringComparison.Ordinal);
            var history = session.ChangeHistory();
            Assert.Contains(Form470Revisions.Disclaimer, history, StringComparison.Ordinal);
            Assert.Contains("Before: Certified", history, StringComparison.Ordinal);
            Assert.Contains("After: Canceled", history, StringComparison.Ordinal);
            Assert.Equal(2, store.ReadForm470Revisions("270000266").Count);

            await session.RefreshAsync([key], null, default);
            Assert.Equal(2, store.ReadForm470Revisions("270000266").Count);
            session.Selected.Add(session.Rows.Single(row => row.Outcome == "Success").Id);
            var exported = session.CreateExport();
            Assert.Equal(ExportSchema.Columns(new ExportColumns { IncludePortfolio = true }), exported.Table[0]);
            Assert.Equal(ExportSchema.BaseColumns.Count + 4, exported.Table[0].Length);
            Assert.DoesNotContain(exported.Table[0], column => column.Contains("Revision", StringComparison.Ordinal));
            session.AcknowledgeDisplayed();
            Assert.Equal("", session.Rows.Single(row => row.Outcome == "Success").Change);
            Assert.Equal("No unread changes in the displayed observations.", session.ChangeHistory());
            Assert.Equal(2, new PortfolioStore(folder).ReadForm470Revisions("270000266").Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    static string Signature(Form470FieldChange change) => string.Join("|", change.Change, change.Scope, change.ServiceRequestId, change.Field, change.Original, change.Current);

    static QueryResult<List<UsacRow>> Live(List<UsacRow> rows, DateTimeOffset observed) => new(rows, observed, false);

    static List<UsacRow> BaselineRows() =>
    [
        Row("Original", "1", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null),
        Row("Original", "3", "Cabling", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null),
        Row("Current", "1", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null),
        Row("Current", "3", "Cabling", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null)
    ];

    static List<UsacRow> ChangedRows() =>
    [
        Row("Original", "1", "Switches", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null),
        Row("Original", "3", "Cabling", "Certified", "ada@example.org", "2026-09-15T00:00:00.000", null),
        Row("Current", "1", "Switches", "Canceled", "bea@example.org", "2026-10-06T00:00:00.000", "https://example.test/addendum.pdf"),
        Row("Current", "4", "WAN", "Canceled", "bea@example.org", "2026-10-06T00:00:00.000", null)
    ];

    static UsacRow Row(string version, string serviceId, string function, string status, string? email, string allowableContractDate, string? rfpUrl) => new()
    {
        Number = "100",
        Ben = "16057656",
        Name = "Example Schools",
        Version = version,
        ServiceRequestId = serviceId,
        Category = "Category 2",
        Service = "Internal Connections",
        Function = function,
        Nickname = "FY27",
        Status470 = status,
        Certified = "2026-08-18T22:10:31.000",
        Modified = "2026-08-18T22:10:31.000",
        AllowableContractDate = allowableContractDate,
        ContactName = "Ada",
        ContactEmail = email ?? "",
        RfpAttachmentCount = rfpUrl is null ? "0" : "1",
        RfpDocuments = rfpUrl is null ? null : new UrlField { Url = rfpUrl }
    };

    sealed class RevisionHandler : HttpMessageHandler
    {
        public bool Changed { get; set; }

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
                    ? 0
                    : query.Contains("fcc_form_470_status='Certified'", StringComparison.Ordinal) ? 1 : 4;
                body = "[{\"count\":\"" + count.ToString(CultureInfo.InvariantCulture) + "\"}]";
            }
            else if (path.Contains("qdmp-ygft", StringComparison.Ordinal))
                body = "[]";
            else if (query.Contains("$select=", StringComparison.Ordinal))
                body = Changed ? ChangedJson : BaselineJson;
            else
                body = FilingJson;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        const string FilingJson = """[{"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Current","fcc_form_470_status":"Certified","certified_date_time":"2026-08-18T22:10:31.000","form_nickname":"FY27","service_category":"Category 2","service_type":"Internal Connections","function":"Switches"}]""";

        const string BaselineJson = """
            [
              {"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Original","form_nickname":"FY27","fcc_form_470_status":"Certified","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-08-18T22:10:31.000","allowable_contract_date":"2026-09-15T00:00:00.000","service_request_id":"1","service_category":"Category 2","service_type":"Internal Connections","function":"Switches","contact_email":"ada@example.org"},
              {"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Original","form_nickname":"FY27","fcc_form_470_status":"Certified","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-08-18T22:10:31.000","allowable_contract_date":"2026-09-15T00:00:00.000","service_request_id":"3","service_category":"Category 2","service_type":"Internal Connections","function":"Cabling","contact_email":"ada@example.org"},
              {"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Current","form_nickname":"FY27","fcc_form_470_status":"Certified","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-08-18T22:10:31.000","allowable_contract_date":"2026-09-15T00:00:00.000","service_request_id":"1","service_category":"Category 2","service_type":"Internal Connections","function":"Switches","contact_email":"ada@example.org"},
              {"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Current","form_nickname":"FY27","fcc_form_470_status":"Certified","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-08-18T22:10:31.000","allowable_contract_date":"2026-09-15T00:00:00.000","service_request_id":"3","service_category":"Category 2","service_type":"Internal Connections","function":"Cabling","contact_email":"ada@example.org"}
            ]
            """;

        const string ChangedJson = """
            [
              {"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Original","form_nickname":"FY27","fcc_form_470_status":"Certified","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-08-18T22:10:31.000","allowable_contract_date":"2026-09-15T00:00:00.000","service_request_id":"1","service_category":"Category 2","service_type":"Internal Connections","function":"Switches","contact_email":"ada@example.org"},
              {"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Original","form_nickname":"FY27","fcc_form_470_status":"Certified","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-08-18T22:10:31.000","allowable_contract_date":"2026-09-15T00:00:00.000","service_request_id":"3","service_category":"Category 2","service_type":"Internal Connections","function":"Cabling","contact_email":"ada@example.org"},
              {"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Current","form_nickname":"FY27","fcc_form_470_status":"Canceled","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-10-06T00:00:00.000","allowable_contract_date":"2026-10-06T00:00:00.000","service_request_id":"1","service_category":"Category 2","service_type":"Internal Connections","function":"Switches","contact_email":"bea@example.org","service_request_rfp_attachment":"1","rfp_documents":{"url":"https://example.test/addendum.pdf"}},
              {"application_number":"270000266","billed_entity_number":"16057656","billed_entity_name":"Example Schools","form_version":"Current","form_nickname":"FY27","fcc_form_470_status":"Canceled","certified_date_time":"2026-08-18T22:10:31.000","last_modified_date_time":"2026-10-06T00:00:00.000","allowable_contract_date":"2026-10-06T00:00:00.000","service_request_id":"4","service_category":"Category 1","service_type":"Data Transmission and/or Internet Access","function":"WAN","contact_email":"bea@example.org"}
            ]
            """;
    }
}
