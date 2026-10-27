# Command-line lookup — 1.0.5

`ERateLookup.Cli` looks up published E-rate Form 470 and Form 471 rows from a
terminal. It is a .NET 10 console program in `Cli/`. It does not open the
Windows Forms, Avalonia, or Android screens. The lookup is read-only. It does
not create, change, certify, or submit a filing.

## Unreleased 1.0.6: committed and disbursed amounts

`--frn` reads the public FRN Status dataset `qdmp-ygft`. It does not sign in
to EPC. The table, CSV, and JSON gain five columns. Excel includes those five
columns only when `--frn` is used. Application lookups leave them blank.
Desktop, Mac, Linux, and Android exports do not include these five columns.

| Column | Published field | Rule |
| --- | --- | --- |
| `FrnStatus` | `form_471_frn_status_name` | The published status text. On 2026-10-19 the Current rows used `Funded`, `Pending`, `Denied`, and `Cancelled`. |
| `CommittedAmount` | `funding_commitment_request` | **[Inference]** Copied only when `FrnStatus` is Funded. Blank for every other status. Q8 is pending: this has not been confirmed against an FCDL, including when `revised_fcdl_date` is set. |
| `FcdlDate` | `fcdl_letter_date` | Calendar date of the wave. The dataset calls this field Wave Date. |
| `RevisedFcdlDate` | `revised_fcdl_date` | Calendar date when USAC published one. Blank when the field is missing. |
| `DisbursedAmount` | `total_authorized_disbursement` | Published total net amount disbursed. Blank when the field is missing. A missing value is not written as zero. |

`RequestedAmount` on an FRN row is still the same `funding_commitment_request`,
for every status. The select aliases it as `funding_request_amount`. That
column is not the committed-amount rule.

The query still requires `form_version='Current'`.

