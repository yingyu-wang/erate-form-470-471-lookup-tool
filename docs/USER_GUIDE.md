# E-rate Form 470 / 471 Lookup — C# / .NET Windows desktop

Native **C# Windows Forms** application targeting **.NET 10 LTS**. This smaller portable build requires the **.NET 10 Desktop Runtime for Windows x64** installed separately. No Python, web server, EPC login, or separate database server are required. Search by BEN or customer name, select a funding year (default **2027**), and choose Form 470 or Form 471 and check whether certified applications are published.

## GitHub and development

Open `ERateLookup.sln` for the full solution. See [GITHUB_SETUP.md](../GITHUB_SETUP.md) for repository layout, first upload, syncing changes, and release packaging. Build output and per-user SQLite caches are excluded by `.gitignore`.

## End-user requirements and setup

1. Install the **.NET 10 Desktop Runtime — Windows x64** from Microsoft's official download page:
   **https://dotnet.microsoft.com/en-us/download/dotnet/10.0**
2. On that page, scroll to **Run apps – Runtime**, locate **.NET Desktop Runtime**, and choose the **Windows x64 installer**. Install the latest stable 10.0.x patch. The Desktop Runtime includes the base .NET runtime; one installer is sufficient.
3. Extract this ZIP to a folder.
4. Double-click **ERateLookup.exe**. There is no installer for the app itself; it is a portable, framework-dependent executable. The runtime is a one-time prerequisite on each PC.
5. Choose **Form 470** or **Form 471**. Leave funding year at **2027**, or choose another year. Type at least two letters for local California suggestions, or enter an exact filing BEN.

Use the **Desktop Runtime**, which includes Windows Forms support. The base .NET Runtime or ASP.NET Core Runtime alone does not supply the desktop framework. The SDK is only needed to build or edit the project, and the older .NET Framework 4.x does not satisfy this prerequisite.

This executable targets **Windows x64**. Internet access is required for live USAC queries and directory updates. Fresh cached results and local name suggestions work offline. SQLite and California seed data remain bundled. Cached data is stored under the Windows user profile, so moving the EXE does not move its cache.

If the runtime is missing, the .NET application host prompts you to install it. Use the official Desktop Runtime page above. You can check installed frameworks in a Command Prompt:

```bat
dotnet --list-runtimes
```

Look for **Microsoft.WindowsDesktop.App 10.0.x** with x64 installation. A newer major version alone is not the configured prerequisite. The EXE is unsigned. Windows GUI launch has not been tested in this Linux build environment.

## Run from source on Windows

1. Install the **.NET 10 SDK** for Windows: https://dotnet.microsoft.com/download/dotnet/10.0
2. Extract the complete ZIP.
3. Double-click **Run-Windows.bat**. It builds and launches the desktop app.
4. Enter a BEN or customer-name fragment. Leave funding year at 2027 or choose another year.
5. For a name search, local California matches appear as you type (after two characters). Select the entity that files the application and click **Check selected customer**.
6. Select a filing to open the Form 470 PDF or export the results to CSV or Excel (.xlsx).

The **SDK** is required to build or use the source launcher. A framework-dependent binary instead needs the **.NET 10 Desktop Runtime**. The older **.NET Framework 4.x** is not used.

## Rebuild the smaller portable Windows EXE

On Windows x64 with the .NET 10 SDK installed, double-click **Publish-Windows.bat**. It creates:

`publish\win-x64-framework\ERateLookup.exe`

This build **does not bundle the .NET runtime**. It retains the app, SQLite native library and seed data in the single-file publish. The separately installed Desktop Runtime is required to run it. SQLite's native component may extract to the user's temporary directory at launch. Do not enable trimming for WinForms.

Command equivalents:

```powershell
dotnet run --project Desktop/ERateLookup.csproj
dotnet publish Desktop/ERateLookup.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64-framework
```

For Windows ARM64, rebuild using `win-arm64` and use the matching ARM64 Desktop Runtime. Only the x64 executable is included in this package.

