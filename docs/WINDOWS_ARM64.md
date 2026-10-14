# Windows ARM64 portable app — version 1.0.1

Download `ERateLookup-v1.0.1-win-arm64.zip` from the v1.0.1 release. Install the **.NET 10 Desktop Runtime for Windows ARM64** from https://dotnet.microsoft.com/en-us/download/dotnet/10.0, extract the ZIP, and open `ERate470Lookup.exe`. No app installer or administrator access is required for the extracted application. The runtime is a separate prerequisite.

This package targets native Windows ARM64 (for example, a Snapdragon Windows PC). It does not run on Windows x64 or x86. Existing Windows x64 downloads remain available. Application version is 1.0.1 and source is MIT licensed; third-party notices are included.

## Build

Install the .NET 10 SDK and run `Publish-Windows-ARM64.bat` on Windows, or run:

```sh
dotnet publish Desktop/ERateLookup.csproj -c Release -r win-arm64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-arm64-framework
```

That command writes `publish/win-arm64-framework/ERateLookup.exe`. The published v1.0.1 archive still contains `ERate470Lookup.exe`. The executable bundles application assemblies, seed data and native SQLite. The .NET Desktop Runtime is not bundled. Windows and macOS packages share the same lookup/export core.

## Verification

The ARM64 package is cross-published and checked for ARM64 executable architecture, ZIP integrity and native SQLite inclusion. Shared offline checks cover lookup, caching, budgets and export behavior. A physical Windows ARM64 GUI launch and live query are still required; this package is unsigned.

## v1.0.2 development: saved BENs

Open **Saved BENs / Watchlists** to save favorite BENs, refresh a list, import CSV,
review changes, and reopen results offline. See [the portfolio guide](PORTFOLIO_GUIDE.md).
These features require the unreleased v1.0.2 build from a successful PR #8 Actions run.
