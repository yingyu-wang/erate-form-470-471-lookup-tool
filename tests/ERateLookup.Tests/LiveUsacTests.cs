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

    [Fact]
    public void SubstantiveForm470PairIsChosenAfterEarlierCandidates()
    {
        var candidates = new List<(string Number, UsacRow[] Rows)>();
        for (var index = 1; index <= 11; index++)
        {
            var number = index.ToString(CultureInfo.InvariantCulture);
            candidates.Add(index % 2 == 0
                ? (number, [VersionRow(number, Form470Diff.CurrentVersion, "2026-02-01T00:00:00.000", "false")])
                : (number,
                [
                    VersionRow(number, Form470Diff.OriginalVersion, "2026-01-01T00:00:00.000", "false"),
                    VersionRow(number, Form470Diff.CurrentVersion, "2026-02-01T00:00:00.000", "false")
                ]));
        }

        candidates.Add(("12",
        [
            VersionRow("12", Form470Diff.OriginalVersion, "2026-01-01T00:00:00.000", "false"),
            VersionRow("12", Form470Diff.CurrentVersion, "2026-02-01T00:00:00.000", "true")
        ]));

        Form470FieldChange? difference = null;
        foreach (var candidate in candidates)
        {
            if (TrySelectSubstantiveChange(candidate.Number, candidate.Rows, out var change, out _))
            {
                difference = change;
                break;
            }
        }

        Assert.NotNull(difference);
        Assert.Equal("12", difference.ApplicationNumber);
        Assert.Equal("rfp_identifier", difference.Field);
        Assert.Equal("false", difference.Original);
        Assert.Equal("true", difference.Current);
    }

    [LiveUsacFact]
    [Trait("Category", "Live")]
    public async Task PublishedForm470OriginalAndCurrentRowsStillDiff()
    {
        // Application 270000266 published both versions through 2026-11-05 and only
        // Current rows after that. Page through applications that still have both.
        var directory = Path.Combine(Path.GetTempPath(), "ERateLive-" + Guid.NewGuid());
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            using var live = new UsacClient(http, Path.Combine(directory, "live"));
            Form470FieldChange? difference = null;
            var notes = new List<string>();
            var omittedNotes = 0;
            var sawPair = false;
            for (var offset = 0; difference is null; offset += PairPageSize)
            {
                var pairs = await FindPairedForm470ApplicationsAsync(http, offset);
                if (pairs.Count == 0)
                    break;
                sawPair = true;
                foreach (var pair in pairs)
                {
                    var versions = await live.GetForm470VersionRowsAsync(pair.Ben, null, pair.Year, pair.Number, true, default);
                    if (TrySelectSubstantiveChange(pair.Number, versions.Data, out var change, out var detail))
                    {
                        difference = change;
                        break;
                    }

                    if (notes.Count < 30)
                        notes.Add("number=" + pair.Number + " FY" + pair.Year.ToString(CultureInfo.InvariantCulture) + " BEN " + pair.Ben + ": " + detail);
                    else
                        omittedNotes++;
                }

                if (pairs.Count < PairPageSize)
                    break;
            }

            if (!sawPair)
                Assert.Fail("USAC dataset jt8s-3q52 published no application with both Original and Current form_version rows.");
            if (difference is null)
            {
                var listed = notes.Count == 0 ? "(no candidate rows)" : string.Join("; ", notes);
                if (omittedNotes > 0)
                    listed += "; " + omittedNotes.ToString(CultureInfo.InvariantCulture) + " more candidates omitted";
                Assert.Fail("Paired Original and Current rows did not differ in any compared field other than last_modified_date_time. " + listed);
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

    const int PairPageSize = 100;

    sealed record PairedForm470(string Number, string Ben, int Year);

    static async Task<List<PairedForm470>> FindPairedForm470ApplicationsAsync(HttpClient http, int offset)
    {
        var parameters = new Dictionary<string, string>
        {
            ["$select"] = "application_number,billed_entity_number,funding_year",
            ["$group"] = "application_number,billed_entity_number,funding_year",
            ["$having"] = "count(distinct form_version) > 1",
            ["$order"] = "funding_year,application_number,billed_entity_number",
            ["$limit"] = PairPageSize.ToString(CultureInfo.InvariantCulture),
            ["$offset"] = offset.ToString(CultureInfo.InvariantCulture)
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

    static bool TrySelectSubstantiveChange(string applicationNumber, IReadOnlyList<UsacRow> rows, out Form470FieldChange? change, out string detail)
    {
        var hasOriginal = rows.Any(row => row.Number == applicationNumber && row.Version == Form470Diff.OriginalVersion);
        var hasCurrent = rows.Any(row => row.Number == applicationNumber && row.Version == Form470Diff.CurrentVersion);
        var sameApplication = rows.Count > 0 && rows.All(row => row.Number == applicationNumber);
        if (!hasOriginal || !hasCurrent || !sameApplication)
        {
            change = null;
            detail = "skipped " + DescribeRows(rows);
            return false;
        }

        var diff = Form470Diff.Compare(rows);
        if (diff.Any(row => row.Change == Form470Diff.VersionGap || row.Change == Form470Diff.Unexpected))
        {
            change = null;
            detail = "skipped " + DescribeRows(rows) + "; " + DescribeDiff(diff);
            return false;
        }

        change = diff.FirstOrDefault(row =>
            row.Field != "last_modified_date_time"
            && (row.Change == Form470Diff.Added || row.Change == Form470Diff.Removed || row.Change == Form470Diff.Changed));
        detail = DescribeRows(rows) + "; " + DescribeDiff(diff);
        return change is not null;
    }

    static UsacRow VersionRow(string number, string version, string modified, string rfpIdentifier) => new()
    {
        Number = number,
        Ben = "1",
        Name = "Example",
        Version = version,
        Modified = modified,
        Certified = "2026-01-01T00:00:00.000",
        Status470 = "Certified",
        AllowableContractDate = "2026-02-01T00:00:00.000",
        ServiceRequestId = "1",
        Category = "Category 2",
        Service = "Internal Connections",
        Function = "Switches",
        RfpIdentifier = rfpIdentifier,
        RfpAttachmentCount = "0"
    };

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