## Behavior and cache

- BEN search uses the exact BEN and selected funding year, with certified status.
- Name resolution searches a preloaded California entity directory in SQLite as you type, then checks the chosen filing BEN for **only the selected year**. California is fixed as the directory scope. Use the district/consortium filing BEN rather than a member school entity ID when applicable. No local match: try another spelling, refresh the directory, or enter a BEN.
- “POSTED” means the query returned matching certified filings. “No certified Form 470 found” refers only to USAC's currently published dataset; EPC submissions can take time to appear.
- A failed request or timeout is explicitly reported as a failure, never “no filing.”
- Service/attachment rows are grouped into one result per distinct application. Multiple applications remain visible. Requests are paginated when necessary.
- Successful API responses, including empty results, are cached in a local **SQLite database for two hours** under `%LOCALAPPDATA%\ERate470LookupDotNet\cache\cache.sqlite3`.
- **Refresh live** bypasses cached results. Responses younger than two hours use the database; at two hours or older, the app makes a new live query and updates the database. Microsoft.Data.Sqlite is restored automatically by NuGet; no database-server installation is needed. Clear the cache by closing the app and deleting its cache folder.
- Requests are asynchronous to keep the GUI responsive; HTTP timeout is 30 seconds per request.
- The currently available year list is 2016–2027. Update the list when new years become available.
- There is no bid-deadline logic, Form 471 lookup, or automatic monitoring. C2 budget data covers FY2021–2025 and FY2026–2030; earlier years show that budget data is not loaded.
- Customer-name spelling can differ from familiar district names. School-site IDs may differ from the district or consortium BEN that files.

## Project layout

| Path | Purpose |
|---|---|
| `Core/UsacClient.cs` | HTTP client, typed API records, SoQL filters, pagination, SQLite database cache, grouping |
| `Desktop/MainForm.cs` | Windows Forms GUI, async search, matching entities, PDF launch, CSV / Excel export |
| `Desktop/Program.cs` | Desktop entry point |
| `Desktop/ERateLookup.csproj` | .NET 10 Windows desktop project |
| `tests/ERateLookup.Tests/` | Shared xUnit suite, including the nightly live USAC checks |
| `Run-Windows.bat` | Build/run source on Windows |
| `Publish-Windows.bat` | Publish a runtime-dependent portable EXE |
| `VALIDATION.md` | Actual checks and known limits |

## API

Endpoint: `https://opendata.usac.org/resource/jt8s-3q52.json`

```sql
billed_entity_number = 'YOUR_BEN'
AND funding_year = '2027'
AND fcc_form_470_status = 'Certified'
```

Text is escaped and query parameters are URL-encoded. No API token is needed for the unauthenticated endpoint tested during development. Throttling and access errors are surfaced. No credentials are stored.

The dataset column `form_version` is `Original` or `Current`. Ordinary lookups still collapse to Current when that version is present. The command-line `--diff` flag compares the two published versions for one Form 470. It reads the same dataset and does not require `fcc_form_470_status='Certified'`, because a Current row can be canceled while Original stays certified. It does not store a local history of intermediate revisions, and it does not determine whether a change was substantial. See [the command-line guide](CLI.md).

Official sources:

- USAC data/API: https://www.usac.org/e-rate/service-providers/step-2-responding-to-bids/
- Dataset: https://opendata.usac.org/E-Rate/E-Rate-FCC-Form-470-Tool-Data/jt8s-3q52
- .NET support: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

## Client checks

```powershell
dotnet test tests/ERateLookup.Tests/ERateLookup.Tests.csproj -c Release --filter Category!=Live
$env:ERATE_LIVE_USAC = '1'
dotnet test tests/ERateLookup.Tests/ERateLookup.Tests.csproj -c Release --filter Category=Live
```

The live command checks published filings for known BENs, including FY2027 Form 470 for BEN 133193. Network access and public USAC data availability are required. The nightly GitHub workflow runs that same filter. Open `Desktop/ERateLookup.csproj` in a compatible Visual Studio installation to edit the GUI project. Source-only developers can use any editor and the CLI. Windows watchlist UI tests live in `tests/ERateLookup.Desktop.Tests` and run on Windows.

