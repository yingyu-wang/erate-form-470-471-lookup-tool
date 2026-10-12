# v1.0.2 implementation plan

Status: **approved; implementation in PR #8, release validation in progress**.
The original plan was approved and merged through PR #7.
Baseline: v1.0.1 plus the Linux implementation merged in PR #6.

See [usage](PORTFOLIO_GUIDE.md), [verified source contracts](V1_0_2_SOURCE_CONTRACTS.md),
and [validation status](V1_0_2_VALIDATION.md) for the current build.

The goal is to help users review a portfolio of districts, identify changes, and
prepare account-planning exports. The proposed v1.0.2 scope covers all ten ideas
below. Work is divided into independently reviewable milestones, beginning with
watchlists, batch lookup, and change tracking. A milestone being complete does
not mean all v1.0.2 features are ready to release.

## Scope and acceptance criteria

| ID | Feature | Planned behavior | Acceptance criterion |
| --- | --- | --- | --- |
| F01 | Saved district watchlists | Named local lists containing BENs, display names, and optional territory tags; refresh selected districts or the whole list. | Lists survive restart; duplicate BENs within a list are rejected; edits and removal work without deleting filing history. |
| F02 | CSV batch lookup | Preview and validate a BEN CSV, choose years/forms, run cancellable lookups, and show progress and per-query outcomes. | Mixed valid/invalid input identifies row errors; failures do not erase successful districts; consolidated exports distinguish errors, cancellation, and successful empty results. |
| F03 | Highlight changes | Compare complete live results with the prior complete snapshot; show new applications and changed observed fields. | First lookup establishes a baseline; repeated equivalent data produces no changes; failed, partial, or cached queries do not advance it. |
| F04 | Service and keyword filters | Filter fetched applications by service and text in names, nicknames, and service descriptions. | Filters combine predictably, display visible/total counts, and preserve explicit selections; unmatched text is not treated as proof that a service was not requested. |
| F05 | Deadline dashboard | Display source-backed Form 470 allowable contract dates where available and funding-year filing-window dates. | Every date has a source, verification time, and date/time precision; absent or unverified dates show Unknown; reminders are local views, not background notifications. |
| F06 | District history | Compare application counts and requested services across selected years; add funding/provider comparisons only when supported by verified source fields and aggregation rules. | Counts match underlying applications; unavailable years/amounts stay Unknown; totals never sum duplicate application or funding-request rows. |
| F07 | Customer summary | Show identity, counts, certification dates, changes, available C2 budget snapshot information, and document links. | Each independently fetched section has its own freshness and availability label; one optional failure does not hide successful filing results. |
| F08 | Offline viewing | Explicitly open saved complete snapshots, including old data beyond the normal query-cache TTL. | Airplane-mode review and export work; dates and Offline/Saved labels remain visible; missing local PDFs are opened only when connectivity is available. |
| F09 | Selected-result exports | Extend existing CSV/Excel exports to batch, history, and filtered views with shared columns and clear selection scope. | Export contains exactly the selected application keys across pages; error/no-result records have separate outcome fields; spreadsheet formula protection and cancellation remain intact. |
| F10 | Android sharing | Share a filing summary and valid HTTPS document link using Android's system chooser. | A user tap opens the chooser; no automatic sending; missing links, no receiving app, rotation, and cancellation are handled. |

Selected-result exports already exist in v1.0.1. F09 extends and standardizes them.
Android already has Search US using published Form 470 records; it is not a
complete national entity directory. None of these changes introduces iOS support.

## Milestones and implementation order

