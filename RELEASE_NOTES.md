# v1.0.1 — E-rate Form 470 / 471 Lookup Tool

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