Checked 2026-10-19 against
[FRN 2099010373](https://opendata.usac.org/resource/qdmp-ygft.json?funding_request_number=2099010373&$select=funding_request_number,application_number,ben,funding_year,organization_name,form_471_frn_status_name,funding_commitment_request,fcdl_letter_date,revised_fcdl_date,total_authorized_disbursement,form_version,pending_reason)
and the [dataset metadata](https://opendata.usac.org/api/views/qdmp-ygft.json).
The Current row was Webster Groves School District, BEN 136910, FY 2020,
application 201009092, status `Funded`, `funding_commitment_request` `14908.32`,
`fcdl_letter_date` `2020-05-09T00:00:00.000`, `revised_fcdl_date`
`2026-07-14T00:00:00.000`, `total_authorized_disbursement` `14908.32`,
`pending_reason` `FCDL Issued`. An Original row for the same FRN was `Pending`
with the same `funding_commitment_request` and no FCDL or disbursement fields
in that response. The CLI does not return the Original row.
Field descriptions used above are from that metadata page:
`funding_commitment_request` is "The total amount for the Funding Request (FRN)
after discount." `fcdl_letter_date` is "The date of the wave that a funding
commitment was issued in." `revised_fcdl_date` is "Date the applicant FCDL was
revised." `total_authorized_disbursement` is "Total net amount disbursed to a
service provider or applicant."

## What changed in 1.0.5

Version 1.0.5 adds four lookup features. It does not add a Category 2 budget
helper, EPC authentication, Form 470 version diffing, or committed-amount
downloads. Unreleased 1.0.6 adds `--diff` for Form 470 Original vs Current.
That command is described below. It does not add local snapshot history.

| Feature | Behavior |
| --- | --- |
| Applicant search | `--ben`, `--name`, or both. `--name` matches the published USAC applicant name and needs at least 2 characters. |
| Source link | Every table, CSV, and JSON row includes a USAC Open Data URL. The table does not shorten that URL. |
| JSON and CSV | The published 1.0.4 CLI already writes JSON with `--json` and CSV with `--csv`. Version 1.0.5 keeps both. CSV columns include `Source`, `Linked`, `Form470Count`, and `Form471Count`. |
| Link 470 to 471 | `--form both` reads both forms for the same applicant and funding year. A name-only link is printed only when every row shares one published BEN. Each row keeps its own form and source. A Form 470 does not establish a Form 471. |

San Diego Unified School District is the example behind the name search. The
RFP title uses "San Diego Unified." The bundled California directory publishes
that district as **San Diego City Unif Sch Dist**, BEN **143662**. Searching
`--name "San Diego City"` matches the published name. The tool does not invent
a BEN from an RFP title.

```sh
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --name "San Diego City" --fy 2027
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form both --ben 143662 --fy 2027 --json
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form both --ben 143662 --fy 2027 --csv --output sdusd.csv
```

An empty result is not proof that no filing exists. If USAC cannot be reached
during `--form both`, the command exits 3 and does not print a partial link.

Release files are unsigned by design. v1.0.5 is not tagged.

## Excel export

`--xlsx` (alias `--excel`) writes a workbook through `DataExporter.WriteExcel`.
The lookup sheet is named `E-rate lookup`. Without `--frn`, the columns are
the 17 base columns through `Form471Count`. With `--frn`, that sheet appends
`FrnStatus`, `CommittedAmount`, `FcdlDate`, `RevisedFcdlDate`, and
`DisbursedAmount`, in that order, matching the table, CSV, and JSON. Version
1.0.6 wrote only the 17 base columns and deferred the five FRN columns. The
flag requires `--output`. The workbook is not written to the terminal.
`--csv`, `--json`, and `--xlsx` together exit 1.

`--diff` can write Excel. The sheet is named `Form 470 diff` and uses the same
columns as the diff CSV. Version 1.0.6 rejected `--diff` with `--xlsx`.

Every workbook cell is an inline string, including `RequestedAmount`,
`PreDiscountAmount`, `CommittedAmount`, and `DisbursedAmount`. The file does
not use numeric cells. CSV prefixes a field that starts with `=`, `+`, `-`,
`@`, tab, or carriage return with `'`. The Excel cell stores the original text
without that prefix. The worksheet stores no formula element. The header row is
frozen, and an autofilter covers the rows that were written.

Release files are unsigned by design. This follow-on does not change the
version number. v1.0.6 is not tagged.

This page describes the command-line lookup added in pull request
[#14](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/pull/14)
(merge `155477d`). The release version is 1.0.4. Pull request
[#15](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/pull/15)
merged as `0c29de3` and set `<Version>` to 1.0.4. There is no `--version`
flag. `--version` exits 1 with `Unknown argument: --version`.

Starting with v1.0.4, the CLI ships as Windows, macOS, and Linux release
assets. v1.0.4 is being rebuilt so those assets are included. There is no
Android CLI. Building from source remains an alternative.

Checked on 2026-10-15 against `Cli/CliCommand.cs`, `Cli/Program.cs`, and
`dotnet run --project Cli/ERateLookup.Cli.csproj -- --help` (SDK 10.0.402,
Linux x64). A framework-dependent publish of that project printed the same
help text.

## What changed from v1.0.3

`v1.0.3` is commit `0d60ef3`. Compared with `main` (`155477d`), the only
product change is the command-line lookup:

| Path | Change |
| --- | --- |
| `Cli/` | New `ERateLookup.Cli` project (`net10.0`, framework-dependent exe) |
| `Cli.Tests/` | Offline tests for parsing, output, FRN queries, empty results, and HTTP failure |
| `.github/workflows/cli.yml` | Tests and publishes the CLI on Ubuntu, Windows, and macOS |
| `Core/UsacClient.cs` | `UsacRow` can read `funding_request_number` |
| `Core/DataExporter.cs` | CSV writing is shared through `WriteCsv` for a `TextWriter` |
| `ERateLookup.sln` | Includes the CLI and CLI test projects |
| `README.md` | Short command-line section |

The desktop, Mac, Linux, and Android packages are unchanged by that diff.
A separate change adds the Windows, macOS, and Linux CLI to the release
files, and v1.0.4 is rebuilt to include them.

## Install and run

Starting with v1.0.4, the [v1.0.4 release](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.4)
includes the CLI for Windows, macOS, and Linux. That release is being rebuilt
to add those assets. The [v1.0.3 release](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.3)
does not include the CLI. There is no Android command-line package.

The program is framework-dependent. It needs the .NET 10 runtime
(`Microsoft.NETCore.App`), not the Windows Desktop Runtime the GUI uses. Keep
every file from the CLI download together: the host, `ERateLookup.Cli.dll`,
the runtime config, `Core.dll`, and the SQLite files under `runtimes/`.

| System | Host in the release asset |
| --- | --- |
| Windows | `ERateLookup.Cli.exe` |
| macOS | `ERateLookup.Cli` |
| Linux | `ERateLookup.Cli` |

### Build from source

Build from a git checkout with the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
when you are not using a release asset. The same commands work on Windows,
macOS, and Linux. Run them from the repository root.

Show help (stdout, exit 0):

```sh
dotnet run --project Cli/ERateLookup.Cli.csproj -- --help
```

Publish a framework-dependent build. This is the command in
`.github/workflows/cli.yml`. It does not pass a runtime identifier, so publish
on the operating system where you will run the program.

```sh
dotnet publish Cli/ERateLookup.Cli.csproj -c Release --self-contained false -o publish/cli
```

Keep the whole `publish/cli` directory together. The host needs
`ERateLookup.Cli.dll`, `ERateLookup.Cli.runtimeconfig.json`,
`ERateLookup.Cli.deps.json`, `Core.dll`, and the SQLite files under
`runtimes/`. The published runtime config asks for `Microsoft.NETCore.App`
10.0.0. That is the shared .NET runtime, not the Windows Desktop Runtime the
GUI uses.

| System | Run the published host |
| --- | --- |
| Windows | `publish\cli\ERateLookup.Cli.exe --help` |
| macOS | `publish/cli/ERateLookup.Cli --help` |
| Linux | `publish/cli/ERateLookup.Cli --help` |

The Linux x64 publish from this documentation check produced `ERateLookup.Cli`
with no `.exe` suffix. Windows adds `.exe` because the project
`AssemblyName` is `ERateLookup.Cli` and `OutputType` is `Exe`. macOS uses the
same project file; this check did not publish on macOS or Windows.

If the .NET 10 runtime is not installed where the app host can find it, the
host exits before the lookup runs. That failure is not one of the exit codes
in the table below. On this Linux check, a publish launched without the
runtime on the host search path exited 131 and printed `You must install .NET
to run this application.` `dotnet run` uses the SDK and does not depend on
that host search.

GitHub Actions uploads `ERateLookup-Cli-ubuntu-latest`,
`ERateLookup-Cli-windows-latest`, and `ERateLookup-Cli-macos-latest` from
`.github/workflows/cli.yml`. Those artifacts are kept for 7 days. They are not
release archives.

There is no Android CLI project and no Android job in `cli.yml`.

The program stores a SQLite cache under `ERateLookup-cli` in the system temp
directory (`Path.GetTempPath()`). Each lookup asks `UsacClient` to refresh, so
a run still contacts USAC. The HTTP client timeout in `Cli/Program.cs` is 60
seconds.

## Flags

Help text, verified by running `--help`:

```text
ERateLookup.Cli --form 470|471|both (--ben <BEN> | --name <applicant>) --fy <year> [options]

  --form 470|471|both  Published form to check. both links Form 470 and Form 471
  --ben <digits>       Billed entity number
  --name <text>        Published applicant name (at least 2 characters)
  --fy <year>          Funding year (2016-2100)
  --app <number>       Keep one application number
  --frn <number>       Form 471 funding request number
  --diff               Compare published Form 470 Original and Current rows
  --csv                Write CSV, including the source link and 470/471 link columns
  --json               Write JSON instead of a text table
  --xlsx, --excel      Write Excel. With --frn, include the five FRN columns. Requires --output
  --output <file>      Write the result to this file
  --help, -h           Show this help
```

The parser also accepts `-?` as help. The printed help does not list `-?`.

| Flag | Required | Rule in `CliCommand.Parse` |
| --- | --- | --- |
| `--form` | Yes | `470`, `471`, or `both`. `both` reads Form 470 and Form 471 for the same applicant and year. |
| `--ben` | One of `--ben` or `--name` | Digits only, after trim. May be combined with `--name`. |
| `--name` | One of `--ben` or `--name` | Published applicant name, 2 to 200 characters, no control characters. Matched with `contains(upper(...))` on the USAC name column. |
| `--fy` | Yes | Integer year from 2016 through 2100 |
| `--app` | No | One application number. Without `--frn`, rows from the filing query are kept only when the application number matches. With `--frn`, it is added to the FRN query. |
| `--frn` | No | Form 471 only. `--form 470 --frn ...` exits 1: `--frn applies to Form 471.` `--form both --frn ...` exits 1 because a funding-request filter is not a 470-to-471 link. |
| `--diff` | No | Form 470 only. `--form 471 --diff` and `--form both --diff` exit 1: `--diff applies to Form 470.` With `--xlsx` or `--excel`, writes one sheet named `Form 470 diff`. Takes no value. |
| `--csv` | No | CSV body. Takes no value. |
| `--json` | No | JSON body. Takes no value. |
| `--xlsx`, `--excel` | No | Excel workbook. Without `--frn`, the 17 base columns. With `--frn`, those columns plus the five FRN columns, in CSV order. Takes no value. `--excel` is an alias of `--xlsx`. Requires `--output`. |
| `--output` | With `--xlsx` | Writes the body to this path. Parent directories are created. Optional for the table, CSV, and JSON. The program does not check that the Excel path ends in `.xlsx`. |
| `--help`, `-h`, `-?` | No | Prints help on stdout and exits 0. Does not run a lookup. |

Valued options accept a separate argument (`--form 470`) or an equals form
(`--form=470`). Repeating a flag is an error (`Duplicate argument: ...`).
`--csv`, `--json`, and `--xlsx` together exit 1: `Choose either --csv, --json, or --xlsx.`
`--xlsx` or `--excel` without `--output` exits 1: `--xlsx requires --output <file.xlsx>.`
`--diff` with `--xlsx` or `--excel` writes the diff workbook. It still requires `--output`.
Omitting `--form`, both `--ben` and `--name`, or `--fy` exits 1: `Form, applicant name or BEN, and funding year
are required.` Unknown text exits 1: `Unknown argument: ...`. Parse errors go
to stderr, followed by the help text.

## Sample commands

Form 470 for one BEN and funding year. Default output is a text table on
stdout. A one-line count goes to stderr.

```sh
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --ben 143618 --fy 2026
```

Form 471 for one funding request number. `--ben` and `--fy` are still
required. BEN 143618 and FRN `2699003124` are the values used in
`Cli.Tests` and the README examples.

```sh
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 471 --ben 143618 --fy 2026 --frn 2699003124
```

Other lookups the flags support:

```sh
# Form 471 applications for a BEN and year (no FRN)
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 471 --ben 143618 --fy 2026

# One application number
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --ben 143618 --fy 2026 --app 260007116

# CSV file
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --ben 143618 --fy 2026 --csv --output chula.csv

# JSON on stdout
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 471 --ben 143618 --fy 2026 --frn 2699003124 --json

# Excel workbook. --xlsx requires --output. --excel is the same flag.
# Without --frn the workbook is the 17 base columns.
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --ben 143618 --fy 2026 --xlsx --output chula.xlsx

# FRN workbook. The five FRN columns follow the 17 base columns.
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 471 --ben 143618 --fy 2026 --frn 2699003124 --xlsx --output frn.xlsx

# Form 470 diff workbook. One sheet, same columns as the diff CSV.
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --ben 16057656 --fy 2027 --app 270000266 --diff --xlsx --output diff.xlsx

# Same FRN, also constrained to an application number
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 471 --ben 143618 --fy 2026 --app 261003372 --frn 2699003124
```

After publish, replace the `dotnet run ... --` prefix with `ERateLookup.Cli`
(or `ERateLookup.Cli.exe` on Windows) and pass the same flags.

A published FY2027 Form 470 for BEN 143618 was not in USAC open data when the
CLI was added. That command exits 2 until USAC publishes a matching row. The
CLI still accepts `--fy 2027`; 2027 is inside 2016–2100. This documentation
pass did not repeat the live USAC query.

## Output formats

The body is the table, CSV, JSON, or Excel workbook. Status text goes to
stderr and is not part of the body. With no `--output`, the table, CSV, or
JSON body is written to stdout, and a final newline is added when the body
does not already end with one. With `--output`, that body is written only to
the file. Stdout stays empty. `--xlsx` always uses `--output` and does not
write a body to stdout. An empty match still writes the body (for Excel, a
workbook whose only row is the header), then exits 2.

### Table (default)

Columns, in order:

`Form`, `FundingYear`, `BEN`, `Name`, `ApplicationNumber`,
`FundingRequestNumber`, `Status`, `CertifiedDate`, `Nickname`, `Services`,
`RequestedAmount`, `PreDiscountAmount`, `PDF`, `Source`, `Linked`,
`Form470Count`, `Form471Count`, `FrnStatus`, `CommittedAmount`, `FcdlDate`,
`RevisedFcdlDate`, `DisbursedAmount`

`--xlsx` writes the 17 columns through `Form471Count` when `--frn` is omitted.
With `--frn`, it also writes `FrnStatus`, `CommittedAmount`, `FcdlDate`,
`RevisedFcdlDate`, and `DisbursedAmount` in that order. The table and CSV
always include those five columns. Application rows leave them blank.

`Linked` is `true` when the command used `--form both` and `false` for a
single-form lookup. `Form470Count` and `Form471Count` are filled only for
`--form both`, including a zero when that form was checked and had no published
rows. A single-form lookup leaves those two counts blank; a blank count means
that form was not queried.

`Source` is the USAC Open Data URL for that row. Form 470 rows use dataset
`jt8s-3q52`. Form 471 application rows use `9s6i-myen`. Funding-request rows
use `qdmp-ygft`. The table prints the full source URL. Other cells longer
than 40 characters are cut to 37 characters plus `...` in the table only.
CSV, JSON, and Excel keep the full text, including the source URL.

### `--csv`

CSV goes through `DataExporter.WriteCsv`. Fields are quoted. A field that
starts with `=`, `+`, `-`, `@`, tab, or carriage return is prefixed with `'`.
A CSV file is UTF-8 with a BOM. CSV on stdout has no BOM. An empty match is
the header row only.

### `--json`

Indented JSON. Names are camelCase. Top-level fields: `form` (470, 471, or
`null` when `--form both`), `linked`, `ben`, `applicantName`, `fundingYear`,
`applicationNumber`, `fundingRequestNumber`, `fetchedAt`, `count`, `rows`.
`--form both` also includes `form470Count` and `form471Count`. Each row has
`form`, `applicationNumber`, `fundingRequestNumber`, `status`,
`certifiedDate`, `nickname`, `ben`, `name`, `services`, `pdf`,
`requestedAmount`, `preDiscountAmount`, `source`, `frnStatus`,
`committedAmount`, `fcdlDate`, `revisedFcdlDate`, and `disbursedAmount`.
Omitted `--app` or `--frn` is JSON `null` on the top-level fields. Filing
rows leave `fundingRequestNumber` as an empty string and leave the five FRN
Status fields as empty strings. `source` is the Open Data URL.
`committedAmount` is an inference: see the unreleased 1.0.6 section. Q8 is
pending.

The Windows, macOS, Linux, and Android lookup screens write this same envelope.
Their JSON rows use the fields through `source` and omit `frnStatus`,
`committedAmount`, `fcdlDate`, `revisedFcdlDate`, and `disbursedAmount`.
Their CSV and Excel files use the 17 base columns through `Form471Count` and
do not include the five FRN Status columns. An app lookup sets `linked` to
false and omits `form470Count` and `form471Count`, because those screens look
up one form at a time. In an app file, `applicantName` is the customer text on
the screen, or `null` when that box is empty. The command-line `applicantName`
remains the `--name` argument.
A portfolio JSON row adds `lookupOutcome`, `change`, `sourceAndNotes`, and
`allowableContractDate` after `source`. The command-line program does not
write those four fields.

### `--xlsx`

`--xlsx` and `--excel` call `DataExporter.WriteExcel`. The lookup sheet is
named `E-rate lookup`. Without `--frn`, the columns are
`ExportSchema.BaseColumns` (the call is `ExportSchema.ExcelColumns(false)`).
With `--frn`, the columns are `ExportSchema.CliColumns`
(`ExportSchema.ExcelColumns(true)`): the 17 base columns plus `FrnStatus`,
`CommittedAmount`, `FcdlDate`, `RevisedFcdlDate`, and `DisbursedAmount`, in
the same order as the table and CSV. The file is an `.xlsx` zip. It is not
UTF-8 text and it has no BOM. `--diff` uses the sheet described under Form 470
Original vs Current. Passing either flag without `--output` exits 1 before the
lookup. Repeating `--xlsx` exits 1 with `Duplicate argument: --xlsx`. Repeating
`--excel` exits 1 with `Duplicate argument: --excel`. Passing both exits 1 with
`Duplicate argument:` and the second flag.

Each cell is an inline string (`t="inlineStr"`). Amounts stay text, such as
`100.50` or `14908.32`, rather than numeric cells. A value CSV would prefix
with `'` is stored without that prefix. The sheet has a frozen header
(`ySplit="1"`, `topLeftCell="A2"`, `state="frozen"`) and an autofilter from
`A1` through the last written cell.

### What a row contains

Without `--frn`, the CLI calls `UsacClient.SearchFilingsAsync`. A BEN-only
lookup goes through `GetFilingsAsync`. Both page in `LoadFilingsAsync`. Form
470 reads dataset `jt8s-3q52` and keeps certified rows. Form 471 reads dataset
`9s6i-myen` and keeps rows with a certified timestamp. Rows are collapsed to
the current version, one application per row.

With `--frn`, the CLI calls the FRN status dataset `qdmp-ygft` instead. The
select list is `UsacClient.FrnStatusSelect`: funding request number,
application number, BEN, organization name, FRN status, nickname, service
type, `funding_commitment_request` (also aliased as `funding_request_amount`),
`fcdl_letter_date`, `revised_fcdl_date`, `total_authorized_disbursement`, and
`form_version`. It does not request a certified date, a pre-discount amount,
or a PDF, so those columns are empty on FRN rows. The query also requires
`form_version='Current'`. `FrnStatus`, `FcdlDate`, `RevisedFcdlDate`, and
`DisbursedAmount` are the published fields. `CommittedAmount` is an inference
and is blank unless the status is Funded. Q8 is pending.

Stderr on success:

```text
Found 1 certified Form 470 application(s) for BEN 143618, FY 2026.
Found 1 Form 471 funding request(s) for BEN 143618, FY 2026.
```

Stderr when nothing matches:

```text
No certified Form 470 found in currently published data for BEN 143618, FY 2027. An empty result is not proof that no filing exists.
No application 999 found in published Form 470 data for BEN 143618, FY 2026. An empty result is not proof that no filing exists.
No Form 471 funding request 2699003124 found in published data for BEN 143618, FY 2026. An empty result is not proof that no filing exists.
No certified Form 470 or Form 471 found in currently published data for BEN 143662, FY 2027. An empty result is not proof that no filing exists.
```

The count and the form number follow the query. An empty result is not proof
that no filing exists. The help text says the same thing.

## Form 470 Original vs Current (`--diff`)

`--diff` compares the published Original and Current `form_version` rows for a
Form 470 in USAC Open Data dataset `jt8s-3q52`. It is a separate read from the
normal lookup. `UsacClient.Collapse` still keeps the Current version for an
ordinary Form 470 or Form 471 lookup. `--diff` writes a table, CSV, JSON, or
Excel. `--xlsx` and `--excel` require `--output`.

```sh
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --ben 16057656 --fy 2027 --app 270000266 --diff
```

The version-row read uses the same `count(*)` pre-check, 50,000-row cap, and
`rowsUpdatedAt` check as `LoadFilingsAsync`. Select, order, and the comparison
are unchanged. A complete result of exactly 50,000 rows is returned. A count
above 50,000 fails before any page is read.

The query does not add `fcc_form_470_status='Certified'`. A Current row can be
canceled while the Original row remains certified. Checked on 2026-10-19,
application `270000194` had Original status `Certified` and Current status
`Canceled`:

https://opendata.usac.org/resource/jt8s-3q52.json?$select=application_number,form_version,fcc_form_470_status,service_request_id&$where=application_number=%27270000194%27&$order=form_version

Service lines are matched on `service_request_id`. An added or removed service
is one row. A service present on both versions lists each changed field,
including category, service type, function, and RFP attachment. Application
fields include contacts and the published dates. Rows for one service request
repeat once per RFP document; the diff compares the set of document URLs.

When only one of Original or Current is present, the result is a `VersionGap`
for `form_version`. Those service lines are not reported as added or removed.
On 2026-10-19, application `270000594` had Current rows and no Original rows.
Application `270000266` had both versions.

The command does not store a local history of intermediate revisions. Open
Data publishes Original and Current, not every edit between them. It does not
determine whether a change was substantial.

Table, CSV, JSON, and Excel use these columns: `ApplicationNumber`, `BEN`,
`Name`, `Change`, `Scope`, `ServiceRequestId`, `Field`, `Original`, `Current`,
`Source`. `Change` is `Added`, `Removed`, `Changed`, `VersionGap`, or
`Unexpected`. JSON adds `diff: true`, `applicationCount`, and `count`. `count`
is the number of difference rows. An empty difference list with
`applicationCount` greater than zero means the published versions match. That
command exits 0. No published Original or Current row exits 2. The body is
still written. Stderr includes: "This comparison reads published Original and
Current rows from USAC Open Data dataset jt8s-3q52. It does not determine
whether a change was substantial."

Excel is one worksheet named `Form 470 diff`
(`DataExporter.Form470DiffSheetName`). It uses the same columns and row order
as the CSV. It does not add a second sheet for `fetchedAt`, `applicationCount`,
`count`, or the query envelope. Those stay in the JSON body. Stderr still
reports whether the versions match. Cells are inline strings, with the same
frozen header and autofilter as a lookup workbook. A value CSV would prefix
with `'` is stored as the original text. An empty difference list still writes
the header row.

`--csv`, `--json`, and `--xlsx` still cannot be combined. CSV formula-like text
is prefixed with `'`. The source link is the Open Data URL for that application
number on `jt8s-3q52`.

## Exit codes

Constants in `CliCommand`: `ExitFound`, `ExitBadArgs`, `ExitNotFound`,
`ExitUsacUnreachable`, `ExitLookupFailed`.

| Code | Name in code | When |
| --- | --- | --- |
| 0 | Found | At least one row matched. `--help`, `-h`, and `-?` also exit 0. `--diff` also exits 0 when published Original and Current rows match. |
| 1 | Bad arguments | The parser rejected the command (message and help on stderr). An `ArgumentException` during the lookup also exits 1, with the exception message only. |
| 2 | No match | USAC answered and the row list is empty. The body is still written. |
| 3 | USAC unreachable | `HttpRequestException`, `TaskCanceledException`, or `JsonException`. Stderr starts with `USAC could not be reached.` |
| 4 | Other lookup failure | `InvalidOperationException`, `IOException`, or `UnauthorizedAccessException`. Stderr starts with `Lookup failed; no filing determination was made.` |

Exit 4 covers a Form 471 identity mismatch (`Form471IdentityMismatchException`),
the filing-query result limit below, and a file write that throws one of those
exceptions. The FRN query does not run the Form 471 identity check. Only
`LoadFilingsAsync` does, and only for Form 471. `GetFilingsAsync` and
`SearchFilingsAsync` both reach that check.

Any other exception is not mapped to these codes.

## Limits

- The `--frn` request sets `$limit` to `1000` and does not ask for another
  page. It does not exit 4 when that page is full. The filter also includes
  the FRN number, BEN, funding year, and `form_version='Current'`, so a single
  FRN is normally one row.
- A Form 470 or Form 471 lookup without `--frn` pages 1,000 rows at a time in
  `LoadFilingsAsync`. Before the first page it reads `count(*)`. A name search
  with more than 50,000 matching rows throws `InvalidOperationException`
  (`N rows match; narrow the name or pass --ben`) and the CLI exits 4. A
  BEN lookup over that cap throws `Result limit reached; a complete lookup
  could not be made.` and also exits 4. The lookup does not return a partial
  page set.
- `LoadFilingsAsync` reads `rowsUpdatedAt` from `/api/views/<id>.json` before
  the first page and after the last. If that value changes, it throws
  `InvalidOperationException` and the CLI exits 4. Pages read for that
  attempt are removed from the local cache, including when the second
  metadata read fails, so a later lookup does not reuse a mixed set.
- `--diff` pages in `GetForm470VersionRowsAsync` with those same guards on
  dataset `jt8s-3q52`. The cap stays 50,000 rows. A name search over the cap
  uses the narrow-name message. A BEN lookup over the cap uses the result-limit
  message. Both exit 4 and do not return a partial page set. The diff columns
  and comparison are unchanged.
- `--fy` accepts 2016 through 2100. A year USAC has not published, including
  FY2027 for some BENs, is an empty published result (exit 2), not a bad
  argument.
- `--name` searches the published applicant name for the selected form and funding year. It does not search the local California directory and it does not translate an RFP title into a BEN.
- `--form both` cannot be combined with `--frn`.
- A name-only `--form both` lookup exits 4 and prints no rows when the published rows do not share one BEN. Pass `--ben` to choose the applicant.
- An FRN row whose funding-request number is blank keeps its source link on dataset `qdmp-ygft`. The link uses the application number when that number is present.
- The CLI has no watchlist, budget, or theme flags. A Category 2 budget helper is not part of 1.0.5.
