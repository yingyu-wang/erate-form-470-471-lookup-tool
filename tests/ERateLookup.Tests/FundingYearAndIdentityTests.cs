using System.Net;
using System.Text;
using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Tests;

public class FundingYearTests
{
    [Theory]
    [InlineData("2026-06-30T23:59:00+00:00", 2025, 2026)]
    [InlineData("2026-07-01T00:00:00+00:00", 2026, 2027)]
    [InlineData("2026-10-14T12:00:00-07:00", 2026, 2027)]
    [InlineData("2027-01-15T00:00:00+00:00", 2026, 2027)]
    [InlineData("2027-07-01T00:00:00-04:00", 2027, 2028)]
    public void FundingYearStartsOnJuly1(string instant, int current, int next)
    {
        var clock = DateTimeOffset.Parse(instant);
        Assert.Equal(current, ERateCalendar.CurrentFundingYear(clock));
        Assert.Equal(next, ERateCalendar.NextFundingYear(clock));
    }

    [Fact]
    public async Task NewCycleQueryUsesClockInsteadOfHardCodedYears()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateYears-" + Guid.NewGuid());
        try
        {
            var now = new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory, () => now);
            var result = await client.HasNewCycleForm470Async("101552", true, default);
            Assert.False(result.Data);
            Assert.Contains("funding_year in ('2025','2026')", Uri.UnescapeDataString(handler.LastQuery));
            Assert.DoesNotContain("'2027'", Uri.UnescapeDataString(handler.LastQuery));

            now = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
            await client.HasNewCycleForm470Async("101552", true, default);
            var query = Uri.UnescapeDataString(handler.LastQuery);
            Assert.Contains("funding_year in ('2026','2027')", query);
            Assert.Equal((2026, 2027), client.NewCycleFundingYears());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public string LastQuery { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastQuery = request.RequestUri!.Query;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }
}

public class Form471IdentityTests
{
    [Fact]
    public async Task MatchingEpcOrganizationIdIsAccepted()
    {
        var outcome = await Lookup("101552", """[{"application_number":"471-1","epc_organization_id":"101552","billed_entity_name":"District","form_version":"Current","filing_status":"Committed","certified_date_time":"2026-03-01T00:00:00.000"}]""");
        var filing = Assert.Single(outcome.Data);
        Assert.Equal("101552", filing.Ben);
        Assert.Equal("Committed", filing.Status);
        Assert.Equal("2026-03-01", filing.Certified);
    }

    [Fact]
    public async Task MismatchedEpcOrganizationIdIsNotAnEmptyLookup()
    {
        var error = await Assert.ThrowsAsync<Form471IdentityMismatchException>(() => Lookup("101552", """[{"application_number":"471-9","epc_organization_id":"999999","billed_entity_number":"101552"}]"""));
        Assert.Contains("999999", error.Message);
        Assert.Contains("101552", error.Message);
        Assert.Contains("not treated as a filing", error.Message);
        Assert.Equal("471-9", error.ApplicationNumber);
    }

    [Fact]
    public async Task ReportedBenThatDiffersFromEpcIsRejected()
    {
        var error = await Assert.ThrowsAsync<Form471IdentityMismatchException>(() => Lookup("101552", """[{"application_number":"471-2","epc_organization_id":"101552","billed_entity_number":"888888"}]"""));
        Assert.Contains("888888", error.Message);
        Assert.Contains("epc_organization_id '101552'", error.Message);
    }

