using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Cli.Tests;

public class ExportSchemaGoldenTests
{
    static readonly DateTimeOffset Clock = new(2026, 10, 19, 1, 2, 3, TimeSpan.Zero);

    const string Form470Body = """
        [{"application_number":"260007116","billed_entity_number":"143618","billed_entity_name":"CHULA VISTA ELEM SCHOOL DIST","fcc_form_470_status":"Certified","form_nickname":"470 C2","certified_date_time":"2025-12-19T11:35:41.000","service_category":"Category 2","service_type":"Internal Connections","function":"Switches","form_version":"Current","funding_request_amount":"100.00","pre_discount_eligible_amount":"200.00","pdf_url":"https://publicdata.usac.org/example.pdf"}]
        """;

    [Fact]
    public async Task SameFilingAgreesAcrossTheCliAndTheLookupExport()
    {
        var directory = Temp();
        var csvPath = Path.Combine(directory, "out.csv");
        var jsonPath = Path.Combine(directory, "out.json");
        try
        {
            var handler = new ScriptedHandler(Form470Body);
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, Path.Combine(directory, "cache"), () => Clock);
            var stdout = new StringWriter();
            var jsonOptions = Options(CliFormat.Json);
            var jsonCode = await CliCommand.RunAsync(jsonOptions, client, stdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, jsonCode);

            var filings = await client.GetFilingsAsync("143618", 2026, FilingForm.Form470, false, default);
            var filing = Assert.Single(filings.Data);
            var schemaRow = ExportSchema.FromFiling(FilingForm.Form470, filing, 2026, false, null, null);
            var displayed = ExportSchema.WithDisplayedBen(schemaRow, "143618");
            var cells = ExportSchema.ToRow(displayed);
            var cliCells = ExportSchema.ToRow(displayed, ExportSchema.CliColumns);
            Assert.Equal(ExportSchema.BaseColumns, ExportSchema.Columns());
            Assert.Equal(cells.Length, ExportSchema.BaseColumns.Count);
            Assert.Equal(new[] { "", "", "", "", "" }, cliCells.Skip(cells.Length));

            var csvCode = await CliCommand.RunAsync(Options(CliFormat.Csv) with { OutputPath = csvPath }, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, csvCode);
            var csv = ParseCsv(await File.ReadAllTextAsync(csvPath));
            Assert.Equal(ExportSchema.Columns(ExportSchema.CliColumns), csv[0]);
            Assert.Equal(cliCells, csv[1]);

            var excelPath = Path.Combine(directory, "rows.xlsx");
            var excelStdout = new StringWriter();
            var excelCode = await CliCommand.RunAsync(Options(CliFormat.Excel) with { OutputPath = excelPath }, client, excelStdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, excelCode);
            Assert.Equal("", excelStdout.ToString());
            var sheet = ReadSheet(excelPath);
            Assert.Equal(ExportSchema.BaseColumns, sheet[0]);
            Assert.Equal(cells, sheet[1]);
            Assert.DoesNotContain("FrnStatus", sheet[0]);

            var metadata = new ExportMetadata
            {
                Form = 470,
                Linked = false,
                Ben = "143618",
                FundingYear = 2026,
                FetchedAt = Clock,
            };
            var columnPath = Path.Combine(directory, "columns.json");
            DataExporter.WriteColumnJson(columnPath, metadata, csv[0], new[] { csv[1] });
            using (var columnDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(columnPath)))
            {
                var columnRow = columnDocument.RootElement.GetProperty("rows")[0];
                Assert.Equal(csv[0], columnRow.EnumerateObject().Select(property => property.Name).ToArray());
                Assert.Equal(cliCells, columnRow.EnumerateObject().Select(property => property.Value.GetString()).ToArray());
            }

