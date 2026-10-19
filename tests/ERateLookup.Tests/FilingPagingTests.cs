using System.Net;
using System.Text;
using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Tests;

public class FilingPagingTests
{
    [Fact]
    public void Form471OrderUsesTheSocrataRowIdAsTieBreaker()
    {
        Assert.Equal("application_number,form_version,last_modified_timestamp,:id", UsacClient.FilingOrder(FilingForm.Form471));
        Assert.EndsWith(",:id", UsacClient.FilingOrder(FilingForm.Form471), StringComparison.Ordinal);
        Assert.Equal("application_number,service_request_id,service_request_rfp_attachment", UsacClient.FilingOrder(FilingForm.Form470));
    }

    [Fact]
    public async Task Form471PageRequestSendsTheTieBreaker()
    {
        var directory = Temp();
        try
        {
            var handler = new PagingHandler("""[{"application_number":"471-1","epc_organization_id":"101552","billed_entity_name":"District","form_version":"Current","filing_status":"Committed","certified_date_time":"2026-03-01T00:00:00.000"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var result = await client.GetFilingsAsync("101552", 2026, FilingForm.Form471, true, default);
            Assert.Equal("471-1", Assert.Single(result.Data).Number);
            var page = Assert.Single(handler.Urls, url => Uri.UnescapeDataString(url).Contains("$offset", StringComparison.Ordinal));
            Assert.Contains("$order=application_number,form_version,last_modified_timestamp,:id", Uri.UnescapeDataString(page), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task RowsUpdatedAtChangeDuringPagingFailsTheLookup()
    {
        var directory = Temp();
        try
        {
            var handler = new PagingHandler("""[{"application_number":"470-1","billed_entity_number":"101552","form_version":"Current"}]""", drift: true);
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetFilingsAsync("101552", 2026, true, default));
            Assert.Equal(UsacClient.PagingDriftMessage("jt8s-3q52", "10", "11"), error.Message);
            Assert.Equal(2, handler.ViewRequests);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task BroadNameSearchOverTheCapFailsBeforePaging()
    {
        var directory = Temp();
        try
        {
            var handler = new PagingHandler("[]", count: 50001);
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SearchFilingsAsync(null, "SCHOOL", 2026, FilingForm.Form470, true, default));
            Assert.Equal("50001 rows match; narrow the name or pass --ben", error.Message);
            Assert.Equal(UsacClient.BroadSearchLimitMessage(50001), error.Message);
            Assert.DoesNotContain(handler.Urls, url => url.Contains("/api/views/", StringComparison.Ordinal));
            Assert.DoesNotContain(handler.Urls, url => Uri.UnescapeDataString(url).Contains("$offset", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task BenLookupOverTheCapKeepsTheResultLimitMessage()
    {
        var directory = Temp();
        try
        {
            var handler = new PagingHandler("[]", count: 50001);
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetFilingsAsync("101552", 2026, true, default));
            Assert.Equal(UsacClient.ResultLimitMessage, error.Message);
            Assert.DoesNotContain("narrow the name", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    static string Temp() => Path.Combine(Path.GetTempPath(), "ERatePaging-" + Guid.NewGuid().ToString("N"));

    static void Cleanup(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    sealed class PagingHandler : HttpMessageHandler
    {
        readonly string page;
        readonly long count;
        readonly bool drift;
        public List<string> Urls { get; } = new();
        public int ViewRequests;

        public PagingHandler(string page, long count = 1, bool drift = false)
        {
            this.page = page;
            this.count = count;
            this.drift = drift;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Urls.Add(uri.ToString());
            var query = Uri.UnescapeDataString(uri.Query);
            string body;
            if (uri.AbsolutePath.Contains("/api/views/", StringComparison.Ordinal))
            {
                ViewRequests++;
                var stamp = drift && ViewRequests > 1 ? "11" : "10";
                body = "{\"rowsUpdatedAt\":" + stamp + "}";
            }
            else if (query.Contains("count(*)", StringComparison.Ordinal))
            {
                body = "[{\"count\":\"" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}]";
            }
            else
            {
                body = page;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
