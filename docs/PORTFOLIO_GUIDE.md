# Saved BENs and district portfolios — v1.0.2

Saved BEN portfolios are included in the published [v1.0.2 packages](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.2).
The **October 12, 2026 v1.0.2 rebuild** includes the main-screen search-result
watchlist shortcuts below. If you downloaded v1.0.2 earlier, download it again
to get the updated UI.

## Save your districts

1. Open **Saved BENs / Watchlists** from the main app screen.
2. Select **Favorites**, or enter a list name and choose **New list**.
3. Enter a BEN, optional display name, and optional tags. Choose **Save BEN**.

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

## Save directly from partial-name search results

1. Enter part of a district name and choose **Search** (or use the local suggestions).
2. Select one or more district entries in the name-match list. On Windows/Linux,
   use Ctrl-click or Shift-click for multiple entries; on macOS use Command-click
   or Shift-click. On Android, check the boxes beside the matched districts.
3. Choose **Add selected BENs to watchlist** beside/below those search results.
4. Pick the destination watchlist, review the selected BENs and names, optionally
   enter tags for all newly added entries, and choose **Save**. A single entry's
   display name can be edited in the confirmation dialog.

This action saves district identities from the directory. **No filing lookup is
required.** It works when a BEN has no published Form 470 for 2027, no C2 budget,
or no filings for the selected year. Existing BENs in that watchlist are skipped
without replacing their saved names/tags. Cancel adds nothing; a failed batch
adds nothing. The status reports added and already-saved counts.

To look up filings instead, select exactly one district and choose **Check
selected customer**; on Android tap **Use BEN** and then **Look up**. You can also
use **Add BEN to watchlist** after a direct BEN lookup, including an empty result;
its budget name is suggested when available, otherwise the display name is blank.
Changing search criteria clears the previous result selection. Name suggestions
remain capped at 15 CA matches; narrow the name if needed. Android's **Search US**
searches historical published Form 470 entities and can have different coverage
from the CA directory.

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

A refresh also reads FRN Status for each tracked BEN and funding year from the
public dataset `qdmp-ygft`, using the same columns as the command-line `--frn`
lookup. The first complete live result is a baseline. Later refreshes flag an
unread change when the published FRN status, the reported committed amount, the
FCDL date, the revised FCDL date, or the disbursed amount differs. A funding
request that appears, or is no longer returned, is flagged too. That label does
not establish cancellation or denial. Cached FRN responses do not advance the
baseline. An older request cannot replace a newer baseline. A failed FRN refresh
keeps the previous snapshot and does not erase saved filings.

**Reported committed amount is not a funding commitment.** It is the same
inference as export `CommittedAmount`: `funding_commitment_request` when the
published status is Funded, and blank otherwise. **Q8 is pending.** This has
not been confirmed against an FCDL, including when a revised FCDL date is
present. Change text calls it "Reported committed amount" and does not treat a
status change as a decision letter. A missing disbursement stays blank. It is
not stored as zero. Portfolio files do not add `FrnStatus`, `CommittedAmount`,
`FcdlDate`, `RevisedFcdlDate`, or `DisbursedAmount`. The shared base stays 17
columns.

## Filter, select, and export

Keyword matches source entity name, BEN, application number, nickname, or service
text. Service filtering matches the service text. Both filters must match when
both are entered; matching ignores case. Filters do not search inside PDFs.

Results are displayed in pages of 50. Select individual rows, use **Select visible**
to select all results matching the current filters across pages, or **Clear selection**.
Selected rows stay selected when a filter hides them; the selected and visible
counts make that scope explicit. A new lookup or opening saved data resets selection.

**Export selected** writes CSV, Excel, or JSON. Android has separate CSV, Excel,
and JSON buttons and uses the system document picker. The file starts with the
same base columns as a lookup export, then adds four trailing columns:
`LookupOutcome`, `Change`, `SourceAndNotes`, and `AllowableContractDate`.
Requested and pre-discount amounts are the base columns `RequestedAmount` and
`PreDiscountAmount`. A missing amount is blank, and a blank amount is not zero.
Category 2 budget columns are not in this file. The previous headers `Customer`,
`FormType`, `FilingStatus`, `RequestedFundingUSD`, and `PreDiscountEligibleUSD`
are not written.

`Linked` is `false`. `Form470Count` and `Form471Count` are blank. `Source` is the
USAC Open Data URL when the row has an application number, and blank when it does
not. Failure and empty rows can be selected and exported. They are not represented
as filed applications: the outcome column says what happened.

JSON uses the command-line camelCase row and adds `lookupOutcome`, `change`,
`sourceAndNotes`, and `allowableContractDate`. The envelope `form`, `ben`, and
`fundingYear` are filled when every selected row shares that value. A mixed
selection stores JSON `null` for the field that differs. `fetchedAt` is the
earliest stored observation time.

If you cancel while writing a destination, that file may be incomplete; delete it
before retrying. CSV values that could be interpreted as spreadsheet formulas are
escaped; Excel values are written as text. JSON is UTF-8 without a BOM.

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

**Clear saved history** deletes saved filing snapshots, FRN snapshots, change events, and last-run outcomes
after confirmation, while retaining watchlists. Pending observations begun before
that deletion cannot recreate cleared history. Uninstalling the Android app clears
local data. Debug-signed CI builds may require uninstalling an older preview when
the signing key changes; export needed information first.

## Storage and validation limits

The new `portfolio.sqlite3` is separate from the disposable query cache. Desktop
stores it beside the existing cache directory; Android stores it in private app
files. Existing v1.0.1 caches and themes are preserved. Schema upgrades are
transactional; a newer unknown schema is refused rather than overwritten. Schema
version 3 adds an FRN snapshot row per query and funding request number
(`frn_status`, reported committed amount, `fcdl_date`, `revised_fcdl_date`,
`disbursed_amount`, and the observation sequence). The reported amount is the
`CommittedAmount` inference, not a confirmed commitment (Q8). Keep
the app closed when backing up/copying the database. No cloud synchronization or
background notification service is included.

GitHub CI compiles the supported architectures, runs shared checks, verifies the
Android APK, and launch-checks Linux. Interactive desktop and physical Android
checks still need to be recorded before a final release. See the repository's
v1.0.2 validation document for that checklist.
