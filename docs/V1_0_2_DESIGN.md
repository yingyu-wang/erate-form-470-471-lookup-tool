# v1.0.2 technical design

Status: **proposed design**, not a description of shipped functionality.
Feature IDs and milestones refer to the [implementation plan](V1_0_2_PLAN.md).

## Current architecture to preserve

`Core/UsacClient.cs` performs paged Form 470/471 queries, collapses service rows
into applications, and caches query pages for two hours in SQLite.
`Core/CaliforniaDirectory.cs` stores the California entity/budget snapshot.
`Core/FilingWindow.cs` supplies optional filing-window information.
`Core/DataExporter.cs` writes CSV/Excel with cancellation and text protections.
Windows uses `Desktop/MainForm.cs`; macOS/Linux share `MacDesktop/MainWindow.cs`;
Android uses `Android/MainActivity.cs` and its behavior helpers.

The existing `Filing` record lacks funding year, query outcome, source completeness,
and explicit field availability. Introduce a shared result envelope rather than
having each screen infer those facts. Keep the existing client entry points usable
while migrating consumers. A default Certified display label must not become proof
that a status was explicitly present in the source.

## Shared models and storage

Proposed services: `PortfolioStore`, `BatchLookupService`, `ChangeTracker`,
`FilingFilterService`, `ExportProjection`, and a history/deadline service in `Core/`.
Names are implementation proposals, not current public APIs.

| Model/table | Identity and data | Retention/behavior |
| --- | --- | --- |
| `watchlists` | Stable list ID, name, creation/update timestamps. | Explicit user removal. |
| `watchlist_entries` | Unique `(list_id, BEN)`; optional display alias and tags. | Removing membership does not silently delete snapshots. |
| `lookup_runs` | Run ID, requested BEN/year/form, outcome, source, UTC times, completeness and live/cache origin, safe error summary. | Latest outcomes available even when no snapshot was committed. |
| `filing_snapshots` | Complete result per `(BEN, year, form)`; application payload and source schema version. | Keep current and previous successful live comparison snapshots per query. |
| `change_events` | Query key, application key, before/after values, observation time, acknowledgement time. | Keep unacknowledged events; acknowledged events expire after 90 days. |
| `deadline_metadata` | Year/form/application association, source URL, verification time, date or instant, timezone/precision. | Unknown and expired verification remain explicit. |

Store portfolio data in a versioned SQLite database in the existing app data
directory, separate from disposable query caches. Apply additive transactional
migrations with a schema version; back up before destructive migrations. Do not
rewrite existing directory/cache tables. Serialize writes per store, use bound SQL
parameters, and commit snapshot plus comparison events in one transaction. Reject observations whose request-start
sequence is older than the current baseline inside that transaction; serialize
refreshes per query key and retain the freshness check for overlapping processes.
Future-schema databases must be refused with a useful message, not overwritten.

Track allocated storage and provide **Clear saved history** separately from
**Delete watchlist**. Saved snapshots support offline review beyond the cache TTL;
they are not a substitute for fresh live lookup. A v1.0.1 query-cache entry may be
reviewed as legacy cached data, but must not silently establish a monitoring baseline.

## Batch imports and execution

- Parse quoted CSV fields, commas, escaped quotes, UTF-8 BOM, and CRLF/LF; avoid
  adding a dependency until the parser choice and license have been reviewed.
- Require a unique BEN header; trim outer whitespace and validate ASCII digits.
  Keep BEN as text. Reject ambiguous duplicate headers and overlong fields.
- Initial limits: 1 MiB input, 500 distinct BENs, at most five years per run, and
  at most 1,000 expanded BEN/year/form queries. Explain the limit before execution;
  do not silently truncate. Deduplicate exact validated BENs, not district names.
- Preview missing/invalid BEN rows, ignored optional columns, and duplicate entries.
  Import valid rows only after the user selects that action.
- Start with one concurrent lookup and a shared pacing policy. Honor Retry-After
  for rate limits, allow at most three attempts for transient GET failures with
  bounded backoff, and make waiting cancellable. Do not retry malformed input.
- Outcomes are `Success`, `SuccessEmpty`, `Failed`, `Cancelled`, and `NotStarted`.
  A failed request is never counted or exported as zero filings.
- Cancellation stops pending work and the active query, preserves completed
  districts, and leaves the affected prior snapshot intact. Progress includes
  completed/total queries and errors. Network and disk work stay off UI threads.

