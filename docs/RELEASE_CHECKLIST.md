# Checklist before tagging a version

Complete this list before pushing a `v*` tag or running a rebuild that sets `release_tag`. This file describes the branch that contains it. It does not sign files, move a tag, or publish a release.

## 1. Unsigned release files

Release archives are unsigned by design. This branch does not add a signing step, a certificate, or a notarization step for the Windows, macOS, or Linux packages.

- [ ] Confirm [RELEASE_NOTES.md](../RELEASE_NOTES.md) for the version being tagged still says the packages are unsigned. The current v1.0.2 notes say: "Windows and macOS packages are unsigned; macOS packages are not notarized."
- [ ] The Android preview note is separate: the notes say that preview uses a generated debug signing key, not a publisher release signature.

## 2. Write permission and the tag-move approval

Workflow **Windows build and release** (`.github/workflows/windows.yml`) sets `contents: read` for the workflow. `contents: write` in that file is only on these jobs:

- `align-release-tag` — the only job that force-moves a lightweight tag. It runs only when `release_tag` is supplied, and it uses the GitHub environment `release-tag-move`.
- `release` — publishes the archives. It checks that the tag already points at the build commit and does not move the tag.

Two other publish workflows also grant `contents: write` on their publish job, and neither moves a tag:

- **Publish existing CI release packages** (`.github/workflows/publish-existing-release.yml`), job `publish`
- **Publish Linux to v1.0.1** (`.github/workflows/linux-release.yml`), job `publish-existing-release`

- [ ] Confirm `align-release-tag` is still the only tag-moving job and still names environment `release-tag-move`.
- [ ] Confirm the GitHub environment `release-tag-move` exists, requires a reviewer, and is limited to `main`, before anyone runs a rebuild with `release_tag`. Setup steps are in [GITHUB_SETUP.md](../GITHUB_SETUP.md).

## 3. Nightly live USAC check

Workflow **Nightly live USAC checks** (`.github/workflows/nightly-usac.yml`), job `live`, step **Live published-filing checks**, runs `scripts/nightly-live-usac.sh`. The step summary is one line and includes the attempt count.

- [ ] Open the latest completed run of that workflow and confirm the summary says `passed` or `passed after retry`.
- [ ] Do not tag while that summary says `skipped due to USAC unreachable`.

## 4. Form 471 BEN check

Every Form 471 lookup calls `Form471Identity.Validate` from `UsacClient.GetFilingsAsync` (`Core/UsacClient.cs`). `Form471Identity` (`Core/Form471Identity.cs`) requires `epc_organization_id` to equal the requested BEN. A `billed_entity_number` on the row, when present, must equal that id. A missing id throws `Form471IdentityMismatchException` instead of an empty success. The same check runs for a cache hit.

- [ ] Confirm these tests in `tests/ERateLookup.Tests/FundingYearAndIdentityTests.cs` still pass: `MatchingEpcOrganizationIdIsAccepted`, `MismatchedEpcOrganizationIdIsNotAnEmptyLookup`, `ReportedBenThatDiffersFromEpcIsRejected`, and `MissingEpcOrganizationIdIsRejectedOnEveryLookupIncludingCache`.

## 5. Funding year from the date

`ERateCalendar.CurrentFundingYear` (`Core/ERateCalendar.cs`) uses the injected clock's date. July through December are funding year N; January through June are funding year N−1. `UsacClient.NewCycleFundingYears` uses that clock for the new-cycle Form 470 query. The years are not hard-coded `2026` and `2027`.

- [ ] Confirm `FundingYearStartsOnJuly1` and `NewCycleQueryUsesClockInsteadOfHardCodedYears` in `tests/ERateLookup.Tests/FundingYearAndIdentityTests.cs` still pass.
