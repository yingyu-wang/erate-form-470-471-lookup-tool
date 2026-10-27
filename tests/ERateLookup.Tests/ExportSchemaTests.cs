using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ERateLookup.Core;
using Xunit;

namespace ERateLookup.Tests;

public class ExportSchemaTests
{
    static readonly string[] BaseHeaders =
    [
        "Form",
        "FundingYear",
        "BEN",
        "Name",
        "ApplicationNumber",
        "FundingRequestNumber",
        "Status",
        "CertifiedDate",
        "Nickname",
        "Services",
        "RequestedAmount",
        "PreDiscountAmount",
        "PDF",
        "Source",
        "Linked",
        "Form470Count",
        "Form471Count",
    ];

    [Fact]
    public void BaseHeadersAreTheSharedLookupColumns()
    {
        Assert.Equal(BaseHeaders, ExportSchema.BaseColumns);
        Assert.Equal(BaseHeaders, ExportSchema.Names(ExportColumnGroup.Base));
    }

    [Fact]
    public void OptionalGroupsStayOutOfTheBaseUntilSelected()
    {
        Assert.Equal(new[] { "ExportedUTC", "LookupFetchedUTC" }, ExportSchema.Names(ExportColumnGroup.LookupTimestamp));
        Assert.Equal(new[] { "LookupResult", "DirectoryUpdatedUTC" }, ExportSchema.Names(ExportColumnGroup.LookupContext));
        Assert.Equal(
            new[]
            {
                "BudgetCycle",
                "USACBudgetStatus",
                "BudgetDisplayStatus",
                "TotalC2BudgetPreDiscount",
                "CommittedC2BudgetPreDiscount",
                "PendingC2BudgetPreDiscount",
                "ReportedRemainingC2BudgetPreDiscount",
            },
            ExportSchema.Names(ExportColumnGroup.Budget));
        Assert.Equal(FrnCommitment.Columns, ExportSchema.Names(ExportColumnGroup.FrnCommitment));
        Assert.Equal(
            new[] { "LookupOutcome", "Change", "SourceAndNotes", "AllowableContractDate" },
            ExportSchema.Names(ExportColumnGroup.Portfolio));
        Assert.Equal(FrnCommitment.Columns, ExportSchema.Columns(ExportSchema.CliColumns).Skip(BaseHeaders.Length));

        foreach (var name in ExportSchema.Names(ExportColumnGroup.Budget)
            .Concat(ExportSchema.Names(ExportColumnGroup.LookupTimestamp))
            .Concat(ExportSchema.Names(ExportColumnGroup.LookupContext))
            .Concat(ExportSchema.Names(ExportColumnGroup.FrnCommitment))
            .Concat(ExportSchema.Names(ExportColumnGroup.Portfolio)))
        {
            Assert.DoesNotContain(name, ExportSchema.BaseColumns);
        }

        var selected = new ExportColumns
        {
            IncludeLookupTimestamps = true,
            IncludeLookupContext = true,
            IncludeFrnCommitment = true,
            IncludeBudget = true,
            IncludePortfolio = true,
        };
        var headers = ExportSchema.Columns(selected);
        Assert.Equal(
            ExportSchema.Names(ExportColumnGroup.LookupTimestamp)
                .Concat(BaseHeaders)
                .Concat(ExportSchema.Names(ExportColumnGroup.FrnCommitment))
                .Concat(ExportSchema.Names(ExportColumnGroup.LookupContext))
                .Concat(ExportSchema.Names(ExportColumnGroup.Budget))
                .Concat(ExportSchema.Names(ExportColumnGroup.Portfolio)),
            headers);
        Assert.Equal(headers.Count, ExportSchema.ToRow(SampleRow(), selected).Length);
    }

    [Fact]
    public void PendingRenamesAndPortfolioTrailingColumnsAreDefined()
    {
        Assert.Equal("Name", ExportSchema.PendingHeaderRenames["Customer"]);
        Assert.Equal("Form", ExportSchema.PendingHeaderRenames["FormType"]);
        Assert.Equal("Status", ExportSchema.PendingHeaderRenames["FilingStatus"]);
        Assert.Equal("RequestedAmount", ExportSchema.PendingHeaderRenames["RequestedFundingUSD"]);
        Assert.Equal("PreDiscountAmount", ExportSchema.PendingHeaderRenames["PreDiscountEligibleUSD"]);

        var portfolio = ExportSchema.Columns(new ExportColumns { IncludePortfolio = true });
        Assert.Equal(BaseHeaders, portfolio.Take(BaseHeaders.Length));
        Assert.Equal(ExportSchema.Names(ExportColumnGroup.Portfolio), portfolio.Skip(BaseHeaders.Length));
    }

