# Saved BENs and district portfolios — v1.0.2

Saved BEN portfolios are included in the published [v1.0.2 packages](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.2).
The main-screen **Add BEN to watchlist** shortcut described below is newer and
requires a successful PR #9 / subsequent main-branch Actions build; it is not in
the published v1.0.2 packages.

## Save your districts

1. Open **Saved BENs / Watchlists** from the main app screen.
2. Select **Favorites**, or enter a list name and choose **New list**.
3. Enter a BEN, optional display name, and optional tags. Choose **Save BEN**.
   In builds with the shortcut: from the main screen, search by partial name, run the
   lookup, then choose **Add BEN to watchlist**. The BEN and customer name are
   filled in; pick the watchlist and save. You can also look up a BEN directly;
   its budget name is suggested when available, otherwise the name stays blank.
   Editing the customer clears the desktop selection until you run another lookup.
   On Android, you can save a selected or entered BEN before running the lookup.
4. Select an existing BEN to edit its name/tags, then choose **Update BEN**.
   To use a different BEN, save it as another entry. **Remove BEN** removes only
   membership in the selected list.
5. Use **Rename list** or **Delete list** to manage lists. Deleting a list retains
   its saved filing history. Lists and history are local to this device/profile.

If all lists have been deleted, create a list in **Saved BENs / Watchlists** first.
A saved-data error is reported when you open a watchlist action; ordinary lookup
remains available. Newer portfolio data is never downgraded or reset automatically.

The portfolio window has its own selection of BENs, years, and forms. Opening it
does not automatically run network lookups or change your main-screen lookup.

## Import a customer CSV

Choose **Import BEN CSV** and select a UTF-8 file with this header:

```csv
BEN,Name,Tags
225495,CENIC,California
```

`BEN` is required; `Name` and `Tags` are optional. Additional columns are ignored.
Quoted commas and escaped quotation marks are supported. BENs remain text, so
spreadsheet software should format the BEN column as text before saving the CSV.

Review the valid-row count and reported invalid/duplicate rows, then continue to
import valid entries. BENs already in the list are skipped. An import is atomic:
if it fails or exceeds list capacity, it adds no entries. Limits are 1 MiB per
file, 500 distinct BENs per list, 50 columns, and 10,000 CSV records. Names may
contain 200 printable characters; tags may contain 300. Imports do not look up
districts until you choose a refresh action.

## Refresh and compare

Enter one to five comma-separated funding years, choose Form 470, Form 471, or
both, then select **Refresh watchlist**. To query one saved BEN, select it first
and choose **Refresh selected BEN**. A run is limited to 1,000 BEN/year/form
combinations. Requests run sequentially; **Cancel** stops pending work.

Results distinguish a successful query with applications, a successful empty
query, a failed request, cancellation, and a query that was not started. A failure
does not erase previously saved results. Use **Open saved / offline** to inspect
those results; their observation time remains visible.

The first complete live result establishes a baseline. Later live refreshes show
new applications, changed observed fields, and applications **No longer returned
by this query**. That last label does not establish withdrawal or denial: the
underlying query is limited to published certified filings. Cached/partial
responses do not advance the baseline. An older overlapping request cannot replace
a newer baseline.

**Changes only** filters to unread changes. **Change history** shows before/after
values; **Mark changes read** acknowledges changes through the observations you
have displayed. Unread events survive later refreshes; acknowledged events are
eligible for cleanup after 90 days. Acknowledgement does not reset the baseline.

## Filter, select, and export

Keyword matches source entity name, BEN, application number, nickname, or service
text. Service filtering matches the service text. Both filters must match when
both are entered; matching ignores case. Filters do not search inside PDFs.

Results are displayed in pages of 50. Select individual rows, use **Select visible**
to select all results matching the current filters across pages, or **Clear selection**.
Selected rows stay selected when a filter hides them; the selected and visible
counts make that scope explicit. A new lookup or opening saved data resets selection.

**Export selected** writes CSV or Excel. Android has separate CSV and Excel buttons
and uses the system document picker. The export retains the original lookup/budget
columns and adds query outcome, change, source notes, source-reported contract date,
and available Form 471 requested/pre-discount amounts. Failure/empty rows can be
selected and exported without being represented as filed applications.

If you cancel while writing a destination, that file may be incomplete; delete it
before retrying. CSV values that could be interpreted as spreadsheet formulas are
escaped; Excel values are written as text.

## Summaries, history, and dates

Choose several years and refresh or open saved results, then open **History / summary**.
It groups application counts, source names, latest certification, observed services,
and observation times by BEN/year/form. It also shows the existing California C2
budget snapshot with its separate date and reported status. Forecast and missing
budgets remain identified; missing data is not a zero budget.

Form 471 requested-funding comparisons sum one observed current application amount
per application. An unavailable amount makes that group's total Unknown. Requested
funding is not committed/disbursed funding. Provider comparisons remain unavailable
in this application-level view.

**Deadlines** shows source-reported Form 470 allowable contract dates and attempts
to retrieve filing-window dates for the displayed years. Each includes its source
and observation/check time. Unknown dates remain Unknown. These are date-only
values, not a claim about the exact time of day. Verify EPC and RFP amendments
before acting; a source snapshot can be stale. Deadline lookup is separate from
filing lookup and does not erase results if unavailable.

## Offline data and Android sharing

**Open saved / offline** loads complete portfolio snapshots regardless of the
normal two-hour HTTP cache expiry. You can filter, compare saved years, and export
without a network connection. PDFs are links, not downloaded attachments.

On Android, **Share filing summary** opens the system share chooser with the BEN,
year, form, application, service text, observation note, and an available HTTPS
document link. Choose the receiving app yourself; the app does not send automatically.

**Clear saved history** deletes saved snapshots, change events, and last-run outcomes
after confirmation, while retaining watchlists. Pending observations begun before
that deletion cannot recreate cleared history. Uninstalling the Android app clears
local data. Debug-signed CI builds may require uninstalling an older preview when
the signing key changes; export needed information first.

## Storage and validation limits

The new `portfolio.sqlite3` is separate from the disposable query cache. Desktop
stores it beside the existing cache directory; Android stores it in private app
files. Existing v1.0.1 caches and themes are preserved. Schema upgrades are
transactional; a newer unknown schema is refused rather than overwritten. Keep
the app closed when backing up/copying the database. No cloud synchronization or
background notification service is included.

GitHub CI compiles the supported architectures, runs shared checks, verifies the
Android APK, and launch-checks Linux. Interactive desktop and physical Android
checks still need to be recorded before a final release. See the repository's
v1.0.2 validation document for that checklist.