    [Fact]
    public async Task MissingEpcOrganizationIdIsRejectedOnEveryLookupIncludingCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERate471-" + Guid.NewGuid());
        try
        {
            var handler = new FixedHandler("""[{"application_number":"471-3","billed_entity_number":"101552"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            await Assert.ThrowsAsync<Form471IdentityMismatchException>(() => client.GetFilingsAsync("101552", 2026, FilingForm.Form471, true, default));
            await Assert.ThrowsAsync<Form471IdentityMismatchException>(() => client.GetFilingsAsync("101552", 2026, FilingForm.Form471, false, default));
            Assert.Equal(1, handler.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    static async Task<QueryResult<List<Filing>>> Lookup(string ben, string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERate471-" + Guid.NewGuid());
        try
        {
            using var http = new HttpClient(new FixedHandler(json));
            using var client = new UsacClient(http, directory);
            return await client.GetFilingsAsync(ben, 2026, FilingForm.Form471, true, default);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    sealed class FixedHandler(string json) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            var body = request.RequestUri!.AbsolutePath.Contains("9s6i-myen", StringComparison.Ordinal) ? json : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

public class CollapseDateTests
{
    [Fact]
    public void ModifiedAndCertifiedSortByParsedTimestamp()
    {
        var rows = new[]
        {
            new UsacRow { Number = "1", Version = "Current", Modified = "2026-10-02T00:00:00Z", Certified = "2026-10-02T00:00:00Z", Nickname = "iso", Function = "Older" },
            new UsacRow { Number = "1", Version = "Current", Modified = "10/03/2026 1:00:00 AM", Certified = "10/03/2026 1:00:00 AM", Nickname = "us", Function = "Newer" },
            new UsacRow { Number = "2", Version = "Current", Modified = "2026-01-15T00:00:00Z", Certified = "10/03/2026 12:00:00 AM", Function = "January" }
        };
        var collapsed = UsacClient.Collapse(rows);
        var first = Assert.Single(collapsed, filing => filing.Number == "1");
        Assert.Equal("us", first.Nickname);
        Assert.Equal("2026-10-03", first.Certified);
        Assert.Equal("Newer; Older", first.Services);
        Assert.Equal("1", collapsed[0].Number);
        Assert.Equal("2026-10-03", collapsed[1].Certified);
        Assert.Equal("not-a-date-value", UsacClient.Collapse([new UsacRow { Number = "9", Certified = "not-a-date-value" }]).Single().Certified);
    }

    [Fact]
    public void CertifiedOffsetKeepsTheOffsetCalendarDate()
    {
        var filing = Assert.Single(UsacClient.Collapse([new UsacRow { Number = "3", Certified = "2026-10-29T23:30:00-04:00", Version = "Current" }]));
        Assert.Equal("2026-10-29", filing.Certified);
    }
}

public class CacheMaintenanceTests
{
    [Fact]
    public async Task ExpiredRowsArePrunedAndOneConnectionServesConcurrentReads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateCache-" + Guid.NewGuid());
        try
        {
            var now = DateTimeOffset.Parse("2026-10-14T00:00:00Z");
            var handler = new CountingHandler();
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory, () => now);
            await client.QueryAsync(new Dictionary<string, string> { ["$limit"] = "1" }, false, default);
            await client.QueryAsync(new Dictionary<string, string> { ["$limit"] = "2" }, false, default);
            now = now.AddHours(2);
            await client.QueryAsync(new Dictionary<string, string> { ["$limit"] = "2" }, false, default);
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(directory, "cache.sqlite3") + ";Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM query_cache";
                Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
            }

            var tasks = Enumerable.Range(0, 6).Select(index => client.QueryAsync(new Dictionary<string, string> { ["$limit"] = "9", ["i"] = index.ToString() }, false, default));
            var results = await Task.WhenAll(tasks);
            Assert.All(results, result => Assert.False(result.FromCache));
            Assert.True(handler.Count >= 8);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    sealed class CountingHandler : HttpMessageHandler
    {
        int count;
        public int Count => count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }
}

public class PortfolioClockTests
{
    [Fact]
    public void AcknowledgeUsesInjectedClock()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERateClock-" + Guid.NewGuid());
        try
        {
            var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var store = new PortfolioStore(folder, () => now);
            var key = new LookupKey("00123", 2027, FilingForm.Form470);
            var filing = new Filing("1", key.Ben, "District", "", "2026-01-01", "Wireless", "");
            Assert.True(store.SaveLive(key, store.BeginObservation(), new QueryResult<List<Filing>>([filing], now, false)));
            store.SaveLive(key, store.BeginObservation(), new QueryResult<List<Filing>>([filing with { Services = "Switches" }], now.AddMinutes(1), false));
            var unread = store.UnreadChanges(key);
            Assert.NotEmpty(unread);
            store.Acknowledge(key, unread.Max(change => change.Sequence));
            using var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "portfolio.sqlite3") + ";Pooling=False");
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT acknowledged_utc FROM change_events";
            Assert.Equal(now.ToString("O"), command.ExecuteScalar() as string);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }
}

public class LookupViewModelTests
{
    [Fact]
    public void BudgetCopyUsesClockFundingYears()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateBudget-" + Guid.NewGuid());
        try
        {
            var now = new DateTimeOffset(2027, 8, 1, 0, 0, 0, TimeSpan.Zero);
            using var http = new HttpClient(new EmptyHandler());
            using var client = new UsacClient(http, directory, () => now);
            var catalog = new CaliforniaDirectory(http, directory);
            catalog.Replace([], [new BudgetRow { Ben = "88", Name = "Forecast District", State = "CA", Cycle = "FY2026-2030", Version = "Confirmed", Total = "10", Committed = "0", Pending = "0", Available = "10" }], now);
            var screen = new LookupViewModel(client, catalog, new FilingWindow(http, directory), LookupSurface.Windows, () => now);
            var text = screen.BudgetText("88", 2027, false);
            Assert.Contains("2027 or 2028", text);
            Assert.Contains("USAC Confirmed status is preserved", text);
            Assert.DoesNotContain("2026 or 2027", text);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }
}
