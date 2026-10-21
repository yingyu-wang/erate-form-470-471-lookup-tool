# E-rate Form 470 / 471 Lookup Tool — release notes

## v1.0.6 — unreleased

Not tagged. Do not tag this version until the latest nightly USAC check says `passed` or `passed after retry`, and only with Ying's approval. Release files are unsigned by design. No signing step, certificate, or signed package is added.

### Shared export schema and CLI Excel

- Core has one export schema (`ExportSchema`) and a shared JSON writer (`DataExporter.WriteJson`). The command-line table, CSV, and JSON are built from that schema.
- Category 2 budget columns stay out of the shared base schema. App and portfolio exports still use their current headers and files.
- The schema records the unified header names (`Name`, `Form`, `Status`, and the other shared columns). A portfolio export is defined as those base columns plus trailing `LookupOutcome`, `Change`, `SourceAndNotes`, and `AllowableContractDate`.
- Command-line JSON keeps the `CliRow` shape. The table, CSV, and JSON include `FrnStatus`, `CommittedAmount`, `FcdlDate`, `RevisedFcdlDate`, and `DisbursedAmount` after the base columns. Those five names stay out of the shared base list. Form 470 and Form 471 application rows leave them blank.
- `--xlsx` (alias `--excel`) writes the 17 base columns only. It does not include those five FRN columns in 1.0.6. Adding them to Excel is deferred to a future release. The flag requires `--output`. `--csv`, `--json`, and `--xlsx` cannot be combined. `--diff` supports the table, CSV, and JSON only. Workbook cells are inline text, including amounts. A value that CSV prefixes with `'` is stored in the workbook as the original text. The sheet has no formula elements.

### Form 470 Original vs Current diff

- `ERateLookup.Cli --diff` compares published Original and Current rows for a Form 470 from USAC Open Data dataset `jt8s-3q52`. The comparison is field by field and service request by service request. Service lines match on `service_request_id`.
- An ordinary lookup still collapses to the Current version when USAC published one. The diff query does not require `fcc_form_470_status='Certified'`.
- The diff does not determine whether a change was substantial. It does not store a local history of intermediate revisions.

### Committed and disbursed amounts from Open Data

- `--frn` reads committed, FCDL, and disbursed fields from the public USAC Open Data FRN Status dataset `qdmp-ygft`. The command does not sign in to EPC and does not scrape EPC.
- The command-line table, CSV, and JSON include `FrnStatus`, `CommittedAmount`, `FcdlDate`, `RevisedFcdlDate`, and `DisbursedAmount`. Form 470 and Form 471 application rows leave those columns blank. Excel does not include them in 1.0.6. Adding them to Excel is deferred to a future release. Desktop, Mac, Linux, and Android exports are unchanged.
- `FrnStatus` is the published `form_471_frn_status_name`. `FcdlDate` is `fcdl_letter_date`. `RevisedFcdlDate` is `revised_fcdl_date`. `DisbursedAmount` is `total_authorized_disbursement`. A missing disbursement stays blank.
- **[Inference]** `CommittedAmount` is `funding_commitment_request` when the status is Funded, and blank otherwise. **Q8 is pending for Ying:** this has not been confirmed against an FCDL, including when a revised FCDL date is present.
- `RequestedAmount` on an FRN row remains the published `funding_commitment_request` for every status. That is the existing column, not the committed-amount rule.
- The FRN query still keeps `form_version='Current'`.
- Checked 2026-10-19. Current FRN `2099010373` (Webster Groves School District, BEN 136910, FY 2020) was `Funded`, with `funding_commitment_request` `14908.32`, `fcdl_letter_date` `2020-05-09T00:00:00.000`, `revised_fcdl_date` `2026-07-14T00:00:00.000`, and `total_authorized_disbursement` `14908.32`. URL: https://opendata.usac.org/resource/qdmp-ygft.json?funding_request_number=2099010373&$select=funding_request_number,application_number,ben,funding_year,organization_name,form_471_frn_status_name,funding_commitment_request,fcdl_letter_date,revised_fcdl_date,total_authorized_disbursement,form_version,pending_reason
- Field text is from https://opendata.usac.org/api/views/qdmp-ygft.json read the same day.

## v1.0.5 — draft, October 19, 2026: applicant search and linked forms

Draft for review. Do not tag this version until the latest nightly USAC check says `passed` or `passed after retry`. Release files are unsigned by design.

