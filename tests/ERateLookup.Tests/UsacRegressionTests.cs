using System.Net;
using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Tests;

public class UsacRegressionTests
{
    [Fact]
    public async Task OfflineChecks()
    {
        void Check(bool condition, string name) => Assert.True(condition, name);
        Check(UsacClient.Literal("O'Brien") == "'O''Brien'", "SoQL escaping");
        Check(UsacClient.FilingFilter("133193", 2027).Contains("funding_year='2027'"), "Selected funding year");
        var grouped = UsacClient.Collapse(new[] { new UsacRow { Number = "1", Version = "Original", Function = "Old" }, new UsacRow { Number = "1", Version = "Current", Function = "Switches" }, new UsacRow { Number = "1", Version = "Current", Function = "Switches" }, new UsacRow { Number = "2", Function = "Wireless" } });
        Check(grouped.Count == 2 && grouped[0].Services == "Switches", "Deduplication and current version");
        var schedule = FilingWindow.Parse("<h1>Funding Year 2027 Filing Window</h1><p>January 21, 2027 – April 1, 2027</p>", 2027, DateTimeOffset.UtcNow, "https://www.usac.org/example");
        Check(schedule.Opens is not null && schedule.Message(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero)).Contains("has not opened"), "Future 471 window shows come-back notice");
        Check(schedule.Message(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)).Contains("is open"), "Window-open notification uses verified schedule");
        Check(schedule.Message(new DateTimeOffset(2027, 5, 1, 0, 0, 0, TimeSpan.Zero)).Contains("has closed"), "Historical filings remain searchable after window");
        Check(FilingWindow.Parse("Funding Year 2026 Filing Window January 21, 2026 – April 1, 2026", 2027, DateTimeOffset.UtcNow, "").Opens is null, "Other year's window is never used for 2027");
        Check(FilingWindow.Parse("Funding Year 2027 Filing Window Dates coming soon", 2027, DateTimeOffset.UtcNow, "").Message(DateTimeOffset.UtcNow).Contains("exact opening date is not available"), "Unannounced dates stay unknown");
        var directory = Path.Combine(Path.GetTempPath(), "ERateChecks-" + Guid.NewGuid());
        try
        {
            var handler = new FakeHandler(); using var http = new HttpClient(handler); var now = DateTimeOffset.UtcNow; using var client = new UsacClient(http, directory, () => now); var parameters = new Dictionary<string, string> { { "$limit", "1" } };
            var a = await client.QueryAsync(parameters, false, default); var b = await client.QueryAsync(parameters, false, default);
            Check(!a.FromCache && b.FromCache && handler.Count == 1, "Cache including empty responses");
            await client.QueryAsync(parameters, true, default); Check(handler.Count == 2, "Refresh bypass");
            now = now.AddMinutes(119); var fresh = await client.QueryAsync(parameters, false, default); Check(fresh.FromCache && handler.Count == 2, "Younger than two hours uses cache");
            now = now.AddMinutes(1); var expired = await client.QueryAsync(parameters, false, default); Check(!expired.FromCache && handler.Count == 3, "Exactly two hours triggers live query");
            handler.Fail = true; var failure = false;
            try { await client.QueryAsync(new() { { "$limit", "2" } }, false, default); } catch (HttpRequestException) { failure = true; }
            Check(failure, "Network failure is not no-filings"); handler.Fail = false;
            var c = await client.QueryAsync(new() { { "$limit", "2" } }, false, default); Check(!c.FromCache, "Failed request not cached");
            var formsHandler = new FormHandler(); using var formsHttp = new HttpClient(formsHandler);
            using var formsClient = new UsacClient(formsHttp, Path.Combine(directory, "forms"), () => now);
            var f470 = await formsClient.GetFilingsAsync("101552", 2027, FilingForm.Form470, false, default);
            var f471 = await formsClient.GetFilingsAsync("101552", 2027, FilingForm.Form471, false, default);
            Check(f470.Data.Count == 1 && f471.Data.Count == 0 && formsHandler.Count == 2, "Form 471 empty result does not reuse Form 470 cache");
            var emptyCached = await formsClient.GetFilingsAsync("101552", 2027, FilingForm.Form471, false, default);
            Check(emptyCached.FromCache && emptyCached.Data.Count == 0 && formsHandler.Count == 2, "Empty Form 471 result cached normally");
            await formsClient.GetFilingsAsync("101552", 2027, FilingForm.Form471, true, default);
            Check(formsHandler.Count == 3, "Form 471 manual refresh bypasses empty cache");
            now = now.AddHours(2); await formsClient.GetFilingsAsync("101552", 2027, FilingForm.Form471, false, default);
            Check(formsHandler.Count == 4, "Empty Form 471 expires at two hours");
            Check(UsacClient.FilingFilter("101552", 2027, FilingForm.Form471).Contains("epc_organization_id='101552'") && UsacClient.FilingFilter("101552", 2027, FilingForm.Form471).Contains("certified_datetime IS NOT NULL"), "Form 471 uses entity ID and certification date");
            var mapped = System.Text.Json.JsonSerializer.Deserialize<List<UsacRow>>("""[{"application_number":"1","billed_entity_number":"101552","form_version":"Current","filing_status":"Committed","service_category":"Category2"},{"application_number":"1","form_version":"Original","pdf_url":"https://publicdata.usac.org/example.pdf"}]""")!;
            var collapsed471 = UsacClient.Collapse(mapped);
            Check(collapsed471.Count == 1 && collapsed471[0].Status == "Committed" && collapsed471[0].Services == "Category2" && collapsed471[0].Pdf.EndsWith("example.pdf"), "Form 471 preserves current status and available original PDF");
            formsHandler.Fail = true; var failed471 = false;
            try { await formsClient.GetFilingsAsync("101552", 2027, FilingForm.Form471, true, default); } catch (HttpRequestException) { failed471 = true; }
            Check(failed471, "Form 471 failure is not an empty successful lookup");
            var directoryClient = new CaliforniaDirectory(http, Path.Combine(directory, "directory"));
            directoryClient.Replace(new() { new() { Number = "99", Name = "No Budget District", City = "Test", State = "CA", Type = "School District" } }, new() { new() { Ben = "88", Name = "Forecast District", State = "CA", City = "Test", Type = "School District", Cycle = "FY2026-2030", Version = "Forecast", Total = "12345.67", Committed = "0", Pending = "0", Available = "12345.67" } }, now);
            Check(directoryClient.Search("budget").Any(x => x.Ben == "99"), "Entities without budget remain searchable");
            Check(directoryClient.GetBudget("99", 2027) is null, "Missing budget remains unknown");
            var forecast = directoryClient.GetBudget("88", 2027);
            Check(forecast?.Version == "Forecast" && BudgetRow.Money(forecast.Total) == "$12,345.67", "Forecast status and budget display");
            Check(BudgetRow.Money(null) == "Not reported" && BudgetRow.Money("0") == "$0.00", "Missing amounts distinguished from zero");
            Check(directoryClient.GetBudget("88", 2025) is null, "Budget cycle follows selected year");
            Check(directoryClient.Search("%").Count == 0, "Partial name search treats wildcards literally");
            Check(BudgetRow.ForecastView("Forecast", true), "USAC Forecast stays forecast even with Form 470");
            Check(!BudgetRow.ForecastView("Confirmed", false), "USAC Confirmed status survives missing new-cycle Form 470");
            Check(BudgetRow.ForecastView("Preliminary", false), "Missing 470 with unconfirmed status uses forecast view");
            Check(BudgetRow.Money("-12.34").Contains("12.34"), "Negative reported balances remain visible");
            Check(!BudgetRow.ForecastView("Confirmed", null), "Failed filing check does not imply no forms");
            var seeded = new CaliforniaDirectory(http, Path.Combine(directory, "seeded")); seeded.LoadSeedIfEmpty(); Check(seeded.Search("SAN DIEGO").Count > 0, "Bundled California directory loads"); Check(seeded.Search("SAN DIEGO CITY UNIF").Any(entity => entity.Ben == "143662"), "San Diego Unified is published as San Diego City Unif Sch Dist, BEN 143662");
            Check(seeded.Search("SCHOOL").Count == 15, "GUI suggestions capped at 15");
            var exportRows = new List<string[]> { new[] { "BEN", "Name", "ExportedUTC" }, new[] { "00123", "=SUM(A1)", DateTimeOffset.UtcNow.ToString("O") } };
            var csv = Path.Combine(directory, "export.csv"); var excel = Path.Combine(directory, "export.xlsx"); DataExporter.WriteCsv(csv, exportRows); DataExporter.WriteExcel(excel, exportRows);
            Check(File.ReadAllText(csv).Contains("'="), "CSV formula-like text escaped");
            using (var archive = System.IO.Compression.ZipFile.OpenRead(excel))
            {
                Check(archive.GetEntry("[Content_Types].xml") is not null && archive.GetEntry("xl/workbook.xml") is not null, "Excel workbook package structure");
                using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open(); var xml = System.Xml.Linq.XDocument.Load(sheet);
                Check(xml.Descendants().Any(e => e.Name.LocalName == "t" && e.Value == "00123") && !xml.Descendants().Any(e => e.Name.LocalName == "f"), "Excel preserves IDs and does not create formulas");
            }

        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

sealed class FakeHandler : HttpMessageHandler
{
    public int Count; public bool Fail;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { Count++; return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent("[]") }); }
}

sealed class FormHandler : HttpMessageHandler
{
    public int Count; public bool Fail;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Count++;
        var content = request.RequestUri!.AbsolutePath.Contains("9s6i-myen") ? "[]" : "[{\"application_number\":\"470-test\",\"billed_entity_number\":\"101552\"}]";
        return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent(content) });
    }
}