    [Fact]
    public void FilingAndFundingRequestRowsUseTheBaseColumnOrder()
    {
        var filing = new Filing(
            "260007116",
            "",
            "CHULA VISTA ELEM SCHOOL DIST",
            "470 C2",
            "2025-12-19",
            "Internal Connections",
            "https://publicdata.usac.org/example.pdf",
            "Certified",
            "2025-12-01",
            "100.00",
            "200.00");
        var row = ExportSchema.WithDisplayedBen(
            ExportSchema.FromFiling(FilingForm.Form470, filing, 2026, false, null, null),
            "143618");
        Assert.Equal(
            new[]
            {
                "470",
                "2026",
                "143618",
                "CHULA VISTA ELEM SCHOOL DIST",
                "260007116",
                "",
                "Certified",
                "2025-12-19",
                "470 C2",
                "Internal Connections",
                "100.00",
                "200.00",
                "https://publicdata.usac.org/example.pdf",
                UsacSources.Application(FilingForm.Form470, "260007116"),
                "false",
                "",
                "",
            },
            ExportSchema.ToRow(row));

        var frn = ExportSchema.FromFundingRequest(
            new UsacRow
            {
                Number = "261003372",
                FundingRequestNumber = "",
                Status = " ",
                Certified = "2026-03-01T00:00:00.000",
                Nickname = "Att Ase",
                Name = "Chula Vista Elem School Dist",
                Service = "Data Transmission and/or Internet Access",
                RequestedAmount = "166320",
            },
            2026,
            false,
            null,
            null);
        Assert.Equal("471", frn.Form);
        Assert.Equal("Unknown", frn.Status);
        Assert.Equal("", frn.FrnStatus);
        Assert.Equal("", frn.CommittedAmount);
        Assert.Equal("2026-03-01", frn.CertifiedDate);
        Assert.Equal(UsacSources.FundingRequest("", "261003372"), frn.Source);
        Assert.Equal("", frn.Ben);

        var funded = ExportSchema.FromFundingRequest(
            new UsacRow
            {
                Number = "201009092",
                FundingRequestNumber = "2099010373",
                Status = "Funded",
                Name = "Webster Groves School District",
                Ben = "136910",
                FundingCommitmentRequest = "14908.32",
                RequestedAmount = "14908.32",
                FcdlLetterDate = "2020-05-09T00:00:00.000",
                RevisedFcdlDate = "2026-07-14T00:00:00.000",
                TotalAuthorizedDisbursement = "14908.32",
            },
            2020,
            false,
            null,
            null);
        Assert.Equal("Funded", funded.Status);
        Assert.Equal("14908.32", funded.RequestedAmount);
        Assert.Equal(
            new[] { "Funded", "14908.32", "2020-05-09", "2026-07-14", "14908.32" },
            ExportSchema.ToRow(funded, ExportSchema.CliColumns).Skip(BaseHeaders.Length));
        Assert.Equal(BaseHeaders.Length, ExportSchema.ToRow(funded).Length);
        var pending = ExportSchema.FromFundingRequest(
            new UsacRow
            {
                Status = "Pending",
                FundingCommitmentRequest = "14908.32",
                RequestedAmount = "14908.32",
            },
            2020,
            false,
            null,
            null);
        Assert.Equal("", pending.CommittedAmount);
        Assert.Equal("14908.32", pending.RequestedAmount);
        Assert.Equal("", ExportSchema.FromFiling(FilingForm.Form470, filing, 2026, false, null, null).FrnStatus);
    }