| Milestone | Deliverables and likely files | Depends on | Exit gate |
| --- | --- | --- | --- |
| M0 — Contracts and source verification | Confirm dataset fields, status coverage, deadlines, funding/provider granularity, and stable pagination; design shared models and migration in `Core/`; fixtures in `Checks/`. | Baseline | Source mappings and aggregation rules documented; unsafe inferences ruled out; migrations preserve existing data. |
| M1 — Watchlists and saved snapshots | F01/F08: `Core/PortfolioStore.cs`, shared snapshot models; watchlist and saved-results views in Windows, shared Avalonia, and Android. | M0 | Restart/offline/upgrade checks pass on all supported app interfaces. |
| M2 — Batch and change tracking | F02/F03: `Core/BatchLookupService.cs`, `Core/ChangeTracker.cs`; CSV preview, refresh progress, Changes view. | M1 | Cancellation, partial failure, rate limiting, and baseline tests pass; long lists remain responsive. |
| M3 — Review and export | F04/F07/F09: shared filtering and export projection; customer summary and explicit export scope on each interface. | M2 | Same input produces equivalent filtering, summary, and exported rows across platforms. |
| M4 — History and deadlines | F05/F06: history queries and verified date metadata; chronological views and due-date dashboard. | M0, M3 | Date/time and missing-data checks pass; unsupported financial/provider fields remain visibly unavailable. |
| M5 — Android sharing | F10: share action in `Android/MainActivity.cs`, reusable summary formatting in `Core/`. | M3 | Physical Android install, chooser, lifecycle, and accessibility checks pass. |
| M6 — Documentation and release | Update user/install guides, version metadata, package scripts, CI, and release notes; build all seven packages. | M1–M5 | All automated and required manual gates in the validation plan are recorded and pass. |

Every implementation milestone should be its own PR labeled `v1.0.2`, referencing
this plan and recording which F01–F10 acceptance criteria it satisfies. Reuse
`MacDesktop/MainWindow.cs` for Linux rather than maintaining another UI fork.
Dependencies determine order; no calendar delivery date is promised before M0.

## Proposed user workflow

1. Create a watchlist, add BENs manually, or choose **Import BEN CSV**.
2. Review valid rows and errors, select funding years and Form 470/471, then run.
3. Review each district's success, successful-empty, error, or cancellation outcome.
4. Open the customer summary or Changes view; filter applications and select rows.
5. Export the selection, compare years, or open source-backed deadline details.
6. On Android, tap Share for a filing summary; choose the recipient app yourself.
7. Later, open saved results offline or refresh to compare against the baseline.

Proposed CSV input (UTF-8; BEN is required, other columns are optional):

```csv
BEN,Name,Tags
225495,CENIC,California
```

Years and forms are selected in the app, not inferred from arbitrary input columns.
The import preview must explain duplicates and invalid rows before any network
requests. BENs stay text; do not convert them to floating-point numbers.

## Decisions and boundaries

- Refresh on explicit request. Any startup refresh requires an opt-in preference;
  v1.0.2 does not depend on scheduled background jobs, email, or push notifications.
- Portfolio information stays local. CSV export is the initial transfer mechanism;
  no cloud account or synchronization service is introduced.
- Existing lookup eligibility and status filters must be documented. A filing
  disappearing from a certified-only query is not evidence of withdrawal or denial.
- Search filters apply to fetched structured fields. PDF text extraction and
  inferred product matching are follow-up work, not promised by F04.
- Do not infer remaining budgets from incomplete funding requests, or treat missing
  budgets, providers, dates, and statuses as zero/none. Preserve forecast labeling.
- Deadline source mappings must be checked against current official USAC material
  during M0. This plan does not specify a compliance rule or a verified deadline.
- Retain current Windows/macOS runtime requirements and Linux/Android bundling for
  this feature scope. Bundling Windows/macOS runtimes is a separate enhancement.

## Implementation checklist

- [ ] M0: Verify source contracts and implement migration/model checks.
- [ ] M1: Implement watchlists and durable offline snapshots on all interfaces.
- [ ] M2: Implement CSV batch lookup and reliable change comparison.
- [ ] M3: Implement filters, summaries, and consolidated selected exports.
- [ ] M4: Implement verified deadlines and year comparison.
- [ ] M5: Implement and physically test Android sharing.
- [ ] M6: Update documentation and pass the seven-package release gates.

See [technical design](V1_0_2_DESIGN.md) for storage and failure semantics, and
[validation and release checklist](V1_0_2_VALIDATION.md) for completion evidence.
