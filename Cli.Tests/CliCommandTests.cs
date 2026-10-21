using System.Net;
using System.Text;
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
        Assert.Equal("Choose either --csv, --json, or --xlsx.", parsed.Error);
    }

    [Fact]
    public void HelpListsCsvJsonAndExcel()
    {
        Assert.Contains("--csv", CliCommand.HelpText, StringComparison.Ordinal);
        Assert.Contains("--json", CliCommand.HelpText, StringComparison.Ordinal);
        Assert.Contains("--xlsx", CliCommand.HelpText, StringComparison.Ordinal);
        Assert.Contains("--excel", CliCommand.HelpText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--csv", "--json")]
    [InlineData("--csv", "--xlsx")]
    [InlineData("--json", "--xlsx")]
    [InlineData("--csv", "--excel")]
    [InlineData("--json", "--excel")]
    public void CombiningFormatsIsRejected(string first, string second)
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", first, second, "--output", "out.bin" });
        Assert.Null(parsed.Options);
        Assert.Equal("Choose either --csv, --json, or --xlsx.", parsed.Error);
    }

    [Fact]
    public void ExcelRequiresAnOutputPath()
    {
        var missing = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", "--xlsx" });
        Assert.Null(missing.Options);
        Assert.Equal("--xlsx requires --output <file.xlsx>.", missing.Error);

        var alias = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", "--excel" });
        Assert.Equal("--xlsx requires --output <file.xlsx>.", alias.Error);

        var valued = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", "--xlsx=out.xlsx" });
        Assert.Contains("does not take a value", valued.Error, StringComparison.Ordinal);

        var duplicate = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", "--xlsx", "--excel", "--output", "out.xlsx" });
        Assert.Contains("Duplicate argument: --excel", duplicate.Error, StringComparison.Ordinal);

        var parsed = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", "--excel", "--output", "nested/out.xlsx" });
        Assert.Null(parsed.Error);
        Assert.NotNull(parsed.Options);
        Assert.Equal(CliFormat.Excel, parsed.Options.Format);
        Assert.Equal("nested/out.xlsx", parsed.Options.OutputPath);
    }

    [Theory]
    [InlineData("--xlsx")]
    [InlineData("--excel")]
    public void DiffRejectsExcel(string flag)
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", "--diff", flag, "--output", "out.xlsx" });
        Assert.Null(parsed.Options);
        Assert.Equal("--diff supports the table, CSV, and JSON only.", parsed.Error);

        var csv = CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2026", "--diff", "--csv", "--output", "out.csv" });
        Assert.Null(csv.Error);
        Assert.NotNull(csv.Options);
        Assert.True(csv.Options.Diff);
        Assert.Equal(CliFormat.Csv, csv.Options.Format);
    }

    [Fact]
    public async Task DiffExcelRunDoesNotQuery()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("[]");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form470, CliFormat.Excel) with { Diff = true, OutputPath = "out.xlsx" };
            var code = await CliCommand.RunAsync(options, client, new StringWriter(), stderr, default);
            Assert.Equal(CliCommand.ExitBadArgs, code);
            Assert.Equal("--diff supports the table, CSV, and JSON only." + Environment.NewLine, stderr.ToString());
            Assert.Empty(handler.Urls);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void DiffRequiresForm470()
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--ben", "16057656", "--fy", "2027", "--app", "270000266", "--diff" });
        Assert.Null(parsed.Error);
        Assert.NotNull(parsed.Options);
        Assert.True(parsed.Options.Diff);
        Assert.Equal("270000266", parsed.Options.ApplicationNumber);
        Assert.Contains("--diff applies to Form 470.", CliCommand.Parse(new[] { "--form", "471", "--ben", "143618", "--fy", "2027", "--diff" }).Error, StringComparison.Ordinal);
        Assert.Contains("--diff applies to Form 470.", CliCommand.Parse(new[] { "--form", "both", "--ben", "143618", "--fy", "2027", "--diff" }).Error, StringComparison.Ordinal);
        Assert.Contains("does not take a value", CliCommand.Parse(new[] { "--form", "470", "--ben", "143618", "--fy", "2027", "--diff=yes" }).Error, StringComparison.Ordinal);
        Assert.Contains("--diff", CliCommand.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffPrintsAddedRemovedAndChangedServices()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""
                [
                  {"application_number":"100","billed_entity_number":"143618","billed_entity_name":"Example","form_version":"Original","service_request_id":"1","service_category":"Category 2","service_type":"Internal Connections","function":"Switches","fcc_form_470_status":"Certified"},
                  {"application_number":"100","billed_entity_number":"143618","billed_entity_name":"Example","form_version":"Original","service_request_id":"2","service_category":"Category 2","service_type":"Internal Connections","function":"Cabling","fcc_form_470_status":"Certified"},
                  {"application_number":"100","billed_entity_number":"143618","billed_entity_name":"Example","form_version":"Current","service_request_id":"1","service_category":"Category 2","service_type":"Internal Connections","function":"=Wireless","fcc_form_470_status":"Certified"},
                  {"application_number":"100","billed_entity_number":"143618","billed_entity_name":"Example","form_version":"Current","service_request_id":"3","service_category":"Category 1","service_type":"Data Transmission and/or Internet Access","function":"WAN","fcc_form_470_status":"Certified"}
                ]
                """);
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var output = Path.Combine(directory, "diff.csv");
            var options = Options(FilingForm.Form470, CliFormat.Csv) with { Diff = true, OutputPath = output };
            var code = await CliCommand.RunAsync(options, client, stdout, stderr, default);
            Assert.Equal(CliCommand.ExitFound, code);
            var csv = await File.ReadAllTextAsync(output);
            Assert.Contains("Added", csv, StringComparison.Ordinal);
            Assert.Contains("Removed", csv, StringComparison.Ordinal);
            Assert.Contains("Changed", csv, StringComparison.Ordinal);
            Assert.Contains("'=Wireless", csv, StringComparison.Ordinal);
            Assert.Contains(Form470Diff.Disclaimer, stderr.ToString(), StringComparison.Ordinal);
            var url = Uri.UnescapeDataString(handler.Urls[0]);
            Assert.Contains("jt8s-3q52", url, StringComparison.Ordinal);
            Assert.DoesNotContain("fcc_form_470_status='Certified'", url, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task IdenticalDiffIsEmptyAndExitsFound()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""
                [
                  {"application_number":"200","billed_entity_number":"143618","billed_entity_name":"Example","form_version":"Original","service_request_id":"1","service_category":"Category 1","service_type":"Data Transmission and/or Internet Access","function":"Internet Access","fcc_form_470_status":"Certified"},
                  {"application_number":"200","billed_entity_number":"143618","billed_entity_name":"Example","form_version":"Current","service_request_id":"1","service_category":"Category 1","service_type":"Data Transmission and/or Internet Access","function":"Internet Access","fcc_form_470_status":"Certified"}
                ]
                """);
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form470, CliFormat.Json) with { Diff = true };
            var code = await CliCommand.RunAsync(options, client, stdout, stderr, default);
            Assert.Equal(CliCommand.ExitFound, code);
            Assert.Contains("match", stderr.ToString(), StringComparison.Ordinal);
            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.Equal(0, document.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("applicationCount").GetInt32());
            Assert.Equal(0, document.RootElement.GetProperty("rows").GetArrayLength());
            Assert.True(document.RootElement.GetProperty("diff").GetBoolean());
        }
        finally
        {
            Cleanup(directory);
        }
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
    public async Task ExcelWritesTextCellsWithAFrozenHeaderAndAutofilter()
    {
        var directory = Temp();
        var output = Path.Combine(directory, "nested", "out.xlsx");
        var csvOutput = Path.Combine(directory, "out.csv");
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"260007116","billed_entity_number":"143618","billed_entity_name":"CHULA VISTA ELEM SCHOOL DIST","fcc_form_470_status":"Certified","form_nickname":"=SUM(A1)","form_version":"Current","funding_request_amount":"100.50","pre_discount_eligible_amount":"200.00"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form470, CliFormat.Excel) with { OutputPath = output };
            var code = await CliCommand.RunAsync(options, client, stdout, stderr, default);
            Assert.Equal(CliCommand.ExitFound, code);
            Assert.Equal("", stdout.ToString());
            Assert.Contains("Found 1", stderr.ToString(), StringComparison.Ordinal);

            var sheet = ReadSheet(output);
            Assert.Equal(ExportSchema.BaseColumns, sheet.Rows[0]);
            Assert.DoesNotContain("FrnStatus", sheet.Rows[0]);
            Assert.Equal(ExportSchema.BaseColumns.Count * sheet.Rows.Count, sheet.CellCount);
            Assert.All(sheet.CellTypes, type => Assert.Equal("inlineStr", type));
            Assert.DoesNotContain("f", sheet.ElementNames);
            var nickname = Array.IndexOf(sheet.Rows[0], "Nickname");
            var amount = Array.IndexOf(sheet.Rows[0], "RequestedAmount");
            Assert.Equal("=SUM(A1)", sheet.Rows[1][nickname]);
            Assert.Equal("100.50", sheet.Rows[1][amount]);
            Assert.Equal("1", sheet.FrozenSplit);
            Assert.Equal("A2", sheet.TopLeftCell);
            Assert.Equal("frozen", sheet.PaneState);
            Assert.Equal("A1:" + ColumnName(sheet.Rows[0].Length) + sheet.Rows.Count, sheet.AutoFilter);

            var csvCode = await CliCommand.RunAsync(options with { Format = CliFormat.Csv, OutputPath = csvOutput }, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, csvCode);
            var csv = await File.ReadAllTextAsync(csvOutput);
            Assert.Contains("'=SUM(A1)", csv, StringComparison.Ordinal);
            Assert.Contains("\"100.50\"", csv, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExcelEmptyResultStillWritesTheHeader()
    {
        var directory = Temp();
        var output = Path.Combine(directory, "empty.xlsx");
        try
        {
            using var http = new HttpClient(new ScriptedHandler("[]"));
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var options = Options(FilingForm.Form470, CliFormat.Excel, 2027) with { OutputPath = output };
            var code = await CliCommand.RunAsync(options, client, stdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitNotFound, code);
            Assert.Equal("", stdout.ToString());
            var sheet = ReadSheet(output);
            var header = Assert.Single(sheet.Rows);
            Assert.Equal(ExportSchema.BaseColumns, header);
            Assert.DoesNotContain("FrnStatus", header);
            Assert.Equal(ExportSchema.BaseColumns.Count, sheet.CellCount);
            Assert.All(sheet.CellTypes, type => Assert.Equal("inlineStr", type));
            Assert.Equal("A1:" + ColumnName(header.Length) + "1", sheet.AutoFilter);
            Assert.Equal("frozen", sheet.PaneState);
            Assert.Equal("1", sheet.FrozenSplit);
            Assert.Equal("A2", sheet.TopLeftCell);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExcelRunWithoutOutputDoesNotQuery()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("[]");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stderr = new StringWriter();
            var code = await CliCommand.RunAsync(Options(FilingForm.Form470, CliFormat.Excel), client, new StringWriter(), stderr, default);
            Assert.Equal(CliCommand.ExitBadArgs, code);
            Assert.Equal("--xlsx requires --output <file.xlsx>." + Environment.NewLine, stderr.ToString());
            Assert.Empty(handler.Urls);
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
            var select = Uri.UnescapeDataString(handler.Urls[0]);
            Assert.Contains("fcdl_letter_date", select, StringComparison.Ordinal);
            Assert.Contains("revised_fcdl_date", select, StringComparison.Ordinal);
            Assert.Contains("total_authorized_disbursement", select, StringComparison.Ordinal);
            Assert.Contains("funding_commitment_request", select, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task FrnRowsExportCommitmentColumnsByStatus()
    {
        var directory = Temp();
        var output = Path.Combine(directory, "frn.csv");
        try
        {
            var handler = new ScriptedHandler("""
                [
                  {"funding_request_number":"2099010373","application_number":"201009092","billed_entity_number":"136910","billed_entity_name":"Webster Groves School District","filing_status":"Funded","funding_commitment_request":"14908.32","funding_request_amount":"14908.32","fcdl_letter_date":"2020-05-09T00:00:00.000","total_authorized_disbursement":"14908.32"},
                  {"funding_request_number":"1799022629","application_number":"171000001","billed_entity_number":"136910","billed_entity_name":"Webster Groves School District","filing_status":"Pending","funding_commitment_request":"14908.32","funding_request_amount":"14908.32"},
                  {"funding_request_number":"1799008573","application_number":"171000002","billed_entity_number":"136910","billed_entity_name":"Webster Groves School District","filing_status":"Denied","funding_commitment_request":"0","funding_request_amount":"0","fcdl_letter_date":"2017-08-25T00:00:00.000"},
                  {"funding_request_number":"2099010999","application_number":"201009092","billed_entity_number":"136910","billed_entity_name":"Webster Groves School District","filing_status":"Funded","funding_commitment_request":"14908.32","funding_request_amount":"14908.32","fcdl_letter_date":"2020-05-09T00:00:00.000","revised_fcdl_date":"2026-07-14T00:00:00.000","total_authorized_disbursement":"14908.32"}
                ]
                """);
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var options = Options(FilingForm.Form471, CliFormat.Csv) with { Ben = "136910", FundingYear = 2020, FundingRequestNumber = "2099010373", OutputPath = output };
            var code = await CliCommand.RunAsync(options, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            var parsed = ParseCsv(await File.ReadAllTextAsync(output));
            Assert.Equal(new[] { "FrnStatus", "CommittedAmount", "FcdlDate", "RevisedFcdlDate", "DisbursedAmount" }, parsed[0][17..]);
            Assert.Equal(new[] { "Funded", "14908.32", "2020-05-09", "", "14908.32" }, parsed[1][17..]);
            Assert.Equal("14908.32", parsed[1][10]);
            Assert.Equal(new[] { "Pending", "", "", "", "" }, parsed[2][17..]);
            Assert.Equal("14908.32", parsed[2][10]);
            Assert.Equal(new[] { "Denied", "", "2017-08-25", "", "" }, parsed[3][17..]);
            Assert.Equal("0", parsed[3][10]);
            Assert.Equal(new[] { "Funded", "14908.32", "2020-05-09", "2026-07-14", "14908.32" }, parsed[4][17..]);

            var jsonOut = new StringWriter();
            var jsonCode = await CliCommand.RunAsync(options with { Format = CliFormat.Json, OutputPath = null }, client, jsonOut, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, jsonCode);
            using var document = JsonDocument.Parse(jsonOut.ToString());
            var rows = document.RootElement.GetProperty("rows");
            Assert.Equal(4, rows.GetArrayLength());
            Assert.Equal("14908.32", rows[0].GetProperty("committedAmount").GetString());
            Assert.Equal("", rows[1].GetProperty("committedAmount").GetString());
            Assert.Equal("", rows[2].GetProperty("committedAmount").GetString());
            Assert.Equal("2026-07-14", rows[3].GetProperty("revisedFcdlDate").GetString());
            Assert.Equal("14908.32", rows[3].GetProperty("disbursedAmount").GetString());
            Assert.Contains("CommittedAmount", CliCommand.HelpText, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void NameCanReplaceBen()
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--name", "San Diego City", "--fy", "2027" });
        Assert.Null(parsed.Error);
        Assert.NotNull(parsed.Options);
        Assert.Null(parsed.Options.Ben);
        Assert.Equal("San Diego City", parsed.Options.ApplicantName);
        Assert.False(parsed.Options.LinkForms);
    }

    [Fact]
    public void ShortNameIsRejected()
    {
        var parsed = CliCommand.Parse(new[] { "--form", "470", "--name", "A", "--fy", "2027" });
        Assert.Contains("2 characters", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void BothFormsRejectFrn()
    {
        var parsed = CliCommand.Parse(new[] { "--form", "both", "--ben", "143662", "--fy", "2027", "--frn", "2699003124" });
        Assert.Contains("cannot be combined", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void NameSearchUsesThePublishedApplicantColumn()
    {
        var filter = UsacClient.SearchFilter(null, "San Diego City", 2027, FilingForm.Form470);
        Assert.Contains("contains(upper(billed_entity_name),'SAN DIEGO CITY')", filter, StringComparison.Ordinal);
        Assert.Contains("fcc_form_470_status='Certified'", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("billed_entity_number", filter, StringComparison.Ordinal);

        var linked = UsacClient.SearchFilter("143662", "O'Brien", 2027, FilingForm.Form471);
        Assert.Equal(
            "epc_organization_id='143662' AND contains(upper(organization_name),'O''BRIEN') AND funding_year='2027' AND certified_datetime IS NOT NULL",
            linked);
    }

    [Fact]
    public void SourceLinkIdentifiesTheOpenDataRecord()
    {
        var url = UsacSources.Application(FilingForm.Form470, "260007116");
        Assert.StartsWith("https://opendata.usac.org/resource/jt8s-3q52.json?", url, StringComparison.Ordinal);
        var text = Uri.UnescapeDataString(url);
        Assert.Contains("application_number='260007116'", text, StringComparison.Ordinal);
        var frn = Uri.UnescapeDataString(UsacSources.FundingRequest("2699003124", "261003372"));
        Assert.Contains("qdmp-ygft", frn, StringComparison.Ordinal);
        Assert.Contains("funding_request_number='2699003124'", frn, StringComparison.Ordinal);
        var fallback = Uri.UnescapeDataString(UsacSources.FundingRequest("", "261003372"));
        Assert.Contains("qdmp-ygft", fallback, StringComparison.Ordinal);
        Assert.Contains("application_number='261003372'", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("9s6i-myen", fallback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TablePrintsTheFullSourceLink()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"260007116","billed_entity_number":"143618","billed_entity_name":"CHULA VISTA ELEM SCHOOL DIST","fcc_form_470_status":"Certified","form_version":"Current"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var code = await CliCommand.RunAsync(Options(FilingForm.Form470), client, stdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            var source = UsacSources.Application(FilingForm.Form470, "260007116");
            Assert.Contains(source, stdout.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(source[..20] + "...", stdout.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task NameSearchQueriesThePublishedName()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"270000001","billed_entity_number":"143662","billed_entity_name":"SAN DIEGO CITY UNIF SCH DIST","fcc_form_470_status":"Certified","form_version":"Current"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var options = Options(FilingForm.Form470, year: 2027) with { Ben = null, ApplicantName = "San Diego City" };
            var code = await CliCommand.RunAsync(options, client, stdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            var query = Uri.UnescapeDataString(handler.Urls[0]);
            Assert.Contains("contains(upper(billed_entity_name),'SAN DIEGO CITY')", query, StringComparison.Ordinal);
            Assert.Contains("143662", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains(UsacSources.Application(FilingForm.Form470, "270000001"), stdout.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task LinkingReadsBothFormsAndKeepsEachSource()
    {
        var directory = Temp();
        try
        {
            var handler = new RoutingHandler(url => url.Contains("9s6i-myen", StringComparison.Ordinal)
                ? """[{"application_number":"271000001","epc_organization_id":"143662","billed_entity_number":"143662","billed_entity_name":"SAN DIEGO CITY UNIF SCH DIST","form_version":"Current","filing_status":"Certified","certified_date_time":"2026-02-01T00:00:00.000"}]"""
                : """[{"application_number":"270000001","billed_entity_number":"143662","billed_entity_name":"SAN DIEGO CITY UNIF SCH DIST","fcc_form_470_status":"Certified","form_version":"Current","certified_date_time":"2026-07-23T00:00:00.000"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form470, CliFormat.Json, 2027) with { Form = null, LinkForms = true, Ben = "143662" };
            var code = await CliCommand.RunAsync(options, client, stdout, stderr, default);
            Assert.Equal(CliCommand.ExitFound, code);
            Assert.Contains("jt8s-3q52", string.Join('\n', handler.Urls), StringComparison.Ordinal);
            Assert.Contains("9s6i-myen", string.Join('\n', handler.Urls), StringComparison.Ordinal);
            Assert.Contains("does not establish", stderr.ToString(), StringComparison.Ordinal);
            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("linked").GetBoolean());
            Assert.Equal(2, document.RootElement.GetProperty("count").GetInt32());
            var rows = document.RootElement.GetProperty("rows");
            Assert.Equal(470, rows[0].GetProperty("form").GetInt32());
            Assert.Equal(471, rows[1].GetProperty("form").GetInt32());
            Assert.Contains("jt8s-3q52", rows[0].GetProperty("source").GetString(), StringComparison.Ordinal);
            Assert.Contains("9s6i-myen", rows[1].GetProperty("source").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task LinkingNothingIsNotProofOfNoFiling()
    {
        var directory = Temp();
        try
        {
            using var http = new HttpClient(new ScriptedHandler("[]"));
            using var client = new UsacClient(http, directory);
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form470, year: 2027) with { Form = null, LinkForms = true, Ben = "143662" };
            var code = await CliCommand.RunAsync(options, client, new StringWriter(), stderr, default);
            Assert.Equal(CliCommand.ExitNotFound, code);
            var message = stderr.ToString();
            Assert.Contains("No certified Form 470 or Form 471", message, StringComparison.Ordinal);
            Assert.Contains("not proof", message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task LinkingStopsWhenUsacIsUnreachable()
    {
        var directory = Temp();
        try
        {
            var handler = new RoutingHandler(_ => "[]", url => url.Contains("9s6i-myen", StringComparison.Ordinal) ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form470) with { Form = null, LinkForms = true };
            var code = await CliCommand.RunAsync(options, client, stdout, stderr, default);
            Assert.Equal(CliCommand.ExitUsacUnreachable, code);
            Assert.Contains("were not linked", stderr.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout.ToString());
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task JsonAndCsvIncludeTheSourceLink()
    {
        var directory = Temp();
        var output = Path.Combine(directory, "out.csv");
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"261003372","epc_organization_id":"143618","billed_entity_name":"Chula Vista Elem School Dist","form_version":"Current","filing_status":"Committed","certified_date_time":"2026-03-01T00:00:00.000"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var jsonCode = await CliCommand.RunAsync(Options(FilingForm.Form471, CliFormat.Json), client, stdout, new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, jsonCode);
            using var document = JsonDocument.Parse(stdout.ToString());
            var source = document.RootElement.GetProperty("rows")[0].GetProperty("source").GetString();
            Assert.False(string.IsNullOrWhiteSpace(source));
            Assert.Contains("9s6i-myen", source, StringComparison.Ordinal);
            Assert.Contains("261003372", Uri.UnescapeDataString(source!), StringComparison.Ordinal);

            var csvOptions = Options(FilingForm.Form471, CliFormat.Csv) with { OutputPath = output };
            var csvCode = await CliCommand.RunAsync(csvOptions, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, csvCode);
            var csv = await File.ReadAllTextAsync(output);
            var parsed = ParseCsv(csv);
            var header = parsed[0];
            Assert.Equal("Source", header[13]);
            Assert.Equal("Linked", header[14]);
            Assert.Equal("Form470Count", header[15]);
            Assert.Equal("Form471Count", header[16]);
            Assert.Equal(new[] { "FrnStatus", "CommittedAmount", "FcdlDate", "RevisedFcdlDate", "DisbursedAmount" }, header[17..]);
            Assert.Equal(new[] { "", "", "", "", "" }, parsed[1][17..]);
            Assert.Contains("9s6i-myen", parsed[1][13], StringComparison.Ordinal);
            Assert.Equal("false", parsed[1][14]);
            Assert.Equal("", parsed[1][15]);
            Assert.Equal("", parsed[1][16]);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task LinkedCsvIncludesSourceAndLinkFields()
    {
        var directory = Temp();
        var output = Path.Combine(directory, "linked.csv");
        try
        {
            var handler = new RoutingHandler(url => url.Contains("9s6i-myen", StringComparison.Ordinal)
                ? """[{"application_number":"271000001","epc_organization_id":"143662","billed_entity_number":"143662","billed_entity_name":"SAN DIEGO CITY UNIF SCH DIST","form_version":"Current","filing_status":"Certified","certified_date_time":"2026-02-01T00:00:00.000"}]"""
                : """[{"application_number":"270000001","billed_entity_number":"143662","billed_entity_name":"SAN DIEGO CITY UNIF SCH DIST","fcc_form_470_status":"Certified","form_version":"Current","certified_date_time":"2026-07-23T00:00:00.000"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var options = Options(FilingForm.Form470, CliFormat.Csv, 2027) with { Form = null, LinkForms = true, Ben = "143662", OutputPath = output };
            var code = await CliCommand.RunAsync(options, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            var parsed = ParseCsv(await File.ReadAllTextAsync(output));
            Assert.Equal(new[] { "Form", "FundingYear", "BEN", "Name", "ApplicationNumber", "FundingRequestNumber", "Status", "CertifiedDate", "Nickname", "Services", "RequestedAmount", "PreDiscountAmount", "PDF", "Source", "Linked", "Form470Count", "Form471Count", "FrnStatus", "CommittedAmount", "FcdlDate", "RevisedFcdlDate", "DisbursedAmount" }, parsed[0]);
            Assert.Equal(3, parsed.Count);
            Assert.Equal("470", parsed[1][0]);
            Assert.Equal("471", parsed[2][0]);
            Assert.Equal(UsacSources.Application(FilingForm.Form470, "270000001"), parsed[1][13]);
            Assert.Equal(UsacSources.Application(FilingForm.Form471, "271000001"), parsed[2][13]);
            Assert.Equal("true", parsed[1][14]);
            Assert.Equal("true", parsed[2][14]);
            Assert.Equal("1", parsed[1][15]);
            Assert.Equal("1", parsed[1][16]);
            Assert.Equal("1", parsed[2][15]);
            Assert.Equal("1", parsed[2][16]);
            Assert.Equal("143662", parsed[1][2]);
            Assert.Equal("2027", parsed[1][1]);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task NameOnlyLinkRequiresOnePublishedBen()
    {
        var directory = Temp();
        try
        {
            var handler = new RoutingHandler(url => url.Contains("9s6i-myen", StringComparison.Ordinal)
                ? """[{"application_number":"271000001","epc_organization_id":"143663","billed_entity_number":"143663","billed_entity_name":"San Diego School Dist-Area 1","form_version":"Current","filing_status":"Certified","certified_date_time":"2026-02-01T00:00:00.000"}]"""
                : """[{"application_number":"270000001","billed_entity_number":"143662","billed_entity_name":"SAN DIEGO CITY UNIF SCH DIST","fcc_form_470_status":"Certified","form_version":"Current","certified_date_time":"2026-07-23T00:00:00.000"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form470, year: 2027) with { Form = null, LinkForms = true, Ben = null, ApplicantName = "San Diego" };
            var code = await CliCommand.RunAsync(options, client, stdout, stderr, default);
            Assert.Equal(CliCommand.ExitLookupFailed, code);
            Assert.Equal(string.Empty, stdout.ToString());
            var message = stderr.ToString();
            Assert.Contains("were not linked", message, StringComparison.Ordinal);
            Assert.Contains("143662", message, StringComparison.Ordinal);
            Assert.Contains("143663", message, StringComparison.Ordinal);
            Assert.Contains("--ben", message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task LinkedCsvRecordsAQueriedEmptyFormAsZero()
    {
        var directory = Temp();
        var output = Path.Combine(directory, "linked.csv");
        try
        {
            var handler = new RoutingHandler(url => url.Contains("9s6i-myen", StringComparison.Ordinal)
                ? "[]"
                : """[{"application_number":"270000001","billed_entity_number":"143662","billed_entity_name":"SAN DIEGO CITY UNIF SCH DIST","fcc_form_470_status":"Certified","form_version":"Current"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var options = Options(FilingForm.Form470, CliFormat.Csv, 2027) with { Form = null, LinkForms = true, Ben = "143662", OutputPath = output };
            var code = await CliCommand.RunAsync(options, client, new StringWriter(), new StringWriter(), default);
            Assert.Equal(CliCommand.ExitFound, code);
            var parsed = ParseCsv(await File.ReadAllTextAsync(output));
            Assert.Equal(2, parsed.Count);
            Assert.Equal("470", parsed[1][0]);
            Assert.Equal("true", parsed[1][14]);
            Assert.Equal("1", parsed[1][15]);
            Assert.Equal("0", parsed[1][16]);
            Assert.Equal(UsacSources.Application(FilingForm.Form470, "270000001"), parsed[1][13]);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task NameOnlyForm471WithoutAnApplicantIdIsNotEmpty()
    {
        var directory = Temp();
        try
        {
            var handler = new ScriptedHandler("""[{"application_number":"271000009","billed_entity_name":"SAN DIEGO CITY UNIF SCH DIST","form_version":"Current","filing_status":"Certified"}]""");
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form471) with { Ben = null, ApplicantName = "San Diego City" };
            var code = await CliCommand.RunAsync(options, client, new StringWriter(), stderr, default);
            Assert.Equal(CliCommand.ExitLookupFailed, code);
            Assert.Contains("not a no-filings result", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task BroadSearchOverTheCapExitsLookupFailed()
    {
        var directory = Temp();
        try
        {
            var handler = new CapHandler();
            using var http = new HttpClient(handler);
            using var client = new UsacClient(http, directory);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var options = Options(FilingForm.Form470) with { Ben = null, ApplicantName = "School" };
            var code = await CliCommand.RunAsync(options, client, stdout, stderr, default);
            Assert.Equal(CliCommand.ExitLookupFailed, code);
            Assert.Equal(string.Empty, stdout.ToString());
            var message = stderr.ToString();
            Assert.Contains("Lookup failed; no filing determination was made.", message, StringComparison.Ordinal);
            Assert.Contains("50001 rows match; narrow the name or pass --ben", message, StringComparison.Ordinal);
            Assert.DoesNotContain(handler.Urls, url => url.Contains("/api/views/", StringComparison.Ordinal));
            Assert.DoesNotContain(handler.Urls, url => Uri.UnescapeDataString(url).Contains("$offset", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void OptionsAndRowRecordsAreInternal()
    {
        Assert.False(typeof(CliOptions).IsPublic);
        Assert.False(typeof(CliRow).IsPublic);
        _ = new CliOptions(FilingForm.Form470, false, "143618", null, 2026, null, null, CliFormat.Table, null);
        _ = new CliRow(470, "260007116", "", "Certified", "2026-01-01", "", "143618", "District", "", "", "", "", "https://opendata.usac.org/resource/jt8s-3q52.json");
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
                    rows.Add(row.ToArray());
                row = new List<string>();
            }
            else if (character != '\r')
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

    static CliOptions Options(FilingForm form, CliFormat format = CliFormat.Table, int year = 2026) =>
        new(form, false, "143618", null, year, null, null, format, null);

    static SheetSnapshot ReadSheet(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(entry);
        using var stream = entry.Open();
        var xml = System.Xml.Linq.XDocument.Load(stream);
        var rows = new List<string[]>();
        var types = new List<string>();
        var count = 0;
        foreach (var row in xml.Descendants().Where(element => element.Name.LocalName == "row"))
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements().Where(element => element.Name.LocalName == "c"))
            {
                count++;
                types.Add(cell.Attribute("t")?.Value ?? "");
                cells.Add(cell.Descendants().First(element => element.Name.LocalName == "t").Value);
            }

            rows.Add(cells.ToArray());
        }

        var pane = xml.Descendants().First(element => element.Name.LocalName == "pane");
        var filter = xml.Descendants().First(element => element.Name.LocalName == "autoFilter");
        return new SheetSnapshot(
            rows,
            count,
            types,
            xml.Descendants().Select(element => element.Name.LocalName).ToHashSet(StringComparer.Ordinal),
            pane.Attribute("ySplit")?.Value,
            pane.Attribute("topLeftCell")?.Value,
            pane.Attribute("state")?.Value,
            filter.Attribute("ref")?.Value);
    }

    static string ColumnName(int n)
    {
        var result = "";
        while (n > 0)
        {
            n--;
            result = (char)('A' + n % 26) + result;
            n /= 26;
        }

        return result;
    }

    sealed record SheetSnapshot(
        List<string[]> Rows,
        int CellCount,
        List<string> CellTypes,
        HashSet<string> ElementNames,
        string? FrozenSplit,
        string? TopLeftCell,
        string? PaneState,
        string? AutoFilter);

    static string Temp() => Path.Combine(Path.GetTempPath(), "ERateCli-" + Guid.NewGuid().ToString("N"));

    static void Cleanup(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    sealed class CapHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            var body = query.Contains("count(*)", StringComparison.Ordinal) ? "[{\"count\":\"50001\"}]" : "{\"rowsUpdatedAt\":1}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
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
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            if (status == HttpStatusCode.OK)
            {
                if (request.RequestUri.AbsolutePath.Contains("/api/views/", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"rowsUpdatedAt\":1}") });
                }

                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                if (query.Contains("count(*)", StringComparison.Ordinal))
                {
                    var count = body.Trim() == "[]" ? "0" : "1";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"count\":\"" + count + "\"}]") });
                }
            }

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    sealed class RoutingHandler : HttpMessageHandler
    {
        readonly Func<string, string> body;
        readonly Func<string, HttpStatusCode> status;
        public List<string> Urls { get; } = new();

        public RoutingHandler(Func<string, string> body, Func<string, HttpStatusCode>? status = null)
        {
            this.body = body;
            this.status = status ?? (_ => HttpStatusCode.OK);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            if (request.RequestUri.AbsolutePath.Contains("/api/views/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"rowsUpdatedAt\":1}") });
            }

            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            if (query.Contains("count(*)", StringComparison.Ordinal))
            {
                var rows = body(url);
                var count = rows.Trim() == "[]" ? "0" : "1";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"count\":\"" + count + "\"}]") });
            }

            return Task.FromResult(new HttpResponseMessage(status(url)) { Content = new StringContent(body(url)) });
        }
    }
}
