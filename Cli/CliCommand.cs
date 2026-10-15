using System.Globalization;
using System.Text;
using System.Text.Json;
using ERateLookup.Core;

namespace ERateLookup.Cli;

public enum CliFormat
{
    Table,
    Csv,
    Json
}

public sealed record CliOptions(
    FilingForm Form,
    string Ben,
    int FundingYear,
    string? ApplicationNumber,
    string? FundingRequestNumber,
    CliFormat Format,
    string? OutputPath);

public sealed record CliParseResult(CliOptions? Options, bool Help, string? Error);

public static class CliCommand
{
    public const int ExitFound = 0;
    public const int ExitBadArgs = 1;
    public const int ExitNotFound = 2;
    public const int ExitUsacUnreachable = 3;
    public const int ExitLookupFailed = 4;
    public const string FrnEndpoint = "https://opendata.usac.org/resource/qdmp-ygft.json";

    public const string HelpText = """
        E-rate Form 470 / 471 lookup

        Usage:
          ERateLookup.Cli --form 470|471 --ben <BEN> --fy <year> [options]

        Options:
          --form 470|471     Published form to check
          --ben <digits>     Billed entity number
          --fy <year>        Funding year (2016-2100)
          --app <number>     Keep one application number
          --frn <number>     Form 471 funding request number
          --csv              Write CSV instead of a text table
          --json             Write JSON instead of a text table
          --output <file>    Write the result to this file
          --help, -h         Show this help

        The lookup uses the same USAC queries as the desktop app. --frn reads the
        published Form 471 FRN status dataset. Results are an observation of
        published data, not proof that no filing exists.

        Exit codes:
          0  At least one published row matched
          1  Invalid arguments
          2  The lookup succeeded and nothing matched
          3  USAC could not be reached
          4  The lookup failed for another reason
        """;

    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return Fail("Form, BEN, and funding year are required.");