    [Fact]
    public void WriteJsonUsesTheCliEnvelope()
    {
        var fetched = new DateTimeOffset(2026, 10, 19, 1, 2, 3, TimeSpan.Zero);
        var metadata = new ExportMetadata
        {
            Form = 470,
            Linked = false,
            Ben = "143618",
            ApplicantName = null,
            FundingYear = 2026,
            FetchedAt = fetched,
        };
        var rows = new[] { new SampleJsonRow("260007116", UsacSources.Application(FilingForm.Form470, "260007116")) };
        using var writer = new StringWriter();
        DataExporter.WriteJson(writer, metadata, rows);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(new[] { "form", "linked", "ben", "applicantName", "fundingYear", "applicationNumber", "fundingRequestNumber", "fetchedAt", "count", "rows" }, root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(root.TryGetProperty("form470Count", out _));
        Assert.False(root.TryGetProperty("form471Count", out _));
        Assert.Equal(470, root.GetProperty("form").GetInt32());
        Assert.False(root.GetProperty("linked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("applicantName").ValueKind);
        Assert.Equal(1, root.GetProperty("count").GetInt32());
        Assert.Equal(root.GetProperty("rows").GetArrayLength(), root.GetProperty("count").GetInt32());
        var fetchedAt = root.GetProperty("fetchedAt").GetString();
        Assert.NotNull(fetchedAt);
        Assert.Equal(fetched, DateTimeOffset.Parse(fetchedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        Assert.Equal(fetched.ToString("O", CultureInfo.InvariantCulture), fetchedAt);
        var row = root.GetProperty("rows")[0];
        Assert.Equal(new[] { "applicationNumber", "source" }, row.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("260007116", row.GetProperty("applicationNumber").GetString());
        Assert.False(row.TryGetProperty("ApplicationNumber", out _));

        var linked = new ExportMetadata
        {
            Form = null,
            Linked = true,
            Form470Count = 1,
            Form471Count = 0,
            Ben = "143618",
            FundingYear = 2027,
            FetchedAt = fetched,
        };
        using var linkedWriter = new StringWriter();
        DataExporter.WriteJson(linkedWriter, linked, Array.Empty<SampleJsonRow>());
        using var linkedDocument = JsonDocument.Parse(linkedWriter.ToString());
        Assert.Equal(JsonValueKind.Null, linkedDocument.RootElement.GetProperty("form").ValueKind);
        Assert.True(linkedDocument.RootElement.GetProperty("linked").GetBoolean());
        Assert.Equal(1, linkedDocument.RootElement.GetProperty("form470Count").GetInt32());
        Assert.Equal(0, linkedDocument.RootElement.GetProperty("form471Count").GetInt32());
        Assert.Equal(0, linkedDocument.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void CsvExcelAndColumnJsonShareTheSchemaCells()
    {
        var headers = ExportSchema.BaseColumns.ToArray();
        var cells = ExportSchema.ToRow(SampleRow());
        var table = new List<string[]> { headers, cells };
        var directory = Path.Combine(Path.GetTempPath(), "ERateSchema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var csvPath = Path.Combine(directory, "rows.csv");
            var excelPath = Path.Combine(directory, "rows.xlsx");
            var jsonPath = Path.Combine(directory, "rows.json");
            DataExporter.WriteCsv(csvPath, table);
            DataExporter.WriteExcel(excelPath, table);
            var fetched = new DateTimeOffset(2026, 10, 19, 1, 2, 3, TimeSpan.Zero);
            var metadata = new ExportMetadata { Form = 470, Ben = "143618", FundingYear = 2026, FetchedAt = fetched };
            DataExporter.WriteColumnJson(jsonPath, metadata, headers, new[] { cells });

            var csvBytes = File.ReadAllBytes(csvPath);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csvBytes.Take(3));
            Assert.Contains("\"260007116\"", Encoding.UTF8.GetString(csvBytes), StringComparison.Ordinal);
            Assert.Equal(table, ReadSheet(excelPath));

            var jsonBytes = File.ReadAllBytes(jsonPath);
            Assert.False(jsonBytes.Length >= 3 && jsonBytes[0] == 0xEF && jsonBytes[1] == 0xBB && jsonBytes[2] == 0xBF);
            using var document = JsonDocument.Parse(jsonBytes);
            var row = document.RootElement.GetProperty("rows")[0];
            Assert.Equal(headers, row.EnumerateObject().Select(property => property.Name).ToArray());
            for (var i = 0; i < headers.Length; i++)
            {
                Assert.Equal(cells[i], row.GetProperty(headers[i]).GetString());
            }

            Assert.Equal(document.RootElement.GetProperty("rows").GetArrayLength(), document.RootElement.GetProperty("count").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WriteJsonStopsBetweenRowsWhenCanceled()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateSchema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var canceled = new CancellationTokenSource();
            var rows = new[]
            {
                new CancelingRow("1", canceled, stop: false),
                new CancelingRow("2", canceled, stop: true),
            };
            var metadata = new ExportMetadata { FundingYear = 2026, FetchedAt = DateTimeOffset.UnixEpoch };
            var writer = new StringWriter();
            Assert.Throws<OperationCanceledException>(() => DataExporter.WriteJson(writer, metadata, rows, canceled.Token));
            Assert.Equal("", writer.ToString());
            var path = Path.Combine(directory, "rows.json");
            Assert.Throws<OperationCanceledException>(() => DataExporter.WriteJson(path, metadata, rows, canceled.Token));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WriteJsonHonorsCancellationBeforeWriting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateSchema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rows.json");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var metadata = new ExportMetadata { FundingYear = 2026, FetchedAt = DateTimeOffset.UnixEpoch };
            Assert.Throws<OperationCanceledException>(() => DataExporter.WriteJson(path, metadata, new[] { new SampleJsonRow("1", "2") }, canceled.Token));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExcelReplacesTheDestinationOnlyAfterTheWorkbookIsComplete()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateSchema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var previous = "previous workbook"u8.ToArray();
            var kept = Path.Combine(directory, "kept.xlsx");
            var replaced = Path.Combine(directory, "replaced.xlsx");
            File.WriteAllBytes(kept, previous);
            File.WriteAllBytes(replaced, previous);

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => DataExporter.WriteExcel(kept, new List<string[]> { new[] { "Header" } }, canceled.Token));
            Assert.Equal(previous, File.ReadAllBytes(kept));

            Assert.Throws<ArgumentException>(() => DataExporter.WriteExcel(kept, new List<string[]> { new[] { "Header" } }, default, "'Sheet"));
            Assert.Equal(previous, File.ReadAllBytes(kept));

            using var during = new CancellationTokenSource();
            var rows = new CancelOnSecondRow(during, new[] { "Header" }, new[] { "100.50" });
            Assert.Throws<OperationCanceledException>(() => DataExporter.WriteExcel(kept, rows, during.Token));
            Assert.Equal(previous, File.ReadAllBytes(kept));
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), path => path.EndsWith(".tmp", StringComparison.Ordinal));

            DataExporter.WriteExcel(replaced, new List<string[]> { new[] { "Header" }, new[] { "100.50" } }, default);
            Assert.NotEqual(previous, File.ReadAllBytes(replaced));
            Assert.Equal(new[] { "Header" }, ReadSheet(replaced)[0]);
            Assert.Equal("100.50", ReadSheet(replaced)[1][0]);
            Assert.Equal(DataExporter.LookupSheetName, ReadWorkbookSheetName(replaced));
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), path => path.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void UntypedDefaultSelectsTheCancellationTokenAndTheLookupSheet()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateSchema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rows.xlsx");
            var named = Path.Combine(directory, "named.xlsx");
            DataExporter.WriteExcel(path, new List<string[]> { new[] { "Amount" }, new[] { "100.50" } }, default);
            DataExporter.WriteExcel(named, new List<string[]> { new[] { "Amount" } }, default, "O'Brien");
            Assert.Equal(DataExporter.LookupSheetName, ReadWorkbookSheetName(path));
            Assert.Equal("O'Brien", ReadWorkbookSheetName(named));
            Assert.Equal("100.50", ReadSheet(path)[1][0]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("'Sheet")]
    [InlineData("Sheet'")]
    [InlineData("'Sheet'")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a[b")]
    [InlineData("a]b")]
    [InlineData("bad\u0001name")]
    [InlineData("bad\u0008name")]
    [InlineData("\uFFFF")]
    public void ExcelRejectsSheetNamesExcelOrXmlWouldReject(string sheetName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateSchema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rows.xlsx");
            var previous = "previous workbook"u8.ToArray();
            File.WriteAllBytes(path, previous);
            var error = Assert.Throws<ArgumentException>(() => DataExporter.WriteExcel(path, new List<string[]> { new[] { "Header" } }, default, sheetName));
            Assert.Contains("sheet name", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(previous, File.ReadAllBytes(path));
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), file => file.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExcelAcceptsAThirtyOneCharacterNameAndAnInteriorApostrophe()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateSchema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var longest = new string('A', 31);
            var path = Path.Combine(directory, "rows.xlsx");
            DataExporter.WriteExcel(path, new List<string[]> { new[] { "Header" } }, default, longest);
            Assert.Equal(longest, ReadWorkbookSheetName(path));
            Assert.Throws<ArgumentException>(() => DataExporter.WriteExcel(path, new List<string[]> { new[] { "Header" } }, default, longest + "B"));
            Assert.Equal(longest, ReadWorkbookSheetName(path));

            var named = Path.Combine(directory, "amp.xlsx");
            DataExporter.WriteExcel(named, new List<string[]> { new[] { "Header" } }, default, "A&B");
            Assert.Equal("A&B", ReadWorkbookSheetName(named));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExcelCellsReplaceCharactersXmlForbidsAndKeepAmountsAsText()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ERateSchema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rows.xlsx");
            var emoji = "a\U0001F600b";
            DataExporter.WriteExcel(path, new List<string[]>
            {
                new[] { "Text", "Amount" },
                new[] { "a\u0001b\tc\u0000d", "100.50" },
                new[] { "\uD800", "0" },
                new[] { emoji, "200.00" }
            });
            var sheet = ReadSheet(path);
            Assert.Equal("a\uFFFDb\tc\uFFFDd", sheet[1][0]);
            Assert.Equal("100.50", sheet[1][1]);
            Assert.Equal("\uFFFD", sheet[2][0]);
            Assert.Equal("0", sheet[2][1]);
            Assert.Equal(emoji, sheet[3][0]);
            Assert.DoesNotContain(sheet.SelectMany(row => row).SelectMany(cell => cell), character => character is '\u0001' or '\0' or '\uD800');
            var xml = FileXml(path, "xl/worksheets/sheet1.xml");
            Assert.Contains("inlineStr", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("t=\"n\"", xml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    static ExportRow SampleRow() =>
        new()
        {
            Form = "470",
            FundingYear = "2026",
            Ben = "143618",
            Name = "CHULA VISTA ELEM SCHOOL DIST",
            ApplicationNumber = "260007116",
            Status = "Certified",
            CertifiedDate = "2025-12-19",
            Nickname = "470 C2",
            Services = "Internal Connections",
            RequestedAmount = "100.00",
            PreDiscountAmount = "200.00",
            Pdf = "https://publicdata.usac.org/example.pdf",
            Source = UsacSources.Application(FilingForm.Form470, "260007116"),
            Linked = "false",
        };

    static string ReadWorkbookSheetName(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var workbook = archive.GetEntry("xl/workbook.xml")!.Open();
        var xml = XDocument.Load(workbook);
        return xml.Descendants().First(element => element.Name.LocalName == "sheet").Attribute("name")!.Value;
    }

    static string FileXml(string path, string entryName)
    {
        using var archive = ZipFile.OpenRead(path);
        using var entry = archive.GetEntry(entryName)!.Open();
        using var reader = new StreamReader(entry);
        return reader.ReadToEnd();
    }

    static List<string[]> ReadSheet(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(sheet);
        return xml.Descendants().Where(element => element.Name.LocalName == "row").Select(row =>
            row.Elements().Where(element => element.Name.LocalName == "c").Select(cell =>
                cell.Descendants().First(element => element.Name.LocalName == "t").Value).ToArray()).ToList();
    }

    sealed class CancelOnSecondRow : IReadOnlyList<string[]>
    {
        readonly CancellationTokenSource source;
        readonly string[][] rows;

        public CancelOnSecondRow(CancellationTokenSource source, params string[][] rows)
        {
            this.source = source;
            this.rows = rows;
        }

        public int Count => rows.Length;

        public string[] this[int index]
        {
            get
            {
                if (index > 0)
                {
                    source.Cancel();
                }

                return rows[index];
            }
        }

        public IEnumerator<string[]> GetEnumerator()
        {
            for (var i = 0; i < rows.Length; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    sealed record SampleJsonRow(string ApplicationNumber, string Source);

    sealed class CancelingRow
    {
        readonly CancellationTokenSource cancel;
        readonly bool stop;

        public CancelingRow(string applicationNumber, CancellationTokenSource cancel, bool stop)
        {
            ApplicationNumber = applicationNumber;
            this.cancel = cancel;
            this.stop = stop;
        }

        public string ApplicationNumber { get; }

        public string Source
        {
            get
            {
                if (stop)
                {
                    cancel.Cancel();
                }

                return ApplicationNumber;
            }
        }
    }
}
