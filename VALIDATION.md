# Current build validation

- Current deliverable: framework-dependent single-file Windows x64 executable. The .NET runtime is **not bundled**. End users must install the **.NET 10 Desktop Runtime for Windows x64** from https://dotnet.microsoft.com/en-us/download/dotnet/10.0.
- SQLite and California seed data remain bundled. No database server, Python, or Excel installation is required.
- C# core and Windows Forms project built successfully using .NET SDK 10.0.401.
- 37 offline checks passed, including Form 471 endpoint mapping, cache isolation, empty-results expiration/refresh, current status/PDF handling, and filing-window unknown/pre-open/open/closed cases. Existing client/export checks cover: filtering, escaping, current-version grouping, cache reuse/refresh/expiration/error handling, missing budgets, cycle selection, Forecast/Confirmed exceptions, negative values, local directory loading, 15-match cap, CSV formula escaping, Excel package structure, and literal ID/text preservation.
- Earlier live FY2027 test for BEN 133193 returned two filings. Direct USAC verification for BEN 101552 found no FY2026/FY2027 Form 470 but a Forecast budget; BEN 106359 has no returned new-cycle Form 470 and a Confirmed budget. Raw verification files are included.
- Seed snapshot: 26,470 active California entity records and 5,027 California C2 records for the two supported cycles. Refreshes are transactional and preserve previous data on failure.
- Export includes both generation and source timestamps and can represent a successful no-filing lookup. Failed lookups cannot be exported as no-filing results.
- Windows GUI interaction, executable launch with the installed Desktop Runtime, and opening the exported workbook in Microsoft Excel have not been tested in this Linux environment. The Windows executable was cross-compiled successfully; these remain end-user checks.
- The executable is unsigned. Cached data is per Windows user/PC in local application data.

- Theme revision compiled and published successfully: Mint Glass with generated background, Midnight, and Classic. Choice persists locally. Themes and custom painted text have not been visually verified on Windows.

- Latest live checks: FY2027 Form 470 for BEN 133193 returned two applications; FY2026 Form 471 for California BEN 106359 returned applications successfully; FY2027 Form 471 for the same BEN returned zero applications without error. 39 assertions passed in the live-enabled run.
- Latest solution build and Windows publish succeeded with zero warnings/errors. EXE size: 10,631,645 bytes.
- Included GitHub Actions workflow syntax was parsed locally. Hosted Actions execution and release upload are not tested until the user pushes to GitHub. VS_CODE_GUIDE.md includes setup and tag instructions.
- Form 471 window notices use official year-specific pages and cache schedules for 24 hours. Exact FY2027 dates remain unknown, with no guessed date. Notifications require using the running app.

## Multiple-application live verification

On October 9, 2026 (Pacific time), compared the application's live and cached filing lists against an independent USAC API query grouped by application number:

- CENIC, California BEN 225495, FY2027 Form 470: all four applications returned (270000004, 270000191, 270000699, 270000700).
- Same BEN, FY2026 Form 471: all 85 applications returned.
- Exact application-number sets matched; no missing or duplicate applications. Cached re-queries returned the same complete sets.
- Full application IDs and timestamps are in verification/multiple-filings-live.json. This validates the shared data client used by the GUI; Windows GUI interaction remains untested here.

## Selective export update

The Windows Forms grid now supports multiple full-row selections with Ctrl-click and Shift-click, Select all and Unselect all buttons, and a selected-count indicator. CSV and XLSX export filter the original results to the selected application IDs. Zero selected rows with existing forms prevents export; a successful empty lookup remains exportable. Changing funding year or form clears the selection. All rows are selected after lookup.

Updated Windows executable compiled and published successfully. This UI behavior was reviewed in code; interacting with selections and checking the visual appearance still requires testing on Windows. No Windows GUI interaction is claimed.

## macOS preview build

- MacDesktop uses Avalonia 12.1.2 and references the same Core project as Windows. No duplicate USAC/database/export backend was introduced.
- Apple Silicon osx-arm64 and Intel osx-x64 framework-dependent app bundles compiled successfully with no warnings/errors. The .NET runtime is not bundled.
- Verified Mach-O architecture, Info.plist executable, .NET runtime configuration, native SQLite presence, and preserved executable permissions in each ZIP. Bundles are approximately 39.5 MB uncompressed; ZIPs approximately 14.8 MB.
- Re-ran all 37 offline Core checks successfully after adding the Mac project.
- The cross-platform CI workflow includes both Mac architectures; YAML parsed locally, but hosted execution remains pending a GitHub push.
- macOS launch, native library loading, file dialogs, selection, themes and signed/notarized distribution are not verified. These unsigned preview packages need a test on macOS 14 or later with the matching .NET 10 Runtime. No actual macOS GUI execution is claimed.

## Mint Glass icon update

Windows x64 framework-dependent publish succeeded with the application ICO and embedded main-window icon resource. Mac Intel and Apple Silicon bundles contain the ICNS and CFBundleIconFile registration. ZIP integrity and executable permission preservation were checked. GUI launch and OS icon display require Windows/macOS testing.
