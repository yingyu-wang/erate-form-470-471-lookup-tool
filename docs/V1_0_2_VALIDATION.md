# v1.0.2 validation and release checklist

Status: **implementation PR #8; automated validation in progress, manual gates pending**.

The initial CI commit built Windows and macOS on x64/ARM64 and passed native Linux
x64/ARM64 window launches. Android initially failed an ambiguous exception-name
compile check; the alias was corrected in the subsequent commit. Consult the
latest PR checks for the current head; earlier successes are not final-head evidence.
Local shared checks cover the new storage, import, batch, changes, and export
semantics. Physical Android and interactive desktop verification remain pending.
See the [feature acceptance criteria](V1_0_2_PLAN.md) and
[technical design](V1_0_2_DESIGN.md). Record commit, platform, result, and remaining
limitations in the implementing PR. The planning PR needs document/link review;
it does not require rebuilding the unchanged app.

## Automated checks to add with implementation

Use deterministic fixtures, an injected clock, fake HTTP responses, and temporary
databases in `tests/ERateLookup.Tests`. Keep existing shared and Android security checks passing.
Routine tests must not depend on live USAC data or a particular filing count.

| Area | Required scenarios |
| --- | --- |
| Migration and storage (F01/F08) | Fresh database; upgrade existing user data; failed migration rollback; future-schema refusal; duplicate membership; restart persistence; removal without unintended history loss. |
| CSV import (F02) | BOM, quoted comma/newline, escaped quote, empty/missing BEN, duplicate headers/BENs, ASCII-digit validation, text BEN preservation, file/field/query limits, cancellation. |
| Batch (F02) | Multiple years/forms; mixed success/empty/error; 429 Retry-After; transient retry limit; cancellation during request and wait; untouched prior snapshot for unfinished queries; responsive progress. |
| Changes (F03) | First observation; new/changed/missing application; service reorder unchanged; duplicate service rows; form/year isolation; repeated refresh idempotence; acknowledged event behavior; failed/partial/cached results cannot advance baseline; an older overlapping request finishing last cannot regress a newer baseline. |
| Filtering and selection (F04/F09) | Literal keyword/case handling; AND filters; empty view; hidden selection count; Select visible; selection across pages/years; exact selected export identities. |
| Summary and history (F06/F07) | Section-specific freshness; optional error isolation; missing vs zero; forecast budgets; no duplicate application/FRN totals; amount-type separation; unavailable year/provider fields. |
| Deadlines (F05) | Missing/moved source; wrong-year text; source-date vs instant; timezone/DST boundary; previously verified stale metadata; no inference from inadequate timestamps. |
| Offline/export (F08/F09) | Expired saved data available offline; no saved data; old cache not silently promoted; CSV formula escaping and Unicode; Excel XML validity; cancellation during generation/copy; incomplete destination handling. |
| Android lifecycle/share (F10) | Summary formatting/link validation; no link; no receiver; chooser cancellation; rotation/backgrounding; no automatic sending; lookup state preserved. |

Developer commands after implementation:

```sh
dotnet test tests/ERateLookup.Tests/ERateLookup.Tests.csproj -c Release --filter Category!=Live
python -m unittest discover -s scripts -p test_verify_android_apk.py -v
```

Use the existing workflows for native package builds and Linux window-launch
checks. Live checks verify current dataset mappings and source-backed dates, not
replace deterministic regression tests. Record the official source URLs, field
definitions, verification date, and any observed API limits during M0.

## Manual platform checks

| Target | Required hands-on evidence |
| --- | --- |
| Windows x64 and ARM64 | Launch the correct native build; upgrade existing local data; import/run/cancel; keyboard/range selection; save/export; open source links; offline restart. |
| macOS Apple Silicon and Intel | Same portfolio flows; Command selection; save dialog and bundle integrity; supported OS/runtime requirements; high-DPI layout. |
| Linux x64 and ARM64 | Native launch plus portfolio flows; Ctrl selection; file dialogs; X11/XWayland; shortcut and browser links; native library requirements. |
| Android ARM64 physical device | Fresh install/update path; Files CSV picker; rotation during work; paging/selection; export destination cancellation; airplane-mode saved view; share chooser; large text and TalkBack. |
| Android x64 emulator | Build/start/lifecycle regression coverage; not a substitute for the physical ARM64 checks. |

Test BEN 225495 as a smoke lookup, but compare against current source data rather
than asserting a historical application count. Include a failed and an empty
lookup in the same watchlist; check that neither is misrepresented as the other.
For stress checks, use synthetic data at the configured input/query limits and
5,001 applications; verify bounded rendering, responsive cancellation, and export
selection across all pages. Record elapsed time and memory on the tested hardware.

## Documentation required before release

- [ ] Update `README.md` Features and the seven-package Download section.
- [ ] Update `docs/USER_GUIDE.md` with watchlists, CSV schema, changes, filters,
      deadlines, history, summary, selection scope, and offline limitations.
- [ ] Update `MACOS_README.md`, `docs/LINUX_INSTALL.md`, and Windows ARM64 guidance
      where platform actions or prerequisites differ.
- [ ] Update `docs/ANDROID_INSTALL.md` and `Android/README.md` with CSV picker,
      sharing, offline data, upgrade/signature behavior, build and install steps.
- [ ] Document data retention, clear-history behavior, and migration compatibility.
- [ ] Update `VALIDATION.md` with actual results and `RELEASE_NOTES.md` with shipped
      behavior; do not describe unimplemented milestones as complete.
- [ ] Include MIT license, third-party notices, and contributor credit as appropriate
      in package contents; review any new dependency's license.

## Release gate

- [ ] Every F01–F10 acceptance criterion is implemented and verified; scope changes
      are explicitly documented before calling the release complete.
- [ ] All applicable CI checks pass; blocking review/security findings are resolved.
- [ ] Required hands-on tests above are recorded; document remaining preview limits.
- [ ] All app metadata says 1.0.2 and Android version code is greater than 111.
- [ ] Linux smoke-check paths derive the version instead of assuming v1.0.1.
- [ ] Build from the final reviewed commit and publish an unused `v1.0.2` tag.
- [ ] Verify exactly these seven package archives plus aggregate `SHA256SUMS.txt`
      (GitHub's automatic source archives are separate):

```text
ERateLookup-v1.0.2-win-x64.zip
ERateLookup-v1.0.2-win-arm64.zip
ERateLookup-v1.0.2-macos-arm64.zip
ERateLookup-v1.0.2-macos-x64.zip
ERateLookup-v1.0.2-linux-x64.tar.gz
ERateLookup-v1.0.2-linux-arm64.tar.gz
ERateLookup-v1.0.2-android-preview.zip
```

- [ ] Download and extract each package; confirm versions, instructions, notices,
      correct architecture/runtime mode, and Android APK inside its ZIP.
- [ ] Independently compare each archive's SHA-256 with the aggregate file.
- [ ] Release-page Downloads lists and links all seven packages with accurate
      installation/runtime requirements and the final source commit.

The planning PR does not bump binaries, create a tag, publish assets, or merge
itself. Implementation PRs and the final release provide the completion evidence.
