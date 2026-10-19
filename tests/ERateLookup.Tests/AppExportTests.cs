using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ERateLookup.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ERateLookup.Tests;

public class AppExportTests
{
    static readonly DateTimeOffset Clock = new(2026, 10, 19, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void LookupExportMatchesTheBaseSchemaAcrossCsvExcelAndJson()
    {
        var folder = Temp();
        try
        {
            using var screen = OpenScreen(folder);
            var filing = new Filing(
                "260007116",
                "",
                "CHULA VISTA ELEM SCHOOL DIST",
                "470 C2",
                "2025-12-19",
                "Internal Connections",
                "https://publicdata.usac.org/example.pdf",
                "Certified",
                "",
                "100.00",
                "200.00");
            var prepared = screen.View.CreateExport(
                2026,
                FilingForm.Form470,
                "143618",
                "fallback",
                Clock,
                new[] { filing },
                new HashSet<string> { filing.Number },
                null);
            var cells = ExportSchema.ToRow(ExportSchema.WithDisplayedBen(
                ExportSchema.FromFiling(FilingForm.Form470, filing, 2026, false, null, null),
                "143618"));
            Assert.Equal(ExportSchema.BaseColumns, prepared.Table[0]);
            Assert.Equal(cells, prepared.Table[1]);
            Assert.Equal("143618", prepared.Table[1][2]);
            Assert.Equal("", prepared.JsonRows[0].Ben);
            Assert.Equal(470, prepared.JsonRows[0].Form);
            Assert.False(prepared.Metadata.Linked);
            Assert.Null(prepared.Metadata.Form470Count);
            Assert.Null(prepared.Metadata.Form471Count);
            Assert.Equal("fallback", prepared.Metadata.ApplicantName);

            var csvPath = Path.Combine(folder, "lookup.csv");
            var excelPath = Path.Combine(folder, "lookup.xlsx");
            var jsonPath = Path.Combine(folder, "lookup.json");
            prepared.Write(csvPath);
            prepared.Write(excelPath);
            prepared.Write(jsonPath);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(csvPath).Take(3));
            Assert.Equal(prepared.Table, ReadSheet(excelPath));
            Assert.Contains("frozen", SheetXml(excelPath), StringComparison.Ordinal);
            Assert.Contains("autoFilter", SheetXml(excelPath), StringComparison.Ordinal);
            var jsonBytes = File.ReadAllBytes(jsonPath);
            Assert.False(jsonBytes.Length >= 3 && jsonBytes[0] == 0xEF && jsonBytes[1] == 0xBB && jsonBytes[2] == 0xBF);
            using var document = JsonDocument.Parse(jsonBytes);
            var root = document.RootElement;
            Assert.Equal(new[] { "form", "linked", "ben", "applicantName", "fundingYear", "applicationNumber", "fundingRequestNumber", "fetchedAt", "count", "rows" }, root.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.False(root.TryGetProperty("form470Count", out _));
            Assert.Equal(1, root.GetProperty("count").GetInt32());
            Assert.Equal(root.GetProperty("rows").GetArrayLength(), root.GetProperty("count").GetInt32());
            Assert.Equal(Clock.ToString("O", CultureInfo.InvariantCulture), root.GetProperty("fetchedAt").GetString());
            var row = root.GetProperty("rows")[0];
            Assert.Equal(new[] { "form", "applicationNumber", "fundingRequestNumber", "status", "certifiedDate", "nickname", "ben", "name", "services", "pdf", "requestedAmount", "preDiscountAmount", "source" }, row.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(470, row.GetProperty("form").GetInt32());
            Assert.Equal("", row.GetProperty("ben").GetString());
            Assert.Equal(UsacSources.Application(FilingForm.Form470, filing.Number), row.GetProperty("source").GetString());
            Assert.False(row.TryGetProperty("lookupOutcome", out _));
            Assert.False(row.TryGetProperty("Name", out _));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void EmptyLookupExportsOneBaseRowWithABlankSource()
    {
        var folder = Temp();
        try
        {
            using var screen = OpenScreen(folder);
            var prepared = screen.View.CreateExport(2026, FilingForm.Form471, "143618", "District", Clock, Array.Empty<Filing>(), new HashSet<string>(), false);
            Assert.Equal(ExportSchema.BaseColumns, prepared.Table[0]);
            Assert.Equal("471", prepared.Table[1][0]);
            Assert.Equal("143618", prepared.Table[1][2]);
            Assert.Equal("District", prepared.Table[1][3]);
            Assert.Equal("", prepared.Table[1][Array.IndexOf(prepared.Table[0], "ApplicationNumber")]);
            Assert.Equal("", prepared.Table[1][Array.IndexOf(prepared.Table[0], "Source")]);
            Assert.Equal("false", prepared.Table[1][Array.IndexOf(prepared.Table[0], "Linked")]);
            Assert.Equal("", prepared.JsonRows[0].Source);
            Assert.Equal(471, prepared.JsonRows[0].Form);
            Assert.DoesNotContain("BudgetCycle", prepared.Table[0]);
            Assert.DoesNotContain("LookupResult", prepared.Table[0]);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void PortfolioExportIsTheBaseSchemaPlusFourTrailingColumns()
    {
        var folder = Temp();
        try
        {
            var store = new PortfolioStore(folder);
            var observed = Clock;
            var filing = new Filing(
                "260007116",
                "143618",
                "CHULA VISTA ELEM SCHOOL DIST",
                "470 C2",
                "2025-12-19",
                "Internal Connections",
                "https://publicdata.usac.org/example.pdf",
                "Certified",
                "2026-10-29T00:00:00.000",
                "100.00",
                "200.00");
            var key = new LookupKey("143618", 2026, FilingForm.Form470);
            Assert.True(store.SaveLive(key, store.BeginObservation(), new QueryResult<List<Filing>>(new List<Filing> { filing }, observed, false)));
            using var http = new HttpClient(new StubHandler());
            using var client = new UsacClient(http, Path.Combine(folder, "cache"), () => Clock);
            var session = new PortfolioSession(client, store);
            session.LoadSaved(new[] { key });
            session.Selected.Add(Assert.Single(session.Rows).Id);
            var prepared = session.CreateExport();
            var headers = ExportSchema.Columns(new ExportColumns { IncludePortfolio = true });
            Assert.Equal(headers, prepared.Table[0]);
            Assert.Equal(ExportSchema.BaseColumns, prepared.Table[0].Take(ExportSchema.BaseColumns.Count));
            Assert.Equal(new[] { "LookupOutcome", "Change", "SourceAndNotes", "AllowableContractDate" }, prepared.Table[0].Skip(ExportSchema.BaseColumns.Count));
            Assert.DoesNotContain("RequestedFundingUSD", prepared.Table[0]);
            Assert.DoesNotContain("BudgetCycle", prepared.Table[0]);
            Assert.Equal("100.00", prepared.Table[1][Array.IndexOf(prepared.Table[0], "RequestedAmount")]);
            Assert.Equal("200.00", prepared.Table[1][Array.IndexOf(prepared.Table[0], "PreDiscountAmount")]);
            Assert.Equal("Success", prepared.Table[1][Array.IndexOf(prepared.Table[0], "LookupOutcome")]);
            Assert.Equal("2026-10-29", prepared.Table[1][Array.IndexOf(prepared.Table[0], "AllowableContractDate")]);
            Assert.Equal(UsacSources.Application(FilingForm.Form470, filing.Number), prepared.Table[1][Array.IndexOf(prepared.Table[0], "Source")]);
            Assert.Equal("false", prepared.Table[1][Array.IndexOf(prepared.Table[0], "Linked")]);

            var jsonPath = Path.Combine(folder, "portfolio.json");
            prepared.Write(jsonPath);
            using var document = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
            var root = document.RootElement;
            Assert.Equal(470, root.GetProperty("form").GetInt32());
            Assert.False(root.GetProperty("linked").GetBoolean());
            Assert.Equal("143618", root.GetProperty("ben").GetString());
            Assert.Equal(2026, root.GetProperty("fundingYear").GetInt32());
            Assert.Equal(observed.ToString("O", CultureInfo.InvariantCulture), root.GetProperty("fetchedAt").GetString());
            Assert.Equal(1, root.GetProperty("count").GetInt32());
            var row = root.GetProperty("rows")[0];
            Assert.Equal("Success", row.GetProperty("lookupOutcome").GetString());
            Assert.Equal("", row.GetProperty("change").GetString());
            Assert.Contains("Saved / offline", row.GetProperty("sourceAndNotes").GetString(), StringComparison.Ordinal);
            Assert.Equal("2026-10-29", row.GetProperty("allowableContractDate").GetString());
            Assert.Equal("100.00", row.GetProperty("requestedAmount").GetString());
            Assert.Equal(470, row.GetProperty("form").GetInt32());
            Assert.False(row.TryGetProperty("LookupOutcome", out _));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact]
    public void PreparedWriterUsesTheExtension()
    {
        var folder = Temp();
        Directory.CreateDirectory(folder);
        try
        {
            var table = new List<string[]> { new[] { "Form" }, new[] { "=470" } };
            var metadata = new ExportMetadata { Form = 470, Linked = false, FundingYear = 2026, FetchedAt = Clock };
            var rows = new[] { new ExportJsonRow(470, "1", "", "Certified", "", "", "143618", "Name", "", "", "", "", "https://opendata.usac.org/resource/jt8s-3q52.json") };
            var jsonPath = Path.Combine(folder, "rows.json");
            var csvPath = Path.Combine(folder, "rows.csv");
            DataExporter.WritePrepared(jsonPath, table, metadata, rows);
            DataExporter.WritePrepared(csvPath, table, metadata, rows);
            using var document = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
            Assert.Equal(1, document.RootElement.GetProperty("count").GetInt32());
            Assert.Contains("'=470", File.ReadAllText(csvPath), StringComparison.Ordinal);
            Assert.Equal(Path.Combine(folder, "chosen.json"), DataExporter.ApplySaveFilterExtension(Path.Combine(folder, "chosen.xlsx"), 3, ".xlsx", ".csv", ".json"));
            Assert.Throws<ArgumentException>(() => DataExporter.WritePrepared(Path.Combine(folder, "rows.txt"), table, metadata, rows));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    static Screen OpenScreen(string folder)
    {
        var http = new HttpClient(new StubHandler());
        var client = new UsacClient(http, Path.Combine(folder, "cache"), () => Clock);
        var directory = new CaliforniaDirectory(http, Path.Combine(folder, "directory"));
        var windows = new FilingWindow(http, Path.Combine(folder, "windows"));
        var view = new LookupViewModel(client, directory, windows, LookupSurface.Windows, () => Clock);
        return new Screen(http, client, view);
    }

    static string Temp() => Path.Combine(Path.GetTempPath(), "ERateAppExport-" + Guid.NewGuid().ToString("N"));

    static void Cleanup(string folder)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    static string SheetXml(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        using var reader = new StreamReader(sheet, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static List<string[]> ReadSheet(string path)
    {
        var xml = XDocument.Parse(SheetXml(path));
        return xml.Descendants().Where(element => element.Name.LocalName == "row").Select(row =>
            row.Elements().Where(element => element.Name.LocalName == "c").Select(cell =>
                cell.Descendants().First(element => element.Name.LocalName == "t").Value).ToArray()).ToList();
    }

    sealed class Screen(HttpClient http, UsacClient client, LookupViewModel view) : IDisposable
    {
        public LookupViewModel View { get; } = view;

        public void Dispose()
        {
            client.Dispose();
            http.Dispose();
        }
    }

    sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
