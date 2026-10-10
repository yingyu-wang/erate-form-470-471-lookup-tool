# E-rate Lookup for iPhone and iPad — development preview

Native UIKit interface in C#, targeting iOS 16+. Reuses `Core.csproj` unchanged: USAC Form 470/471 queries, embedded California entities and C2 snapshots, SQLite cache, filing-window notices, CSV and Excel export. This project is deliberately outside the desktop solution so Windows builds do not require an Apple workload.

## Status

Source port prepared. A successful Apple toolchain build and simulator/device launch are required before calling this an installable release. The workflow builds a simulator `.app`, not an iPhone-installable IPA. App Store distribution is not configured. No signing certificates or Apple credentials are stored in this repository.

## Interface

- Search CA uses bundled data; Search US queries published Form 470 entity records nationwide.
- Select an entity or enter a numeric BEN, funding year, and Form 470/471.
- Look up allows fresh cached queries; Refresh filings bypasses the query cache.
- C2 data is California-only, with snapshot dates and forecast labels.
- Tap rows to select filings; the information button opens details and an HTTPS PDF link.
- The share button exports selected rows to Excel or CSV through the iOS share sheet. Empty successful searches export an explicit no-results record.
- Refresh CA data replaces the directory only after successful downloads. Network failures retain existing data.
- Directory and C2 snapshots work offline. Filing lookup only works offline when its query is already cached and within the shared core's two-hour TTL. It does not pre-load every filing.

## Build on a Mac

Install .NET SDK 10.0.401 and Xcode 26.6 (requires macOS 26.2+), then select that Xcode and accept its license. These versions match Microsoft's published workload set; update them together if changing the toolchain.

```sh
dotnet workload install ios --version 10.0.401
dotnet run --project Checks/Checks.csproj -c Release
dotnet build iOS/ERateLookup.iOS.csproj -c Debug -r iossimulator-arm64
```

Use `iossimulator-x64` for an Intel Mac with a supported Xcode/macOS installation. No separate .NET runtime is installed on the phone.

To install on a booted compatible simulator:

```sh
xcrun simctl install booted iOS/bin/Debug/net10.0-ios/iossimulator-arm64/ERateLookup.app
xcrun simctl launch booted com.yingyuwang.eratelookup
```

## Test on your own iPhone for free

Developer Mode permits development-signed apps; it does **not** permit arbitrary unsigned apps.

1. Sign into Xcode with your Apple Account; select your Personal Team. Pair your unlocked iPhone with the Mac and trust the computer.
2. Enable Settings → Privacy & Security → Developer Mode on the phone, restart, unlock, and confirm. Pairing with Xcode exposes the option if it is missing.
3. Let Xcode create an Apple Development identity and a provisioning profile for your team, device, and a unique bundle identifier. For this .NET project, a temporary Xcode iOS project with the same bundle identifier can establish the profile. Make sure `ApplicationId` here matches it.
4. Build the .NET app with those signing values:

```sh
dotnet build iOS/ERateLookup.iOS.csproj -c Debug -r ios-arm64 \
  -p:CodesignKey='Apple Development: YOUR IDENTITY' \
  -p:CodesignProvision='YOUR PROFILE NAME OR UUID'
```

5. Use Xcode's device tools to install the signed build. If prompted, trust the developer under Settings → General → VPN & Device Management.

Personal Team provisioning expires after seven days; rebuild/re-provision to continue testing. A spare phone works, but it follows the same signing requirements.

## Free App Store app

Free download pricing does not waive developer membership. Standard Apple Developer Program membership is USD 99/year; eligible organizations may qualify for a fee waiver. Membership also enables TestFlight distribution.

1. Enroll, register the final bundle identifier, and create the app record in App Store Connect.
2. Configure distribution signing and create a Release device archive using the .NET iOS toolchain. Keep private keys and profiles out of git.
3. Upload the signed build with Apple's supported upload tools, such as Transporter; test it through TestFlight.
4. Provide screenshots from the real app, description, support URL, privacy-policy URL, age rating, export-compliance answers, and accurate privacy disclosures. Set the price to free.
5. Select the build, submit for App Review, and release after approval. There is no submission in this project and approval is not guaranteed.

Before submission, review bundled third-party privacy manifests and required-reason API declarations, test the Release AOT build on a physical device, and prepare the real privacy policy. The app talks directly to USAC and opens its external PDF URLs; do not claim no data collection without reviewing the complete data flow and third-party policies.

## Verification checklist

- First launch offline: CA search and snapshot labels, SQLite native loading, no crash.
- CENIC BEN 225495 / FY2027 / Form 470: compare to current USAC results (counts may change).
- Form 471 queries, budget forecast labels, missing budgets, empty results, malformed BEN/year.
- Airplane mode, cancellation, rate limits; ensure failed queries never enable export as successful no-results.
- Change BEN/form/year: invalidate previous results and exports.
- Multi-select, Excel/CSV share to Files, iPad popovers, PDF opening.
- iPhone/iPad rotation, large text, VoiceOver, light/dark appearance.
- Release device build: AOT and SQLite/JSON runtime behavior.

## Official references

- https://developer.apple.com/support/compare-memberships/
- https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device
- https://developer.apple.com/help/app-store-connect/manage-submissions-to-app-review/submit-an-app/
- https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode26.5-10318

MIT license and third-party/data notices from the repository root apply.
