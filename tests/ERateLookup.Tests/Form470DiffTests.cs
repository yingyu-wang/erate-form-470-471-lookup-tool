using System.Net;
using System.Text.Json;
using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Tests;

public class Form470DiffTests
{
    [Fact]
    public void AddedRemovedAndChangedServices()
    {
        var rows = new[]
        {
            Service("Original", "1", "Internet Access", "ada@example.org", "2026-09-15T00:00:00.000", "0", null),
            Service("Original", "2", "Switches", "ada@example.org", "2026-09-15T00:00:00.000", "0", null),
            Service("Original", "3", "Cabling", "ada@example.org", "2026-09-15T00:00:00.000", "0", null),
            Service("Current", "1", "Internet Access", "bea@example.org", "2026-10-06T00:00:00.000", "0", null),
            Service("Current", "2", "Wireless", "bea@example.org", "2026-10-06T00:00:00.000", "1", "https://example.test/addendum.pdf"),
            Service("Current", "4", "WAN", "bea@example.org", "2026-10-06T00:00:00.000", "0", null)
        };

        var diff = Form470Diff.Compare(rows);

        Assert.Contains(diff, change => change.Change == Form470Diff.Added && change.ServiceRequestId == "4" && change.Current.Contains("WAN", StringComparison.Ordinal));
        Assert.Contains(diff, change => change.Change == Form470Diff.Removed && change.ServiceRequestId == "3" && change.Original.Contains("Cabling", StringComparison.Ordinal));
        Assert.Contains(diff, change => change.Change == Form470Diff.Changed && change.ServiceRequestId == "2" && change.Field == "function" && change.Original == "Switches" && change.Current == "Wireless");
        Assert.Contains(diff, change => change.ServiceRequestId == "2" && change.Field == "service_request_rfp_attachment" && change.Original == "0" && change.Current == "1");
        Assert.Contains(diff, change => change.ServiceRequestId == "2" && change.Field == "rfp_documents" && change.Current.Contains("addendum.pdf", StringComparison.Ordinal));
        Assert.Contains(diff, change => change.Scope == Form470Diff.ApplicationScope && change.Field == "contact_email" && change.Original == "ada@example.org" && change.Current == "bea@example.org");
        Assert.Contains(diff, change => change.Field == "allowable_contract_date" && change.Original == "2026-09-15T00:00:00.000" && change.Current == "2026-10-06T00:00:00.000");
        Assert.DoesNotContain(diff, change => change.ServiceRequestId == "1");
    }

    [Fact]
    public void IdenticalVersionsAreAnEmptyDiff()
    {
        var diff = Form470Diff.Compare(
        [
            Document("Original", "https://example.test/a.pdf"),
            Document("Original", "https://example.test/b.pdf"),
            Document("Current", "https://example.test/b.pdf"),
            Document("Current", "https://example.test/a.pdf")
        ]);

        Assert.Empty(diff);
    }

    [Fact]
    public void OmittedCheckboxMatchesExplicitFalse()
    {
        var original = Service("Original", "1", "Switches", "ada@example.org", "2026-09-15T00:00:00.000", "0", null);
        var current = Service("Current", "1", "Switches", "ada@example.org", "2026-09-15T00:00:00.000", "0", null);
        current.InstallationInitial = "false";
        current.RfpIdentifier = "false";

        Assert.Empty(Form470Diff.Compare([original, current]));
    }

    [Fact]
    public void OnePublishedVersionIsAGapNotAServiceList()
    {
        var diff = Form470Diff.Compare([Service("Current", "1", "Switches", "ada@example.org", "2026-09-15T00:00:00.000", "0", null)]);

        var change = Assert.Single(diff);
        Assert.Equal(Form470Diff.VersionGap, change.Change);
        Assert.Equal(Form470Diff.NotPublished, change.Original);
        Assert.Equal(Form470Diff.CurrentVersion, change.Current);
    }

    [Fact]
    public void CanceledCurrentDropsTheServiceAndChangesStatus()
    {
        var original = Service("Original", "1", "Switches", "ada@example.org", "2026-09-15T00:00:00.000", "0", null);
        var current = Service("Current", "", "Switches", "ada@example.org", "2026-09-15T00:00:00.000", "0", null);
        current.ServiceRequestId = "";
        current.Category = "";
        current.Service = "";
        current.Function = "";
        current.Status470 = "Canceled";

        var diff = Form470Diff.Compare([original, current]);

        Assert.Contains(diff, change => change.Field == "fcc_form_470_status" && change.Original == "Certified" && change.Current == "Canceled");
        Assert.Contains(diff, change => change.Change == Form470Diff.Removed && change.ServiceRequestId == "1");
        Assert.DoesNotContain(diff, change => change.Change == Form470Diff.Added);
    }

