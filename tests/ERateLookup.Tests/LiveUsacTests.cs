using System.Text.Json;
using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Tests;

public sealed class LiveUsacFactAttribute : FactAttribute
{
    public LiveUsacFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ERATE_LIVE_USAC"), "1", StringComparison.Ordinal))
            Skip = "Live USAC checks run from the nightly workflow (ERATE_LIVE_USAC=1).";
    }
}

public class LiveUsacTests
{
    [LiveUsacFact]
    [Trait("Category", "Live")]
    public async Task PublishedFilingsForKnownCaliforniaBens()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateLive-" + Guid.NewGuid());
        try
        {
            using var liveHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var live = new UsacClient(liveHttp, Path.Combine(directory, "live"));
            var data = await live.GetFilingsAsync("133193", 2027, true, default);
            Assert.True(data.Data.Count > 0, "Live FY2027 query");
            var older471 = await live.GetFilingsAsync("106359", 2026, FilingForm.Form471, true, default);
            Assert.True(older471.Data.Count > 0, "Live California FY2026 Form 471 mapping");
            Assert.All(older471.Data, filing => Assert.Equal("106359", filing.Ben));
            var future471 = await live.GetFilingsAsync("106359", 2027, FilingForm.Form471, true, default);
            Assert.NotNull(future471.Data);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [LiveUsacFact]
    [Trait("Category", "Live")]
    public async Task FrnStatusCommitmentFieldsArePresentForKnownFrn()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var parameters = new Dictionary<string, string>
        {
            ["$select"] = UsacClient.FrnStatusSelect,
            ["$where"] = "funding_request_number='2099010373' AND form_version='Current'",
            ["$limit"] = "5"
        };
        var url = UsacClient.FrnStatusEndpoint + "?" + UsacClient.EncodeQuery(parameters);
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("2099010373", row.GetProperty("funding_request_number").GetString());
        Assert.Equal("Current", row.GetProperty("form_version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("filing_status").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("funding_commitment_request").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("funding_request_amount").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("fcdl_letter_date").GetString()));
        Assert.True(row.TryGetProperty("revised_fcdl_date", out _));
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("total_authorized_disbursement").GetString()));
    }
}