            var jsonText = Body(stdout.ToString());
            using var document = JsonDocument.Parse(jsonText);
            var root = document.RootElement;
            Assert.Equal(root.GetProperty("rows").GetArrayLength(), root.GetProperty("count").GetInt32());
            var fetchedAt = root.GetProperty("fetchedAt").GetString();
            Assert.NotNull(fetchedAt);
            Assert.Equal(Clock, DateTimeOffset.Parse(fetchedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            Assert.Equal(Clock.ToString("O", CultureInfo.InvariantCulture), fetchedAt);
            Assert.False(root.TryGetProperty("form470Count", out _));
            var jsonRow = root.GetProperty("rows")[0];
            Assert.Equal(schemaRow.ApplicationNumber, jsonRow.GetProperty("applicationNumber").GetString());
            Assert.Equal(schemaRow.FundingRequestNumber, jsonRow.GetProperty("fundingRequestNumber").GetString());
            Assert.Equal(schemaRow.Status, jsonRow.GetProperty("status").GetString());
            Assert.Equal(schemaRow.CertifiedDate, jsonRow.GetProperty("certifiedDate").GetString());
            Assert.Equal(schemaRow.Nickname, jsonRow.GetProperty("nickname").GetString());
            Assert.Equal(schemaRow.Ben, jsonRow.GetProperty("ben").GetString());
            Assert.Equal(schemaRow.Name, jsonRow.GetProperty("name").GetString());
            Assert.Equal(schemaRow.Services, jsonRow.GetProperty("services").GetString());
            Assert.Equal(schemaRow.Pdf, jsonRow.GetProperty("pdf").GetString());
            Assert.Equal(schemaRow.RequestedAmount, jsonRow.GetProperty("requestedAmount").GetString());
            Assert.Equal(schemaRow.PreDiscountAmount, jsonRow.GetProperty("preDiscountAmount").GetString());
            Assert.Equal(schemaRow.Source, jsonRow.GetProperty("source").GetString());
            Assert.Equal(470, jsonRow.GetProperty("form").GetInt32());
            Assert.False(jsonRow.TryGetProperty("ApplicationNumber", out _));
            Assert.False(jsonRow.TryGetProperty("Form", out _));
            Assert.Equal(ExpectedJson(schemaRow), jsonText);

            var fileCode = await CliCommand.RunAsync(jsonOptions with { OutputPath = jsonPath }, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, fileCode);
            var fileBytes = await File.ReadAllBytesAsync(jsonPath);
            Assert.False(fileBytes.Length >= 3 && fileBytes[0] == 0xEF && fileBytes[1] == 0xBB && fileBytes[2] == 0xBF);
            Assert.Equal(jsonText, EncodingWithoutBom(fileBytes));

            var directoryClient = new CaliforniaDirectory(http, Path.Combine(directory, "directory"));
            var windows = new FilingWindow(http, Path.Combine(directory, "windows"));
            var screen = new LookupViewModel(client, directoryClient, windows, LookupSurface.Windows, () => Clock);
            var lookup = screen.ExportRows(2026, FilingForm.Form470, "143618", "fallback", filings.FetchedAt, filings.Data, new HashSet<string> { filing.Number }, null);
            Assert.Equal(
                new[]
                {
                    "ExportedUTC", "LookupFetchedUTC", "DirectoryUpdatedUTC", "FundingYear", "BEN", "Customer", "LookupResult", "FormType", "ApplicationNumber", "FilingStatus", "CertifiedDate", "Nickname", "Services", "PDF", "BudgetCycle", "USACBudgetStatus", "BudgetDisplayStatus", "TotalC2BudgetPreDiscount", "CommittedC2BudgetPreDiscount", "PendingC2BudgetPreDiscount", "ReportedRemainingC2BudgetPreDiscount",
                },
                lookup[0]);
            Assert.Contains("Customer", lookup[0]);
            Assert.Contains("FormType", lookup[0]);
            Assert.Contains("FilingStatus", lookup[0]);
            Assert.DoesNotContain("Name", lookup[0]);
            Assert.DoesNotContain("Form", lookup[0]);
            Assert.DoesNotContain("Status", lookup[0]);
            var legacy = Map(lookup[0], lookup[1]);
            var shared = Map(ExportSchema.BaseColumns, cells);
            Assert.Equal(shared["FundingYear"], legacy["FundingYear"]);
            Assert.Equal(shared["BEN"], legacy["BEN"]);
            Assert.Equal(shared["Name"], legacy["Customer"]);
            Assert.Equal(shared["Form"], legacy["FormType"]);
            Assert.Equal(shared["ApplicationNumber"], legacy["ApplicationNumber"]);
            Assert.Equal(shared["Status"], legacy["FilingStatus"]);
            Assert.Equal(shared["CertifiedDate"], legacy["CertifiedDate"]);
            Assert.Equal(shared["Nickname"], legacy["Nickname"]);
            Assert.Equal(shared["Services"], legacy["Services"]);
            Assert.Equal(shared["PDF"], legacy["PDF"]);
            Assert.Equal("Filed", legacy["LookupResult"]);
            Assert.Equal("Not reported", legacy["TotalC2BudgetPreDiscount"]);
            Assert.DoesNotContain("BudgetCycle", shared.Keys);
            Assert.DoesNotContain("Source", legacy.Keys);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task FundingRequestWithoutAPublishedBenKeepsThatDifference()
    {
        var directory = Temp();
        var csvPath = Path.Combine(directory, "frn.csv");
        try
        {
            var handler = new ScriptedHandler("""[{"funding_request_number":"2699003124","application_number":"261003372","billed_entity_name":"Chula Vista Elem School Dist","filing_status":"Funded","form_version":"Current"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory, () => Clock);
            var stdout = new StringWriter();
            var options = Options(CliFormat.Json, FilingForm.Form471) with { FundingRequestNumber = "2699003124" };
            var code = await CliCommand.RunAsync(options, client, stdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            using var document = JsonDocument.Parse(Body(stdout.ToString()));
            Assert.Equal("", document.RootElement.GetProperty("rows")[0].GetProperty("ben").GetString());

            var csvCode = await CliCommand.RunAsync(options with { Format = CliFormat.Csv, OutputPath = csvPath }, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, csvCode);
            var csv = ParseCsv(await File.ReadAllTextAsync(csvPath));
            Assert.Equal(ExportSchema.Columns(ExportSchema.CliColumns), csv[0]);
            Assert.Equal("143618", csv[1][2]);
            Assert.Equal(UsacSources.FundingRequest("2699003124", "261003372"), csv[1][13]);
            Assert.Equal(new[] { "Funded", "", "", "", "" }, csv[1][17..]);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    static string ExpectedJson(ExportRow row)
    {
        var payload = new
        {
            form = 470,
            linked = false,
            ben = "143618",
            applicantName = (string?)null,
            fundingYear = 2026,
            applicationNumber = (string?)null,
            fundingRequestNumber = (string?)null,
            fetchedAt = Clock.ToString("O", CultureInfo.InvariantCulture),
            count = 1,
            rows = new[]
            {
                new CliRow(
                    470,
                    row.ApplicationNumber,
                    row.FundingRequestNumber,
                    row.Status,
                    row.CertifiedDate,
                    row.Nickname,
                    row.Ben,
                    row.Name,
                    row.Services,
                    row.Pdf,
                    row.RequestedAmount,
                    row.PreDiscountAmount,
                    row.Source)
            }
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    static string Body(string stdout)
    {
        Assert.EndsWith(Environment.NewLine, stdout, StringComparison.Ordinal);
        return stdout[..^Environment.NewLine.Length];
    }

    static string EncodingWithoutBom(byte[] bytes) => new System.Text.UTF8Encoding(false).GetString(bytes);

    static Dictionary<string, string> Map(IReadOnlyList<string> headers, IReadOnlyList<string> values) =>
        headers.Select((header, index) => (header, values[index])).ToDictionary(item => item.header, item => item.Item2, StringComparer.Ordinal);

    static CliOptions Options(CliFormat format, FilingForm form = FilingForm.Form470) =>
        new(form, false, "143618", null, 2026, null, null, format, null);

    static string Temp() => Path.Combine(Path.GetTempPath(), "ERateSchemaCli-" + Guid.NewGuid().ToString("N"));

    static void Cleanup(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    static List<string[]> ParseCsv(string csv)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var character = csv[i];
            if (quoted)
            {
                if (character == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                if (row.Count > 1 || row[0].Length > 0)
                {
                    rows.Add(row.ToArray());
                }

                row = new List<string>();
            }
            else if (character != '\r' && character != '\uFEFF')
            {
                field.Append(character);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }

    static List<string[]> ReadSheet(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = System.Xml.Linq.XDocument.Load(sheet);
        return xml.Descendants().Where(element => element.Name.LocalName == "row").Select(row =>
            row.Elements().Where(element => element.Name.LocalName == "c").Select(cell =>
                cell.Descendants().First(element => element.Name.LocalName == "t").Value).ToArray()).ToList();
    }

    sealed class ScriptedHandler : HttpMessageHandler
    {
        readonly string body;

        public ScriptedHandler(string body) => this.body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