## Cache schema

```sql
CREATE TABLE query_cache (
  query_key TEXT PRIMARY KEY,
  fetched_utc TEXT NOT NULL,
  payload_json TEXT NOT NULL
);
```

The key is a SHA-256 hash of the endpoint and complete canonical query (including BEN/name, year, filters, and pagination). Timestamps use UTC. Failed HTTP responses do not overwrite successful results or become empty results. Stale cached data is not displayed as a fresh result when a live query fails. Future-dated cache timestamps trigger refresh. The chosen two-hour lifetime follows the final requested refresh threshold.

## California directory and C2 budget display

The EXE bundles a California snapshot from USAC and imports it into SQLite on first run. Entity membership comes from active California entries in the Supplemental Entity Information dataset, with additional BENs present in C2 budget data. This keeps entities without C2 budget records searchable. Districts and library systems are prioritized in suggestions; other entity types are labeled. Data does not imply that every entity number is the BEN that files district-wide applications.

The customer view shows **total five-year budget, committed, pending, and available** amounts from USAC's reported fields. Amounts are pre-discount budget figures; committed does not mean disbursed. The selected funding year determines the budget cycle.

- Forecast, Preliminary, and Confirmed status is displayed. Forecast and Preliminary amounts are not Confirmed budgets.
- Missing records or fields show **Not reported**, never an inferred zero. Missing budget data does not establish filing history or eligibility.
- A fresh directory is checked at startup: once the snapshot is 24 hours old, the application attempts an automatic refresh while open. **Refresh CA directory / budgets** is a separate manual override. No closed-app schedule is created.
- Both dataset downloads must finish before a transaction replaces the local directory/budgets. Download failure preserves the previous snapshot and reports the limitation.
- The Form 470 query cache still has its separate **two-hour** lifetime. Directory/budget data uses the daily snapshot rule.

Directory source: `https://opendata.usac.org/resource/7i5i-83qf.json` (physical_state CA; active records).

C2 source: `https://opendata.usac.org/resource/6brt-5pbv.json` (state CA; supported budget cycles).

Budget columns: `c2_budget`, `funded_c2_budget_amount`, `pending_c2_budget_amount`, `available_c2_budget_amount`, `c2_budget_version`. Remaining amounts are taken from USAC directly, not guessed from invoice totals. Pending requests are shown separately.

The database also contains `entities`, `c2_budgets`, and `directory_meta` tables. `Core/CaliforniaDirectory.cs` implements import, partial-name lookup, per-cycle budgets, and atomic refresh. `Core/CaliforniaC2.json`, `Core/CaliforniaEntities.json`, and `Core/CaliforniaSnapshot.txt` are the bundled snapshot resources.

## Verified missing-Form-470 example

On October 9, 2026 (Pacific time), direct USAC queries for Whittier Christian Schools, BEN 101552, returned no Form 470 application for FY2026 or FY2027. Its FY2026–2030 C2 record reported Forecast status, total $86,271.96, committed $0, pending $0, and available $86,271.96. This demonstrates that a forecast budget may exist without a new-cycle Form 470; it does not prove absence of a Form 471, use of a consortium contract, or eligibility for an award. See `verification/c2-corner-case-verification.json` for the retrieved data and UTC timestamp.

USAC budget status guidance: https://www.usac.org/wp-content/uploads/e-rate/documents/Webinars/2025/Admin-Window-2025-FINAL.pdf

## Conservative forecast display rule

For FY2026/FY2027 selections, a separate two-hour-cached query checks for any certified Form 470 in either year. If none is found and USAC has not reported Confirmed status, the GUI labels the budget **Forecast C2 budget** and the remaining figure **Forecast remaining (not confirmed)**. USAC's original status remains displayed separately. A USAC Forecast budget retains its forecast label even if a Form 470 exists. A failed cycle check never implies absence of filings.