- Search published Form 470 and Form 471 rows by applicant name, by BEN, or by both. The name match uses the published USAC applicant name.
- Every command-line row includes its USAC Open Data source link in the table, CSV, and JSON. The source link is not shortened.
- The published 1.0.4 CLI already writes JSON (`--json`) and CSV (`--csv`). Version 1.0.5 keeps both. CSV rows include `Source`, `Linked`, `Form470Count`, and `Form471Count`. Counts are filled for `--form both` and left blank when that form was not queried.
- `--form both` reads Form 470 and Form 471 for the same applicant and funding year. Each row keeps its own form and source link. A published Form 470 does not establish a Form 471.
- An empty result says that it is not proof that no filing exists. If USAC cannot be reached while linking the two forms, the command does not present a partial link.
- The lookup stays read-only. It does not create, change, certify, or submit a filing.
- A name-only `--form both` result is printed only when every row shares one published BEN. An FRN row with no funding-request number keeps its source link on the FRN dataset.
- The source-link fix landed in [PR #19](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/pull/19). An FRN row with a blank funding-request number keeps its link on the FRN dataset (`qdmp-ygft`). The quoted column filter was checked against live USAC Open Data and left unchanged.
- Version 1.0.5 metadata; Android version code 114. All seven desktop package variants and the command-line packages remain supported build targets.
- Release files are unsigned by design. Windows, macOS, and Linux packages are not signed or notarized. The Android preview still uses a generated debug signing key.

Not in this version, and left for follow-on work after 1.0.5 merges:

- A Category 2 budget helper that multiplies a per-student figure by enrollment, labeled as computed and not USAC data.
- Authentication or token handling for EPC.
- Diffing Form 470 versions over time.
- Pulling actual committed amounts.

## v1.0.3 — October 14, 2026: hardened lookups, tests, and release workflows

Prepared from [PR #12](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/pull/12). Release files are unsigned by design.

- Shared offline checks and the Windows watchlist checks are xUnit projects. Offline runs use `dotnet test --filter Category!=Live`. A nightly workflow can run the live USAC cases and records `passed`, `passed after retry`, or `skipped due to USAC unreachable`.
- The current funding year follows the date: July through December are year N, and January through June are year N−1. New-cycle Form 470 queries no longer assume 2026 and 2027.
- Windows, macOS, Linux, and Android share one lookup view for search validation, lookup, new-cycle notes, budget text, the Form 471 window banner, and export rows.
- Every Form 471 lookup, including a cache hit, requires `epc_organization_id` to match the requested BEN. A mismatch is an error, not an empty result.
- Certified and modified dates are parsed before sorting. A parsed value displays as `yyyy-MM-dd`; an unparseable value is kept as published.
- Workflow actions are pinned to commit SHAs. `contents: write` is limited to jobs that publish or move a tag. Rebuilding an existing release moves the lightweight tag only in the `release-tag-move` environment.
- The Windows program file is `ERateLookup.exe`. User data stays in `%LOCALAPPDATA%\ERate470LookupDotNet`.
- Closing the Windows window releases the query-cache file. Expired cache rows are pruned, and operational UI errors are logged.
- Version 1.0.3 metadata; Android version code 113. All seven package variants remain supported build targets.
- Release files are unsigned by design. Windows, macOS, and Linux packages are not signed or notarized. The Android preview still uses a generated debug signing key.

## v1.0.2 — rebuilt October 12, 2026: search-result watchlist saving

- Select one or more partial-name search matches and choose **Add selected BENs
  to watchlist**, without running a filing lookup. Works even with no published
  2027 Form 470 or C2 budget. Available on Windows, macOS, Linux, and Android.
- Confirm the destination, selected districts, and optional tags. Existing BENs
  are skipped without changing their names/tags; batch additions are atomic.
- Keep the main-screen shortcut for a directly identified BEN, including empty
  filing results.
- Keep ordinary lookup available when saved portfolio data is unreadable or belongs
  to a newer app; show the error when a watchlist action is used.
- Clear the previous customer when input changes so a direct BEN cannot inherit
  another district's display name.

The October 12 rebuild includes PRs #9–#11 and regenerates all seven platform
archives and SHA256SUMS.txt. Download the rebuilt v1.0.2 packages to use these
controls; earlier v1.0.2 downloads have the previous UI.

### Saved BEN portfolios

- Dedicated Saved BENs / Watchlists screen on Windows, macOS/Linux Avalonia, and Android.
- Named lists with saved BENs, names/tags, and validated atomic CSV import.
- Cancellable batch refresh across up to five years and both form types, with distinct failure/empty outcomes.
- Durable offline snapshots and overlap-safe comparison baselines.
- Persistent unread changes, before/after history, and explicit acknowledgement.
- Service/keyword filters, selection across 50-row pages, and consolidated CSV/Excel exports.
- District summaries with independently dated C2 budget snapshots and observed Form 471 requested-funding totals.
- Source-reported Form 470 contract dates and optional filing-window dates; missing data remains Unknown.
- Android system share chooser for filing summaries and HTTPS document links.
- Version 1.0.2 metadata; Android version code 112. All seven package variants remain supported build targets.

See [portfolio usage](docs/PORTFOLIO_GUIDE.md), [source mappings](docs/V1_0_2_SOURCE_CONTRACTS.md),
and [validation status](docs/V1_0_2_VALIDATION.md). Provider comparisons are unavailable
in the application-level view. Hands-on desktop and physical Android checks remain outstanding.
Merged implementation CI passed across all supported build targets; the release workflow
rebuilds all seven packages from the v1.0.2 tag and publishes them with SHA256 checksums.

### v1.0.2 downloads

| Asset | Platform | Runtime prerequisite |
| --- | --- | --- |
| ERateLookup-v1.0.2-win-x64.zip | Windows x64 | .NET 10 Desktop Runtime x64 |
| ERateLookup-v1.0.2-win-arm64.zip | Windows ARM64 | .NET 10 Desktop Runtime ARM64 |
| ERateLookup-v1.0.2-macos-arm64.zip | macOS 14+ Apple Silicon | .NET 10 Runtime ARM64 |
| ERateLookup-v1.0.2-macos-x64.zip | macOS 14+ Intel | .NET 10 Runtime x64 |
| ERateLookup-v1.0.2-linux-x64.tar.gz | Linux x64 | Runtime bundled; native libraries and X11/XWayland required |
| ERateLookup-v1.0.2-linux-arm64.tar.gz | Linux ARM64 | Runtime bundled; native libraries and X11/XWayland required |
| ERateLookup-v1.0.2-android-preview.zip | Android 8.0+ ARM64; x64 emulators | Runtime bundled; extract APK to install |

Android remains a development preview with a generated debug signing key; an update may
require uninstalling the prior preview, which deletes local app data. Export needed data first.
Windows and macOS packages are unsigned; macOS packages are not notarized.

## v1.0.1 — Published

Unified 1.0.1 release for Windows x64/ARM64, macOS Apple Silicon/Intel, Linux x64/ARM64, and Android preview.

Android packaging removes command-line executable paths from the verifier and includes four security regression checks. Android remains a development preview: optional lookup failures preserve filings, result views are paged in groups of 50, exports honor cancellation during generation and copying, and stale California data refreshes at startup with offline fallback.

Linux packages were added after the original v1.0.1 tag; the release notes link
the exact Linux build commit. They share the Avalonia desktop screen and include
.NET 10. Ubuntu 24.04 x64 and ARM64 are the build/window-launch-check baseline.
See `docs/LINUX_INSTALL.md` for installation, dependencies, and compilation.

## Features

- Form 470 and Form 471 lookup by BEN and funding year.
- Bundled California directory with local partial-name suggestions, capped at 15.
- Shared local SQLite database; two-hour filing caches keyed by BEN, year, and form.
- C2 budget display, including forecast labeling and available snapshot information.
- All distinct certified applications returned by the published USAC dataset.
- Select All / Unselect All, individual and range selection, and selected-only CSV/Excel exports.
- Mint Glass, Midnight, and Classic themes, with saved theme preference.
- Mint Glass icon embedded in Windows and registered in macOS bundles.

## Downloads

| Asset | Platform | Runtime prerequisite |
| --- | --- | --- |
| ERateLookup-v1.0.1-win-x64.zip | Windows x64 | .NET 10 Desktop Runtime x64 |
| ERateLookup-v1.0.1-win-arm64.zip | Windows ARM64 | .NET 10 Desktop Runtime ARM64 |
| ERateLookup-v1.0.1-macos-arm64.zip | macOS Apple Silicon | .NET 10 Runtime Arm64 |
| ERateLookup-v1.0.1-macos-x64.zip | macOS Intel | .NET 10 Runtime x64 |
| ERateLookup-v1.0.1-linux-x64.tar.gz | Linux x64 desktop | Runtime bundled; native Linux libraries required |
| ERateLookup-v1.0.1-linux-arm64.tar.gz | Linux ARM64 desktop | Runtime bundled; native Linux libraries required |
| ERateLookup-v1.0.1-android-preview.zip | Android 8.0+ ARM64; x64 emulators | Runtime bundled |

Download the Android ZIP and extract it to get **ERateLookup-v1.0.1-android-preview.apk**, then tap the APK to install. Android installation instructions are included in the Android ZIP and `docs/ANDROID_INSTALL.md`. The preview uses a generated debug signing certificate; updates from another build may require uninstalling first, which removes the app’s local cache. Android version code is 111. No iOS package is included.

Install the matching runtime from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), extract the ZIP, and run the EXE or .app. Windows/macOS runtimes are not bundled. Linux runtimes are bundled: extract the matching tar.gz and run ERateLookup. SHA256SUMS.txt provides checksums for all seven package archives.

## Validation and limitations

CI builds Windows/macOS packages and verifies the Android APK signature, manifest, native SQLite libraries, and package contents. All 52 host checks and four verifier security regression tests pass. These GUI builds have not been launched on Windows or macOS in this build environment; hands-on launch, selection, export, and layout testing is still required. Packages are unsigned; macOS packages are not notarized. The funding-year selector currently covers 2016–2027. Local suggestions cover California; USAC publishing delays and API errors can affect results.
