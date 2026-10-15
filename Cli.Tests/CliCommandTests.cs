using System.Net;
using System.Text.Json;
using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Cli.Tests;

public class CliCommandTests
{
    [Fact]
    public void HelpDoesNotRequireAQuery()
    {
        var parsed = CliCommand.Parse(new[] { "--help" });
        Assert.True(parsed.Help);
        Assert.Null(parsed.Error);
        Assert.Contains("Exit codes:", CliCommand.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBenIsABadArgument()
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--fy", "2027" });
        Assert.False(parsed.Help);
        Assert.Null(parsed.Options);
        Assert.Contains("required", parsed.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvAndJsonTogetherAreRejected()
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2027", "--csv", "--json" });
        Assert.Contains("--csv", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FrnRequiresForm471()
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", "--frn", "2699003124" });
        Assert.Contains("471", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesTheDailyCheckShape()
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2027", "--csv", "--output", "chula.csv" });
        Assert.Null(parsed.Error);
        Assert.NotNull(parsed.Options);
        Assert.Equal(FilingForm.Form470, parsed.Options.Form);
        Assert.Equal("143618", parsed.Options.Ben);
        Assert.Equal(2027, parsed.Options.FundingYear);
        Assert.Equal(CliFormat.Csv, parsed.Options.Format);
        Assert.Equal("chula.csv", parsed.Options.OutputPath);
    }

    [Fact]
    public async Task TableReportsAPublishedApplication()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"260007116","billed_entity_number":"143618","billed_entity_name":"CHULA VISTA ELEM SCHOOL DIST","fcc_form_470_status":"Certified","form_nickname":"470 C2","certified_date_time":"2025-12-19T11:35:41.000","form_version":"Current"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = await CliCommand.RunAsync(Options(FilingForm.Form470), client, stdout, stderr, default);
            Assert.Equal(CliCommand.ExitFound, code);
            Assert.Contains("260007116", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Found 1", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("jt8s-3q52", handler.Urls[0], StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task EmptyPublishedResultIsNotFound()
    {
        var directory = Temp();
        try
        {
            using var http = new HttpClient(new ScriptedHandler("[]"));
            using var client = new UsacClient(http, directory);
            var code = await CliCommand.RunAsync(Options(FilingForm.Form470, year: 2027), client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitNotFound, code);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task HttpFailureIsUnreachable()
    {
        var directory = Temp();
        try
        {
            using var http = new HttpClient(new ScriptedHandler("[]", HttpStatusCode.BadRequest));
            using var client = new UsacClient(http, directory);
            var stderr = new StringWriter();
            var code = await CliCommand.RunAsync(Options(FilingForm.Form470), client, new StringWriter(), stderr, default);
            Assert.Equal(CliCommand.ExitUsacUnreachable, code);
            Assert.Contains("USAC could not be reached", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task ApplicationFilterMissesExitNotFound()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"260007116","billed_entity_number":"143618","fcc_form_470_status":"Certified","form_version":"Current"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var options = Options(FilingForm.Form470) with { ApplicationNumber = "999" };
            var code = await CliCommand.RunAsync(options, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitNotFound, code);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task CsvEscapesFormulaTextAndWritesAFile()
    {
        var directory = Temp();
        var output = Path.Combine(directory, "out.csv");
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"260007116","billed_entity_number":"143618","form_nickname":"=SUM(A1)","fcc_form_470_status":"Certified","form_version":"Current"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var options = Options(FilingForm.Form470, CliFormat.Csv) with { OutputPath = output };
            var code = await CliCommand.RunAsync(options, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            var csv = await File.ReadAllTextAsync(output);
            Assert.Contains("ApplicationNumber", csv, StringComparison.Ordinal);
            Assert.Contains("'=SUM(A1)", csv, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task JsonIncludesTheApplication()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"261003372","epc_organization_id":"143618","billed_entity_name":"Chula Vista Elem School Dist","form_version":"Current","filing_status":"Committed","certified_date_time":"2026-03-01T00:00:00.000"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var options = Options(FilingForm.Form471, CliFormat.Json);
            var code = await CliCommand.RunAsync(options, client, stdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.Equal(471, document.RootElement.GetProperty("form").GetInt32());
            Assert.Equal("261003372", document.RootElement.GetProperty("rows")[0].GetProperty("applicationNumber").GetString());
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task FrnQueryUsesTheFrnDataset()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""[{"funding_request_number":"2699003124","application_number":"261003372","billed_entity_number":"143618","billed_entity_name":"Chula Vista Elem School Dist","filing_status":"Funded","form_nickname":"Att Ase","service_type":"Data Transmission and/or Internet Access","funding_request_amount":"166320"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var options = Options(FilingForm.Form471) with { FundingRequestNumber = "2699003124" };
            var code = await CliCommand.RunAsync(options, client, stdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            Assert.Contains("2699003124", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("qdmp-ygft", handler.Urls[0], StringComparison.Ordinal);
            Assert.Contains("funding_request_number='2699003124'", Uri.UnescapeDataString(handler.Urls[0]), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    static CliOptions Options(FilingForm form, CliFormat format = CliFormat.Table, int year = 2026) =>
        new(form, "143618", year, null, null, format, null);

    static string Temp() => Path.Combine(Path.GetTempPath(), "ERateCli-" + Guid.NewGuid().ToString("N"));

    static void Cleanup(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    sealed class ScriptedHandler : HttpMessageHandler
    {
        readonly string body;
        readonly HttpStatusCode status;
        public List<string> Urls { get; } = new();

        public ScriptedHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            this.body = body;
            this.status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