    [Fact]
    public void CheckboxJsonDeserializesAsText()
    {
        var rows = JsonSerializer.Deserialize<List<UsacRow>>("""
            [{"application_number":"270000266","form_version":"Current","rfp_identifier":true,"installation_initial":false,"service_request_rfp_attachment":"1","rfp_documents":{"url":"https://example.test/a.pdf"}}]
            """);

        Assert.NotNull(rows);
        Assert.Equal("true", rows[0].RfpIdentifier);
        Assert.Equal("false", rows[0].InstallationInitial);
        Assert.Equal("1", rows[0].RfpAttachmentCount);
        Assert.Equal("https://example.test/a.pdf", rows[0].RfpDocuments!.Url);
    }

    [Fact]
    public void VersionFilterDoesNotRequireCertifiedStatus()
    {
        var filter = UsacClient.Form470VersionFilter("16057656", null, 2027, "270000266");

        Assert.Equal("billed_entity_number='16057656' AND funding_year='2027' AND application_number='270000266'", filter);
        Assert.DoesNotContain("fcc_form_470_status", filter, StringComparison.Ordinal);
        Assert.Contains("billed_entity_name", UsacClient.Form470VersionFilter(null, "Kipp", 2027, null), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionQueryReadsTheForm470Dataset()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateDiff-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new RecordingHandler("[]");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var result = await client.GetForm470VersionRowsAsync("16057656", null, 2027, "270000266", true, default);

            Assert.Empty(result.Data);
            var page = Assert.Single(handler.Urls, url => Uri.UnescapeDataString(url).Contains("$offset", StringComparison.Ordinal));
            var url = Uri.UnescapeDataString(page);
            Assert.Contains("jt8s-3q52", url, StringComparison.Ordinal);
            Assert.Contains("application_number='270000266'", url, StringComparison.Ordinal);
            Assert.Contains(UsacClient.Form470VersionOrder, url, StringComparison.Ordinal);
            Assert.Contains("service_request_id", url, StringComparison.Ordinal);
            Assert.DoesNotContain("fcc_form_470_status='Certified'", url, StringComparison.Ordinal);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    static UsacRow Service(string version, string serviceId, string function, string email, string allowableContractDate, string attachmentCount, string? rfpUrl) => new()
    {
        Number = "100",
        Ben = "16057656",
        Name = "Example Schools",
        Version = version,
        ServiceRequestId = serviceId,
        Category = serviceId == "4" ? "Category 1" : serviceId == "1" ? "Category 1" : "Category 2",
        Service = serviceId is "1" or "4" ? "Data Transmission and/or Internet Access" : "Internal Connections",
        Function = function,
        Nickname = "FY27",
        Status470 = "Certified",
        Certified = "2026-08-18T22:10:31.000",
        Modified = "2026-08-18T22:10:31.000",
        AllowableContractDate = allowableContractDate,
        ContactName = "Ada",
        ContactEmail = email,
        RfpAttachmentCount = attachmentCount,
        RfpDocuments = rfpUrl is null ? null : new UrlField { Url = rfpUrl }
    };

    static UsacRow Document(string version, string url) => new()
    {
        Number = "200",
        Ben = "16057656",
        Name = "Example Schools",
        Version = version,
        ServiceRequestId = "1",
        Category = "Category 1",
        Service = "Data Transmission and/or Internet Access",
        Function = "Internet Access and Data Transmission Service",
        Nickname = "Same",
        Status470 = "Certified",
        Certified = "2026-08-18T22:10:31.000",
        Modified = "2026-08-18T22:10:31.000",
        AllowableContractDate = "2026-09-15T00:00:00.000",
        ContactEmail = "ada@example.org",
        RfpAttachmentCount = "1",
        RfpIdentifier = "false",
        RfpDocuments = new UrlField { Url = url }
    };

    sealed class RecordingHandler : HttpMessageHandler
    {
        readonly string body;
        public List<string> Urls { get; } = new();

        public RecordingHandler(string body) => this.body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Urls.Add(uri.ToString());
            var query = Uri.UnescapeDataString(uri.Query);
            string payload;
            if (uri.AbsolutePath.Contains("/api/views/", StringComparison.Ordinal))
            {
                payload = "{\"rowsUpdatedAt\":1}";
            }
            else if (query.Contains("count(*)", StringComparison.Ordinal))
            {
                payload = "[{\"count\":\"0\"}]";
            }
            else
            {
                payload = body;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }
}