        string? form = null;
        string? ben = null;
        string? fy = null;
        string? app = null;
        string? frn = null;
        string? output = null;
        var csv = false;
        var json = false;
        var help = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Count; i++)
        {
            var raw = args[i];
            if (raw is "--help" or "-h" or "-?")
            {
                help = true;
                continue;
            }

            var name = raw;
            string? value = null;
            var eq = raw.IndexOf('=');
            if (raw.StartsWith("--", StringComparison.Ordinal) && eq > 2)
            {
                name = raw[..eq];
                value = raw[(eq + 1)..];
            }

            switch (name)
            {
                case "--form":
                case "--ben":
                case "--fy":
                case "--app":
                case "--frn":
                case "--output":
                    if (!seen.Add(name))
                        return Fail($"Duplicate argument: {name}");
                    value ??= TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(value))
                        return Fail($"Missing value for {name}.");
                    if (name == "--form")
                        form = value.Trim();
                    else if (name == "--ben")
                        ben = value.Trim();
                    else if (name == "--fy")
                        fy = value.Trim();
                    else if (name == "--app")
                        app = value.Trim();
                    else if (name == "--frn")
                        frn = value.Trim();
                    else
                        output = value.Trim();
                    break;
                case "--csv":
                    if (value is not null)
                        return Fail("--csv does not take a value.");
                    if (!seen.Add(name))
                        return Fail("Duplicate argument: --csv");
                    csv = true;
                    break;
                case "--json":
                    if (value is not null)
                        return Fail("--json does not take a value.");
                    if (!seen.Add(name))
                        return Fail("Duplicate argument: --json");
                    json = true;
                    break;
                default:
                    return Fail($"Unknown argument: {raw}");
            }
        }

        if (help)
            return new CliParseResult(null, true, null);
        if (csv && json)
            return Fail("Choose either --csv or --json.");
        if (form is null || ben is null || fy is null)
            return Fail("Form, BEN, and funding year are required.");
        if (form is not ("470" or "471"))
            return Fail("Form must be 470 or 471.");
        if (ben.Length == 0 || !ben.All(char.IsAsciiDigit))
            return Fail("BEN must contain digits only.");
        if (!int.TryParse(fy, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year < 2016 || year > 2100)
            return Fail("Funding year must be a year from 2016 through 2100.");
        if (frn is not null && form != "471")
            return Fail("--frn applies to Form 471.");

        var format = csv ? CliFormat.Csv : json ? CliFormat.Json : CliFormat.Table;
        var filing = form == "471" ? FilingForm.Form471 : FilingForm.Form470;
        return new CliParseResult(new CliOptions(filing, ben, year, app, frn, format, output), false, null);
    }

    public static async Task<int> RunAsync(CliOptions options, UsacClient client, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CliRow> rows;
            DateTimeOffset fetched;
            if (options.FundingRequestNumber is not null)
            {
                var response = await FundingRequestsAsync(client, options, cancellationToken).ConfigureAwait(false);
                fetched = response.FetchedAt;
                rows = response.Data.Select(ToFrnRow).ToList();
            }
            else
            {
                var response = await client.GetFilingsAsync(options.Ben, options.FundingYear, options.Form, true, cancellationToken).ConfigureAwait(false);
                fetched = response.FetchedAt;
                var filings = response.Data;
                if (options.ApplicationNumber is not null)
                    filings = filings.Where(filing => string.Equals(filing.Number, options.ApplicationNumber, StringComparison.Ordinal)).ToList();
                rows = filings.Select(ToFilingRow).ToList();
            }

            var body = Render(options, rows, fetched);
            await WriteBodyAsync(options, body, stdout, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                await stderr.WriteLineAsync(MissingMessage(options)).ConfigureAwait(false);
                return ExitNotFound;
            }

            await stderr.WriteLineAsync(FoundMessage(options, rows.Count)).ConfigureAwait(false);
            return ExitFound;
        }
        catch (ArgumentException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return ExitBadArgs;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            await stderr.WriteLineAsync("USAC could not be reached. " + ex.Message).ConfigureAwait(false);
            return ExitUsacUnreachable;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync("Lookup failed; no filing determination was made. " + ex.Message).ConfigureAwait(false);
            return ExitLookupFailed;
        }
    }

    public static async Task<QueryResult<List<UsacRow>>> FundingRequestsAsync(UsacClient client, CliOptions options, CancellationToken cancellationToken)
    {
        _ = UsacClient.FilingFilter(options.Ben, options.FundingYear);
        var where = $"ben={UsacClient.Literal(options.Ben)} AND funding_year={UsacClient.Literal(options.FundingYear.ToString(CultureInfo.InvariantCulture))} AND form_version='Current'";
        if (!string.IsNullOrEmpty(options.ApplicationNumber))
            where += " AND application_number=" + UsacClient.Literal(options.ApplicationNumber);
        if (!string.IsNullOrEmpty(options.FundingRequestNumber))
            where += " AND funding_request_number=" + UsacClient.Literal(options.FundingRequestNumber);
        return await client.QueryAsync(new Dictionary<string, string>
        {
            ["$select"] = "funding_request_number,application_number,ben as billed_entity_number,organization_name as billed_entity_name,form_471_frn_status_name as filing_status,nickname as form_nickname,form_471_service_type_name as service_type,funding_commitment_request as funding_request_amount,form_version",
            ["$where"] = where,
            ["$order"] = "funding_request_number",
            ["$limit"] = "1000"
        }, true, cancellationToken, FrnEndpoint).ConfigureAwait(false);
    }

    static string? TakeValue(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith('-'))
            return null;
        index++;
        return args[index];
    }

    static CliParseResult Fail(string message) => new(null, false, message);

    static CliRow ToFilingRow(Filing filing) => new(
        filing.Number,
        "",
        filing.Status,
        filing.Certified,
        filing.Nickname,
        filing.Ben,
        filing.Name,
        filing.Services,
        filing.Pdf,
        filing.RequestedAmount,
        filing.PreDiscountAmount);

    static CliRow ToFrnRow(UsacRow row) => new(
        row.Number,
        row.FundingRequestNumber,
        string.IsNullOrWhiteSpace(row.Status) ? "Unknown" : row.Status,
        UsacTimestamps.CalendarDate(row.Certified),
        row.Nickname,
        row.Ben,
        row.Name,
        row.Service,
        row.Pdf?.Url ?? row.PdfUrl,
        row.RequestedAmount,
        row.PreDiscountAmount);

    static string Render(CliOptions options, IReadOnlyList<CliRow> rows, DateTimeOffset fetched)
    {
        if (options.Format == CliFormat.Json)
        {
            var payload = new
            {
                form = (int)options.Form,
                ben = options.Ben,
                fundingYear = options.FundingYear,
                applicationNumber = options.ApplicationNumber,
                fundingRequestNumber = options.FundingRequestNumber,
                fetchedAt = fetched.ToString("O", CultureInfo.InvariantCulture),
                count = rows.Count,
                rows
            };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        var table = new List<string[]>
        {
            new[] { "Form", "FundingYear", "BEN", "Name", "ApplicationNumber", "FundingRequestNumber", "Status", "CertifiedDate", "Nickname", "Services", "RequestedAmount", "PreDiscountAmount", "PDF" }
        };
        foreach (var row in rows)
        {
            table.Add(
            [
                ((int)options.Form).ToString(CultureInfo.InvariantCulture),
                options.FundingYear.ToString(CultureInfo.InvariantCulture),
                options.Ben,
                row.Name,
                row.ApplicationNumber,
                row.FundingRequestNumber,
                row.Status,
                row.CertifiedDate,
                row.Nickname,
                row.Services,
                row.RequestedAmount,
                row.PreDiscountAmount,
                row.Pdf
            ]);
        }

        if (options.Format == CliFormat.Csv)
        {
            using var writer = new StringWriter();
            DataExporter.WriteCsv(writer, table);
            return writer.ToString();
        }

        return RenderTable(table);
    }

    static string RenderTable(List<string[]> table)
    {
        var widths = new int[table[0].Length];
        foreach (var row in table)
        {
            for (var i = 0; i < row.Length; i++)
                widths[i] = Math.Max(widths[i], Math.Min(40, row[i].Length));
        }

        var builder = new StringBuilder();
        foreach (var row in table)
        {
            for (var i = 0; i < row.Length; i++)
            {
                if (i > 0)
                    builder.Append("  ");
                var text = row[i].Length > 40 ? row[i][..37] + "..." : row[i];
                builder.Append(text.PadRight(widths[i]));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    static async Task WriteBodyAsync(CliOptions options, string body, TextWriter stdout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(options.OutputPath))
        {
            await stdout.WriteAsync(body).ConfigureAwait(false);
            if (!body.EndsWith('\n'))
                await stdout.WriteLineAsync().ConfigureAwait(false);
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(options.OutputPath, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: options.Format == CliFormat.Csv), cancellationToken).ConfigureAwait(false);
    }

    static string FoundMessage(CliOptions options, int count)
    {
        var what = options.FundingRequestNumber is null ? $"certified Form {(int)options.Form} application(s)" : "Form 471 funding request(s)";
        return $"Found {count.ToString(CultureInfo.InvariantCulture)} {what} for BEN {options.Ben}, FY {options.FundingYear.ToString(CultureInfo.InvariantCulture)}.";
    }

    static string MissingMessage(CliOptions options)
    {
        if (options.FundingRequestNumber is not null)
            return $"No Form 471 funding request {options.FundingRequestNumber} found in published data for BEN {options.Ben}, FY {options.FundingYear.ToString(CultureInfo.InvariantCulture)}.";
        if (options.ApplicationNumber is not null)
            return $"No application {options.ApplicationNumber} found in published Form {(int)options.Form} data for BEN {options.Ben}, FY {options.FundingYear.ToString(CultureInfo.InvariantCulture)}.";
        return $"No certified Form {(int)options.Form} found in currently published data for BEN {options.Ben}, FY {options.FundingYear.ToString(CultureInfo.InvariantCulture)}.";
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed record CliRow(
    string ApplicationNumber,
    string FundingRequestNumber,
    string Status,
    string CertifiedDate,
    string Nickname,
    string Ben,
    string Name,
    string Services,
    string Pdf,
    string RequestedAmount,
    string PreDiscountAmount);