`GetFilingsAsync` already pages and fails at a result cap. Preserve that behavior;
do not publish a prefix of rows as a complete snapshot. M0 must verify ordering
and source consistency across pages. Capture request start/end and per-page
freshness; describe successful results as an observed published view rather than
claiming the server supplied an atomic snapshot.

## Change tracking

Application identity is `(BEN, funding year, form type, application number)`.
Canonical comparison fields initially include source-observed status, certified
date, nickname, normalized service set, and validated document link. Keep display
formatting, service order, and fetch timestamps out of the comparison fingerprint.
Preserve original values for display and export; version the fingerprint algorithm.

Only a complete, successful **forced live** lookup may advance the baseline.
Mixed cached/live page results, cancellations, pagination caps, and optional-section
failures must not manufacture changes. Optional budget/deadline data is tracked
independently from the filing baseline. First live success establishes a baseline
and shows **First observation**, not an alert that every old filing is new.

Compare before replacing the baseline. Report **New**, **Changed**, and
**No longer returned by this query**. Do not infer withdrawal, denial, or a status
transition absent source evidence. Successful empty live results are valid
observations but use the same cautious missing-record language. Refreshing again
must not duplicate events; acknowledgement is separate from baseline advancement.
Changing query eligibility or fingerprint versions requires an explicit baseline
reset, not a wave of false alerts.

## Filters, summaries, and history

Use shared case-insensitive text matching with documented AND behavior between
service and keyword filters. Default keyword matching is literal text, not regex.
Filtering a list does not silently select hidden applications. Show selected,
visible, and total counts; provide **Select visible** and **Clear selection**.
Selection uses the composite application identity across all pages and years.

Customer summaries render sections independently with Live, Cached, Saved/Offline,
Unknown, or Failed labels and fetched times. Display C2 totals only from the
available budget source, with budget cycle and forecast status. A customer alias
must not replace the source entity name in an exported source-data column.

History compares application counts and service categories first. Funding/provider
comparison requires M0 to verify a source at funding-request/service-line level,
stable FRN identity, numeric units, category, and amount definitions. Extend the
client only after that mapping is documented. Keep requested, committed, and
disbursed amounts distinct; show unavailable comparison fields as Unknown.
Do not promise provider totals derived from the current application-only model.

## Deadlines and offline behavior

Prefer a source-reported allowable contract date. If calculation is needed, first
verify the official rule and required timestamps; record the rule/version and
clearly label the result Calculated. No inferred date from a shortened certification
date is treated as authoritative. Source dates and exact instants need distinct
types; show source timezone alongside local conversion. User notes are not verified
deadlines. Missing/failed metadata never blocks filings.

Open saved snapshots explicitly, even when the normal cache is expired. Show age,
observation time, and an action to refresh. Offline mode can filter, compare saved
years, and export. It does not claim to download or cache PDFs; opening a remote
document may require connectivity. Delete-history actions explain what local data
will be removed before executing.

## Export and Android integration

Reuse one shared export projection for all interfaces. Keep current columns and
add explicit query outcome, source/freshness, saved observation time, change type,
and watchlist/alias fields where relevant. CSV has one flat schema; Excel initially
uses the same projection, avoiding inconsistent multi-sheet logic. Financial
columns must identify amount type and units. Preserve spreadsheet formula escaping,
UTF-8 handling, and cancellation checks.

Windows/macOS/Linux use save dialogs; Android retains the Storage Access Framework
and paged results. Stream/import bounded documents off the UI thread; preserve
selection and import/run state across rotation without restarting network work.
Android share uses ACTION_SEND text/plain plus the system chooser for a summary
and validated HTTPS link. Sending attachments is deferred; no broad storage
permission is needed for text sharing. Optional share errors remain independent
of lookup state. Android signing/update behavior stays documented as a preview.

## Versioning and packaging

Do not publish a v1.0.2 tag from the planning PR. At M6, update every app version
and script/manifest reference together; choose an Android version code greater
than 111. Remove hardcoded v1.0.1 archive paths in Linux smoke checks and validate
package names against app metadata. The historical Linux publisher is specific
to v1.0.1; use the unified tagged workflow for v1.0.2. Preserve all seven package
variants and one aggregate checksum file; do not reintroduce a standalone APK,
individual checksum assets, or duplicate ARM64 Windows archives.