This conservative display rule is a user-requested presentation rule, not USAC's confirmation rule. C2 Form 471 certification/review determines Preliminary/Confirmed budget status; posting Form 470 alone does not confirm enrollment or establish a confirmed budget. A district may use another procurement path, such as an applicable existing contract, so absence of a new Form 470 cannot disprove a Confirmed status reported by USAC.

## Corner cases reviewed

- **Confirmed budget without a new-cycle Form 470:** USAC Confirmed status takes precedence. The UI notes the missing Form 470 but does not downgrade the budget. A live exact query for BEN 106359 returned no 2026/2027 forms; its California budget snapshot reports Confirmed, total $159,240.30, committed $20,740.44 and reported available $138,499.86. No conclusion about why is inferred from these records.
- **Forecast plus posted Form 470:** stays Forecast; posting does not confirm a budget.
- **Preliminary:** original USAC status remains visible; figures are not represented as Confirmed.
- **Negative reported remaining balance:** display it as reported with a review note; never silently clamp to zero.
- **No budget record:** keep the entity searchable and show Not reported; do not infer zero or no eligibility.
- **Duplicate names:** display ID, entity type and city; the user chooses. District and library-system results come first.
- **Member school vs filing BEN:** use the entity that files; do not merge or sum school and district budgets.
- **Names with apostrophes, percent signs or underscores:** quoted/parameterized values; SQLite wildcard characters are escaped.
- **Multiple filings, services, attachments and original/current versions:** distinct application grouping and Current-version preference.
- **Expired, future-dated or corrupt cache:** refresh; HTTP failures never become no-filings.
- **Missing directory refresh or offline mode:** preserve last successful snapshot; timestamp remains visible. Stale Form 470 responses are not substituted as fresh data.
- **Year changes:** clear old filing results; budget lookup uses the selected year's supported five-year cycle.
- **Amounts:** show pre-discount cycle budgets with pending separately; committed is not disbursed. Do not infer invoice spending from these fields.

See VALIDATION.md for build/check evidence and Windows GUI testing limits.

Name suggestions are capped at **15** matches. A prompt asks for more letters when the limit is reached. Searches require at least two characters and use a 250 ms debounce before local SQLite lookup.

## Export CSV or Excel

After a completed lookup, click **Export CSV / Excel** and select the file type in the Save dialog. The export includes UTC export time, lookup fetch time, directory update time, funding year, BEN, customer, filing status, form details and PDF URL, plus the budget cycle, original USAC status, displayed status, total, committed, pending and reported remaining C2 amounts. A successful lookup with no filing can also be exported; a failed lookup cannot be exported as a no-filing result.

Excel export creates a genuine `.xlsx` file without requiring Microsoft Excel to be installed. Columns are stored as text to preserve IDs, timestamps, missing-value labels and exact reported amounts. Header filters and a frozen first row are included. Formula-like strings are escaped in CSV and stored as literal text in Excel. No additional runtime or library is required for export.

## Visual styling

Glass-style gradient background and budget panel, darker high-contrast text, a subtle heading shadow, stronger table headers and softly highlighted rows. This is a painted Windows Forms treatment, not live background blur or operating-system transparency. Budget and table text stays crisp without decorative shadows.

## Theme selector and background

Choose **Mint Glass**, **Midnight**, or **Classic** in the bottom action area. Mint Glass uses an embedded generated pearl/mint background, soft highlighted budget card and heading shadow. Midnight uses darker surfaces with light high-contrast text. Classic uses a plain light surface. Table text remains crisp. The theme choice is saved in `%LOCALAPPDATA%\ERate470LookupDotNet\theme.txt`; no network request is needed to change themes.

The background asset is `Desktop/Assets/mint-glass-background.png`, created with the built-in image-generation feature. Prompt: “Refined liquid-glass-inspired abstract background, pale mint, cool pearl white, subtle blue-gray; translucent glass ribbons around the edges; near-white low-contrast center for dark text and tables; no text, controls, people, logos, or watermark.” This is a bitmap-backed glass-style treatment, not native live acrylic blur.

