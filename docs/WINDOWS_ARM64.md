# Windows ARM64 portable app

Download `ERateLookup-v1.0.7-win-arm64.zip` from the [latest release](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/latest). Install the **.NET 10 Desktop Runtime for Windows ARM64** from https://dotnet.microsoft.com/en-us/download/dotnet/10.0, extract the ZIP, and open `ERateLookup.exe`. No app installer or administrator access is required for the extracted application. The runtime is a separate prerequisite.

This package targets native Windows ARM64 (for example, a Snapdragon Windows PC). It does not run on Windows x64 or x86. Existing Windows x64 downloads remain available. The source is MIT licensed; third-party notices are included.

## Build

Install the .NET 10 SDK and run `Publish-Windows-ARM64.bat` on Windows, or run:

```sh
dotnet publish Desktop/ERateLookup.csproj -c Release -r win-arm64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-arm64-framework
```

That command writes `publish/win-arm64-framework/ERateLookup.exe`. The executable bundles application assemblies, seed data and native SQLite. The .NET Desktop Runtime is not bundled. Windows and macOS packages share the same lookup/export core.

## Verification

The ARM64 package is cross-published and checked for ARM64 executable architecture, ZIP integrity and native SQLite inclusion. Shared offline checks cover lookup, caching, budgets and export behavior. A physical Windows ARM64 GUI launch and live query are still required; this package is unsigned.

## Saved BENs / Watchlists

Open **Saved BENs / Watchlists** to save favorite BENs, refresh a list, import CSV,
review changes, and reopen results offline. See [the portfolio guide](PORTFOLIO_GUIDE.md).
These features are included in current releases.
