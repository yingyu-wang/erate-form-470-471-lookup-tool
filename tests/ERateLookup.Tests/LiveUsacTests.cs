using System.Globalization;
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

    [LiveUsacFact]
    [Trait("Category", "Live")]
    public async Task PublishedForm470OriginalAndCurrentRowsStillDiff()
    {
        // Application 270000266 published both versions through 2026-11-05 and only
        // Current rows after that. Search for an application that still has both.
        var directory = Path.Combine(Path.GetTempPath(), "ERateLive-" + Guid.NewGuid());
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var pairs = await FindPairedForm470ApplicationsAsync(http);
            Assert.True(pairs.Count > 0, "USAC dataset jt8s-3q52 published no application with both Original and Current form_version rows.");
            using var live = new UsacClient(http, Path.Combine(directory, "live"));
            Form470FieldChange? difference = null;
            var notes = new List<string>();
            foreach (var pair in pairs)
            {
                var versions = await live.GetForm470VersionRowsAsync(pair.Ben, null, pair.Year, pair.Number, true, default);
                var described = DescribeRows(versions.Data);
                Assert.True(
                    versions.Data.Any(row => row.Number == pair.Number && row.Version == Form470Diff.OriginalVersion),
                    "Missing Original for " + pair.Number + " FY" + pair.Year.ToString(CultureInfo.InvariantCulture) + ". Rows: " + described);
                Assert.True(
                    versions.Data.Any(row => row.Number == pair.Number && row.Version == Form470Diff.CurrentVersion),
                    "Missing Current for " + pair.Number + " FY" + pair.Year.ToString(CultureInfo.InvariantCulture) + ". Rows: " + described);
                Assert.All(versions.Data, row => Assert.Equal(pair.Number, row.Number));
                var diff = Form470Diff.Compare(versions.Data);
                Assert.DoesNotContain(diff, change => change.Change == Form470Diff.VersionGap || change.Change == Form470Diff.Unexpected);
                var substantive = diff.FirstOrDefault(change =>
                    change.Field != "last_modified_date_time"
                    && (change.Change == Form470Diff.Added || change.Change == Form470Diff.Removed || change.Change == Form470Diff.Changed));
                notes.Add(pair.Number + " FY" + pair.Year.ToString(CultureInfo.InvariantCulture) + " BEN " + pair.Ben + ": " + DescribeDiff(diff));
                if (substantive is not null && difference is null)
                    difference = substantive;
            }

            if (difference is null)
            {
                Assert.Fail("Paired Original and Current rows did not differ in any compared field other than last_modified_date_time. " + string.Join("; ", notes));
            }

            Assert.False(string.IsNullOrWhiteSpace(difference.Field));
            Assert.NotEqual("last_modified_date_time", difference.Field);
            Assert.NotEqual(difference.Original, difference.Current);
            Assert.False(string.IsNullOrWhiteSpace(difference.ApplicationNumber));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    sealed record PairedForm470(string Number, string Ben, int Year);

    static async Task<List<PairedForm470>> FindPairedForm470ApplicationsAsync(HttpClient http)
    {
        var parameters = new Dictionary<string, string>
        {
            ["$select"] = "application_number,billed_entity_number,funding_year",
            ["$group"] = "application_number,billed_entity_number,funding_year",
            ["$having"] = "count(distinct form_version) > 1",
            ["$order"] = "funding_year,application_number",
            ["$limit"] = "10"
        };
        var url = UsacClient.Endpoint + "?" + UsacClient.EncodeQuery(parameters);
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var pairs = new List<PairedForm470>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var number = JsonText(row.GetProperty("application_number"));
            var ben = JsonText(row.GetProperty("billed_entity_number"));
            var yearText = JsonText(row.GetProperty("funding_year"));
            if (number.Length == 0 || ben.Length == 0 || !int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                continue;
            pairs.Add(new PairedForm470(number, ben, year));
        }

        return pairs;
    }

    static string JsonText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number => element.GetRawText(),
        _ => ""
    };

    static string DescribeRows(IReadOnlyList<UsacRow> rows) =>
        rows.Count == 0
            ? "(no rows)"
            : string.Join("; ", rows.Select(row => "number=" + row.Number + " version=" + row.Version + " service=" + row.ServiceRequestId));

    static string DescribeDiff(IReadOnlyList<Form470FieldChange> diff) =>
        diff.Count == 0
            ? "(no differences)"
            : string.Join(", ", diff.Select(change => change.Change + " " + change.Scope + " " + change.Field));
}