## Form 470 and Form 471 lookup

Choose **Form 470** or **Form 471**, select the funding year, and enter a BEN or choose a local California name match. Both modes share the same entity directory and budget data. The two-hour filing cache keeps each dataset separate. Changing form type or funding year clears the previous filing results and disables export until another lookup completes.

Form 471 uses USAC's Basic Information dataset (`9s6i-myen`) and the `epc_organization_id` field for the filing BEN. Only rows with a certification date are included. The table shows one row per application, preferring Current versions, with certification date, customer, category, and reported application status. A PDF is opened when available; some Current records have only the original certified PDF link, which may not reflect later changes.

A successful empty response, including FY2027 before Form 471 records are available, displays **No certified Form 471 found in currently published data**. It does not change years, throw an error, or establish that the filing window is closed. API failures display a failure message and never become a no-filings determination. Successful empty lookups can be exported and expire from cache after two hours. Refresh live always bypasses the cache.

CSV and Excel exports include FormType, ApplicationNumber, and FilingStatus as well as the existing year, BEN, timestamps and C2 fields. The C2 forecast rules continue to use USAC's budget status and the separate new-cycle Form 470 check; selecting Form 471 does not by itself confirm a budget.

See [VS_CODE_GUIDE.md](../VS_CODE_GUIDE.md) for local editing, pushing and automated releases.

## Form 471 filing window notices

Selecting Form 471 displays an informational popup and banner for the selected funding year, with a button to open USAC's official page. The app checks the official year-specific E-rate filing-window page and caches the schedule for 24 hours. It accepts only an explicit date range following the selected year's window heading; it never borrows another year's dates or infers a window from an empty filing result. Unknown dates remain unknown, and lookups stay available even if the schedule cannot be checked. Before opening it asks users to return after the window opens; while open it says filings can now be checked; after closing it allows historical lookup. Dates use Eastern Time. Notifications appear while using the app, not as a background Windows service.

As checked October 9, 2026, USAC says FY2027 Form 471 opens in early 2027, without an exact date on the official announcements reviewed. FY2027 Form 470 is already available. Source: https://www.usac.org/e-rate/resources/announcements/ . No specific FY2027 opening date is hardcoded.

## Export selected filings

After a successful lookup, all returned filing rows are selected by default. Use **Unselect all** and hold **Ctrl** while clicking rows to select individual forms; Shift-click selects a range. **Select all** restores every returned form. The selected-count label shows how many rows will be exported. Both CSV and Excel exports include only the selected filing rows, in the original results order. This applies to Form 470 and Form 471. If forms exist but no row is selected, export asks you to select at least one; it does not silently export all rows. Successful no-filing lookups can still export a no-results record. Changing year or form clears previous results and selections.

## macOS source and builds

The same solution now includes **MacDesktop**, an Avalonia C# GUI referencing the existing Core project. Windows remains Windows Forms; shared USAC, database, directory, cache and export source is not duplicated. See [MACOS_README.md](../MACOS_README.md) for Apple Silicon / Intel packages and build commands. macOS uses the standard .NET 10 Runtime rather than the Windows-only Desktop Runtime. The included GitHub Actions workflow builds both Mac architectures and Windows and attaches all three packages to tagged releases. macOS packages are unsigned previews and require a launch test on a Mac.

## Application icon

The Mint Glass document/search icon is included in `Assets` as PNG, Windows ICO, and macOS ICNS. Windows builds embed the ICO in the executable and main window. The Mac packaging script copies the ICNS into the app bundle and registers it in Info.plist. No additional runtime dependency is required.

## v1.0.2 development: saved BENs

Open **Saved BENs / Watchlists** to save favorite BENs, refresh a list, import CSV,
review changes, and reopen results offline. See [the portfolio guide](PORTFOLIO_GUIDE.md).
These features require the unreleased v1.0.2 build from a successful PR #8 Actions run.
