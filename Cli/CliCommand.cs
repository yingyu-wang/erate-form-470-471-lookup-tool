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

internal sealed record CliOptions(
    FilingForm? Form,
    bool LinkForms,
    string? Ben,
    string? ApplicantName,
    int FundingYear,
    string? ApplicationNumber,
    string? FundingRequestNumber,
    CliFormat Format,
    string? OutputPath);

internal sealed record CliParseResult(CliOptions? Options, bool Help, string? Error);

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
          ERateLookup.Cli --form 470|471|both (--ben <BEN> | --name <applicant>) --fy <year> [options]

        Options:
          --form 470|471|both  Published form to check. both links Form 470 and Form 471
          --ben <digits>       Billed entity number
          --name <text>        Published applicant name (at least 2 characters)
          --fy <year>          Funding year (2016-2100)
          --app <number>       Keep one application number
          --frn <number>       Form 471 funding request number
          --csv                Write CSV, including the source link and 470/471 link columns
          --json               Write JSON instead of a text table
          --output <file>      Write the result to this file
          --help, -h           Show this help

        Search by BEN, by the published applicant name, or by both. --form both
        reads Form 470 and Form 471 for that applicant and year. A name-only
        link is printed only when every row shares one published BEN. A published
        Form 470 does not establish a Form 471.

        Every result row includes its USAC Open Data source link. The lookup is
        read-only: it does not create, change, certify, or submit a filing.
        Results are an observation of published data, not proof that no filing exists.

        Exit codes:
          0  At least one published row matched
          1  Invalid arguments
          2  The lookup succeeded and nothing matched
          3  USAC could not be reached
          4  The lookup failed for another reason
        """;

    internal static CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return Fail("Form, applicant name or BEN, and funding year are required.");

        string? form = null;
        string? ben = null;
        string? name = null;
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

            var token = raw;
            string? value = null;
            var eq = raw.IndexOf('=');
            if (raw.StartsWith("--", StringComparison.Ordinal) && eq > 2)
            {
                token = raw[..eq];
                value = raw[(eq + 1)..];
            }

            switch (token)
            {
                case "--form":
                case "--ben":
                case "--name":
                case "--fy":
                case "--app":
                case "--frn":
                case "--output":
                    if (!seen.Add(token))
                        return Fail($"Duplicate argument: {token}");
                    value ??= TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(value))
                        return Fail($"Missing value for {token}.");
                    if (token == "--form")
                        form = value.Trim();
                    else if (token == "--ben")
                        ben = value.Trim();
                    else if (token == "--name")
                        name = value.Trim();
                    else if (token == "--fy")
                        fy = value.Trim();
                    else if (token == "--app")
                        app = value.Trim();
                    else if (token == "--frn")
                        frn = value.Trim();
                    else
                        output = value.Trim();
                    break;
                case "--csv":
                    if (value is not null)
                        return Fail("--csv does not take a value.");
                    if (!seen.Add(token))
                        return Fail("Duplicate argument: --csv");
                    csv = true;
                    break;
                case "--json":
                    if (value is not null)
                        return Fail("--json does not take a value.");
                    if (!seen.Add(token))
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
        if (form is null || fy is null || (ben is null && name is null))
            return Fail("Form, applicant name or BEN, and funding year are required.");
        if (form is not ("470" or "471" or "both"))
            return Fail("Form must be 470, 471, or both.");
        if (ben is not null && (ben.Length == 0 || !ben.All(char.IsAsciiDigit)))
            return Fail("BEN must contain digits only.");
        if (name is not null)
        {
            if (name.Length < 2)
                return Fail("Applicant name must contain at least 2 characters.");
            if (name.Length > 200)
                return Fail("Applicant name must be 200 characters or fewer.");
            if (name.Any(char.IsControl))
                return Fail("Applicant name cannot contain control characters.");
        }

        if (!int.TryParse(fy, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year < 2016 || year > 2100)
            return Fail("Funding year must be a year from 2016 through 2100.");
        if (frn is not null && form == "both")
            return Fail("--frn applies to Form 471 and cannot be combined with --form both.");
        if (frn is not null && form != "471")
            return Fail("--frn applies to Form 471.");

        var link = form == "both";
        var format = csv ? CliFormat.Csv : json ? CliFormat.Json : CliFormat.Table;
        FilingForm? filing = form switch
        {
            "471" => FilingForm.Form471,
            "470" => FilingForm.Form470,
            _ => null
        };
        return new CliParseResult(new CliOptions(filing, link, ben, name, year, app, frn, format, output), false, null);
    }

    internal static async Task<int> RunAsync(CliOptions options, UsacClient client, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CliRow> rows;
            DateTimeOffset fetched;
            int? form470Count = null;
            int? form471Count = null;
            if (options.FundingRequestNumber is not null)
            {
                var response = await FundingRequestsAsync(client, options, cancellationToken).ConfigureAwait(false);
                fetched = response.FetchedAt;
                rows = response.Data.Select(ToFrnRow).ToList();
            }
            else if (options.LinkForms)
            {
                var form470 = await SearchAsync(client, options, FilingForm.Form470, cancellationToken).ConfigureAwait(false);
                var form471 = await SearchAsync(client, options, FilingForm.Form471, cancellationToken).ConfigureAwait(false);
                fetched = form470.FetchedAt <= form471.FetchedAt ? form470.FetchedAt : form471.FetchedAt;
                var filings470 = ApplyApplicationFilter(options, form470.Data);
                var filings471 = ApplyApplicationFilter(options, form471.Data);
                form470Count = filings470.Count;
                form471Count = filings471.Count;
                rows = filings470.Select(filing => ToFilingRow(FilingForm.Form470, filing))
                    .Concat(filings471.Select(filing => ToFilingRow(FilingForm.Form471, filing)))
                    .ToList();
                EnsureLinkedApplicant(options, rows);
            }
            else
            {
                var response = await SearchAsync(client, options, options.Form!.Value, cancellationToken).ConfigureAwait(false);
                fetched = response.FetchedAt;
                rows = ApplyApplicationFilter(options, response.Data).Select(filing => ToFilingRow(options.Form!.Value, filing)).ToList();
            }

            var body = Render(options, rows, fetched, form470Count, form471Count);
            await WriteBodyAsync(options, body, stdout, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                await stderr.WriteLineAsync(MissingMessage(options)).ConfigureAwait(false);
                return ExitNotFound;
            }

            await stderr.WriteLineAsync(FoundMessage(options, rows.Count, form470Count, form471Count)).ConfigureAwait(false);
            return ExitFound;
        }
        catch (ArgumentException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return ExitBadArgs;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            var prefix = options.LinkForms
                ? "USAC could not be reached, so Form 470 and Form 471 were not linked. "
                : "USAC could not be reached. ";
            await stderr.WriteLineAsync(prefix + ex.Message).ConfigureAwait(false);
            return ExitUsacUnreachable;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            var prefix = options.LinkForms
                ? "Lookup failed, so Form 470 and Form 471 were not linked. No filing determination was made. "
                : "Lookup failed; no filing determination was made. ";
            await stderr.WriteLineAsync(prefix + ex.Message).ConfigureAwait(false);
            return ExitLookupFailed;
        }
    }

    internal static async Task<QueryResult<List<UsacRow>>> FundingRequestsAsync(UsacClient client, CliOptions options, CancellationToken cancellationToken)
    {
        var where = UsacClient.ApplicantClause(FilingForm.Form471, options.Ben, options.ApplicantName, "ben");
        where += " AND funding_year=" + UsacClient.Literal(options.FundingYear.ToString(CultureInfo.InvariantCulture));
        where += " AND form_version='Current'";
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

    static Task<QueryResult<List<Filing>>> SearchAsync(UsacClient client, CliOptions options, FilingForm form, CancellationToken cancellationToken) =>
        client.SearchFilingsAsync(options.Ben, options.ApplicantName, options.FundingYear, form, true, cancellationToken);

    static void EnsureLinkedApplicant(CliOptions options, IReadOnlyList<CliRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        if (options.Ben is not null)
        {
            var other = rows
                .Select(row => row.Ben)
                .Where(ben => !string.IsNullOrWhiteSpace(ben) && !string.Equals(ben, options.Ben, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (other.Count > 0)
            {
                throw new InvalidOperationException(
                    "The published rows include BEN " + string.Join(", ", other) + " in addition to requested BEN " + options.Ben + ". Pass one BEN to link Form 470 and Form 471 for that applicant.");
            }

            return;
        }

        if (rows.Any(row => string.IsNullOrWhiteSpace(row.Ben)))
        {
            throw new InvalidOperationException(
                "A published row did not include a BEN, so Form 470 and Form 471 were not linked as one applicant. Pass --ben to choose one applicant.");
        }

        var bens = rows.Select(row => row.Ben).Distinct(StringComparer.Ordinal).ToList();
        if (bens.Count > 1)
        {
            throw new InvalidOperationException(
                "The published name matches more than one BEN (" + string.Join(", ", bens) + "). Pass --ben to choose one applicant.");
        }
    }

    static List<Filing> ApplyApplicationFilter(CliOptions options, IReadOnlyList<Filing> filings)
    {
        if (options.ApplicationNumber is null)
            return filings.ToList();
        return filings.Where(filing => string.Equals(filing.Number, options.ApplicationNumber, StringComparison.Ordinal)).ToList();
    }

    static string? TakeValue(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith('-'))
            return null;
        index++;
        return args[index];
    }

    static CliParseResult Fail(string message) => new(null, false, message);

    static CliRow ToFilingRow(FilingForm form, Filing filing) => new(
        (int)form,
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
        filing.PreDiscountAmount,
        UsacSources.Application(form, filing.Number));

    static CliRow ToFrnRow(UsacRow row) => new(
        (int)FilingForm.Form471,
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
        row.PreDiscountAmount,
        UsacSources.FundingRequest(row.FundingRequestNumber, row.Number));

    static string Render(CliOptions options, IReadOnlyList<CliRow> rows, DateTimeOffset fetched, int? form470Count, int? form471Count)
    {
        if (options.Format == CliFormat.Json)
        {
            var fetchedAt = fetched.ToString("O", CultureInfo.InvariantCulture);
            object payload = options.LinkForms
                ? new
                {
                    form = (int?)null,
                    linked = true,
                    form470Count,
                    form471Count,
                    ben = options.Ben,
                    applicantName = options.ApplicantName,
                    fundingYear = options.FundingYear,
                    applicationNumber = options.ApplicationNumber,
                    fundingRequestNumber = options.FundingRequestNumber,
                    fetchedAt,
                    count = rows.Count,
                    rows
                }
                : new
                {
                    form = (int)options.Form!,
                    linked = false,
                    ben = options.Ben,
                    applicantName = options.ApplicantName,
                    fundingYear = options.FundingYear,
                    applicationNumber = options.ApplicationNumber,
                    fundingRequestNumber = options.FundingRequestNumber,
                    fetchedAt,
                    count = rows.Count,
                    rows
                };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        var table = new List<string[]>
        {
            new[] { "Form", "FundingYear", "BEN", "Name", "ApplicationNumber", "FundingRequestNumber", "Status", "CertifiedDate", "Nickname", "Services", "RequestedAmount", "PreDiscountAmount", "PDF", "Source", "Linked", "Form470Count", "Form471Count" }
        };
        foreach (var row in TableRows(options, rows, form470Count, form471Count))
            table.Add(row);

        if (options.Format == CliFormat.Csv)
        {
            using var writer = new StringWriter();
            DataExporter.WriteCsv(writer, table);
            return writer.ToString();
        }

        return RenderTable(table);
    }

    static IEnumerable<string[]> TableRows(CliOptions options, IReadOnlyList<CliRow> rows, int? form470Count, int? form471Count)
    {
        var linked = options.LinkForms ? "true" : "false";
        var count470 = form470Count?.ToString(CultureInfo.InvariantCulture) ?? "";
        var count471 = form471Count?.ToString(CultureInfo.InvariantCulture) ?? "";
        foreach (var row in rows)
        {
            yield return
            [
                row.Form.ToString(CultureInfo.InvariantCulture),
                options.FundingYear.ToString(CultureInfo.InvariantCulture),
                DisplayBen(options, row),
                row.Name,
                row.ApplicationNumber,
                row.FundingRequestNumber,
                row.Status,
                row.CertifiedDate,
                row.Nickname,
                row.Services,
                row.RequestedAmount,
                row.PreDiscountAmount,
                row.Pdf,
                row.Source,
                linked,
                count470,
                count471
            ];
        }
    }

    static string DisplayBen(CliOptions options, CliRow row) =>
        !string.IsNullOrEmpty(row.Ben) ? row.Ben : options.Ben ?? "";

    static string RenderTable(List<string[]> table)
    {
        var sourceIndex = Array.IndexOf(table[0], "Source");
        var widths = new int[table[0].Length];
        foreach (var row in table)
        {
            for (var i = 0; i < row.Length; i++)
            {
                var length = i == sourceIndex ? row[i].Length : Math.Min(40, row[i].Length);
                widths[i] = Math.Max(widths[i], length);
            }
        }

        var builder = new StringBuilder();
        foreach (var row in table)
        {
            for (var i = 0; i < row.Length; i++)
            {
                if (i > 0)
                    builder.Append("  ");
                var text = i == sourceIndex || row[i].Length <= 40 ? row[i] : row[i][..37] + "...";
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

    static string FoundMessage(CliOptions options, int count, int? form470Count, int? form471Count)
    {
        if (form470Count is int form470 && form471Count is int form471)
            return $"Found {form470.ToString(CultureInfo.InvariantCulture)} certified Form 470 application(s) and {form471.ToString(CultureInfo.InvariantCulture)} certified Form 471 application(s) for {Who(options)}. A published Form 470 does not establish a Form 471.";
        var what = options.FundingRequestNumber is null ? $"certified Form {(int)options.Form!.Value} application(s)" : "Form 471 funding request(s)";
        return $"Found {count.ToString(CultureInfo.InvariantCulture)} {what} for {Who(options)}.";
    }

    static string MissingMessage(CliOptions options)
    {
        var who = Who(options);
        if (options.LinkForms && options.ApplicationNumber is not null)
            return $"No application {options.ApplicationNumber} found in published Form 470 or Form 471 data for {who}. An empty result is not proof that no filing exists.";
        if (options.LinkForms)
            return $"No certified Form 470 or Form 471 found in currently published data for {who}. An empty result is not proof that no filing exists.";
        if (options.FundingRequestNumber is not null)
            return $"No Form 471 funding request {options.FundingRequestNumber} found in published data for {who}. An empty result is not proof that no filing exists.";
        if (options.ApplicationNumber is not null)
            return $"No application {options.ApplicationNumber} found in published Form {(int)options.Form!.Value} data for {who}. An empty result is not proof that no filing exists.";
        return $"No certified Form {(int)options.Form!.Value} found in currently published data for {who}. An empty result is not proof that no filing exists.";
    }

    static string Who(CliOptions options)
    {
        var year = options.FundingYear.ToString(CultureInfo.InvariantCulture);
        var applicant = options.Ben is not null && options.ApplicantName is not null
            ? $"BEN {options.Ben}, applicant name '{options.ApplicantName}'"
            : options.Ben is not null
                ? $"BEN {options.Ben}"
                : $"applicant name '{options.ApplicantName}'";
        return $"{applicant}, FY {year}";
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

internal sealed record CliRow(
    int Form,
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
    string PreDiscountAmount,
    string Source);
