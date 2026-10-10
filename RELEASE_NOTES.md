# v1.0.0 — E-rate Form 470 / 471 Lookup Tool

Initial open-source release for Windows x64, macOS Apple Silicon, and macOS Intel.

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
| ERateLookup-v1.0.0-win-x64.zip | Windows x64 | .NET 10 Desktop Runtime x64 |
| ERateLookup-v1.0.0-macos-arm64.zip | macOS Apple Silicon | .NET 10 Runtime Arm64 |
| ERateLookup-v1.0.0-macos-x64.zip | macOS Intel | .NET 10 Runtime x64 |

Install the matching runtime from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), extract the ZIP, and run the EXE or .app. The runtime is not bundled. SHA256SUMS.txt provides checksums for the three ZIP files.

## Validation and limitations

Windows publish succeeded. The icon payloads were verified in the Windows EXE. macOS icon registration, ZIP integrity, and executable permissions were checked. These GUI builds have not been launched on Windows or macOS in this build environment; hands-on launch, selection, export, and layout testing is still required. Packages are unsigned; macOS packages are not notarized. The funding-year selector currently covers 2016–2027. Local suggestions cover California; USAC publishing delays and API errors can affect results.
