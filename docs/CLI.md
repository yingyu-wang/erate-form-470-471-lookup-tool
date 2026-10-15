# Command-line lookup — 1.0.3.1

`ERateLookup.Cli` looks up published E-rate Form 470 and Form 471 rows from a
terminal. It is a .NET 10 console program in `Cli/`. It does not open the
Windows Forms, Avalonia, or Android screens.

This page describes the program on `main` after pull request
[#14](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/pull/14)
(merge `155477d`). That tree is the same as lightweight tag `1.0.3.1`
(`a58799c`). There is no GitHub Release for `1.0.3.1`.
`Cli/ERateLookup.Cli.csproj` still has `<Version>1.0.3</Version>`. There is no
`--version` flag. `--version` exits 1 with `Unknown argument: --version`.

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
Release packaging in `.github/workflows/windows.yml` does not build or attach
`ERateLookup.Cli`.

## Install and run

The [v1.0.3 release](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.3)
assets are the Windows, macOS, and Linux desktop archives, the Android preview
ZIP, and `SHA256SUMS.txt`. None of them contains this CLI. Do not look for
`ERateLookup.Cli` inside those downloads.

Build it from a git checkout with the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
The same commands work on Windows, macOS, and Linux. Run them from the
repository root.

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
ERateLookup.Cli --form 470|471 --ben <BEN> --fy <year> [options]

  --form 470|471     Published form to check
  --ben <digits>     Billed entity number
  --fy <year>        Funding year (2016-2100)
  --app <number>     Keep one application number
  --frn <number>     Form 471 funding request number
  --csv              Write CSV instead of a text table
  --json             Write JSON instead of a text table
  --output <file>    Write the result to this file
  --help, -h         Show this help
```

The parser also accepts `-?` as help. The printed help does not list `-?`.

| Flag | Required | Rule in `CliCommand.Parse` |
| --- | --- | --- |
| `--form` | Yes | `470` or `471` |
| `--ben` | Yes | Digits only, after trim |
| `--fy` | Yes | Integer year from 2016 through 2100 |
| `--app` | No | One application number. Without `--frn`, rows from the filing query are kept only when the application number matches. With `--frn`, it is added to the FRN query. |
| `--frn` | No | Form 471 only. `--form 470 --frn ...` exits 1: `--frn applies to Form 471.` |
| `--csv` | No | CSV body. Takes no value. |
| `--json` | No | JSON body. Takes no value. |
| `--output` | No | Writes the body to this path. Parent directories are created. |
| `--help`, `-h`, `-?` | No | Prints help on stdout and exits 0. Does not run a lookup. |

Valued options accept a separate argument (`--form 470`) or an equals form
(`--form=470`). Repeating a flag is an error (`Duplicate argument: ...`).
`--csv` and `--json` together exit 1: `Choose either --csv or --json.`
Omitting `--form`, `--ben`, or `--fy` exits 1: `Form, BEN, and funding year
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

The body is the table, CSV, or JSON. Status text goes to stderr and is not
part of the body. With no `--output`, the body is written to stdout, and a
final newline is added when the body does not already end with one. With
`--output`, the body is written only to that file. Stdout stays empty. An
empty match still writes the body, then exits 2.

### Table (default)

Columns, in order:

`Form`, `FundingYear`, `BEN`, `Name`, `ApplicationNumber`,
`FundingRequestNumber`, `Status`, `CertifiedDate`, `Nickname`, `Services`,
`RequestedAmount`, `PreDiscountAmount`, `PDF`

Cells longer than 40 characters are cut to 37 characters plus `...` in the
table only. CSV and JSON keep the full text.

### `--csv`

CSV goes through `DataExporter.WriteCsv`. Fields are quoted. A field that
starts with `=`, `+`, `-`, `@`, tab, or carriage return is prefixed with `'`.
A CSV file is UTF-8 with a BOM. CSV on stdout has no BOM. An empty match is
the header row only.

### `--json`

Indented JSON. Names are camelCase. Top-level fields: `form` (470 or 471),
`ben`, `fundingYear`, `applicationNumber`, `fundingRequestNumber`,
`fetchedAt`, `count`, `rows`. Each row has `applicationNumber`,
`fundingRequestNumber`, `status`, `certifiedDate`, `nickname`, `ben`, `name`,
`services`, `pdf`, `requestedAmount`, `preDiscountAmount`. Omitted `--app` or
`--frn` is JSON `null` on the top-level fields. Filing rows leave
`fundingRequestNumber` as an empty string.

### What a row contains

Without `--frn`, the CLI calls `UsacClient.GetFilingsAsync` (the desktop
lookup). Form 470 reads dataset `jt8s-3q52` and keeps certified rows. Form
471 reads dataset `9s6i-myen` and keeps rows with a certified timestamp. Rows
are collapsed to the current version, one application per row.

With `--frn`, the CLI calls the FRN status dataset `qdmp-ygft` instead. The
select list is funding request number, application number, BEN, organization
name, FRN status, nickname, service type, and `funding_commitment_request`.
It does not request a certified date, a pre-discount amount, or a PDF, so
those columns are empty on FRN rows. The query also requires
`form_version='Current'`.

Stderr on success:

```text
Found 1 certified Form 470 application(s) for BEN 143618, FY 2026.
Found 1 Form 471 funding request(s) for BEN 143618, FY 2026.
```

Stderr when nothing matches:

```text
No certified Form 470 found in currently published data for BEN 143618, FY 2027.
No application 999 found in published Form 470 data for BEN 143618, FY 2026.
No Form 471 funding request 2699003124 found in published data for BEN 143618, FY 2026.
```

The count and the form number follow the query. An empty result is not proof
that no filing exists. The help text says the same thing.

## Exit codes

Constants in `CliCommand`: `ExitFound`, `ExitBadArgs`, `ExitNotFound`,
`ExitUsacUnreachable`, `ExitLookupFailed`.

| Code | Name in code | When |
| --- | --- | --- |
| 0 | Found | At least one row matched. `--help`, `-h`, and `-?` also exit 0. |
| 1 | Bad arguments | The parser rejected the command (message and help on stderr). An `ArgumentException` during the lookup also exits 1, with the exception message only. |
| 2 | No match | USAC answered and the row list is empty. The body is still written. |
| 3 | USAC unreachable | `HttpRequestException`, `TaskCanceledException`, or `JsonException`. Stderr starts with `USAC could not be reached.` |
| 4 | Other lookup failure | `InvalidOperationException`, `IOException`, or `UnauthorizedAccessException`. Stderr starts with `Lookup failed; no filing determination was made.` |

Exit 4 covers a Form 471 identity mismatch (`Form471IdentityMismatchException`),
the filing-query result limit below, and a file write that throws one of those
exceptions. The FRN query does not run the Form 471 identity check. Only
`GetFilingsAsync` does, and only for Form 471.

Any other exception is not mapped to these codes.

## Limits

- The `--frn` request sets `$limit` to `1000` and does not ask for another
  page. It does not exit 4 when that page is full. The filter also includes
  the FRN number, BEN, funding year, and `form_version='Current'`, so a single
  FRN is normally one row.
- A Form 470 or Form 471 lookup without `--frn` pages 1,000 rows at a time. If
  every page through offset 49000 is full, `GetFilingsAsync` throws
  `InvalidOperationException` (`Result limit reached; a complete lookup could
  not be made.`) and the CLI exits 4.
- `--fy` accepts 2016 through 2100. A year USAC has not published, including
  FY2027 for some BENs, is an empty published result (exit 2), not a bad
  argument.
- The CLI has no name search, watchlist, budget, or theme flags.
