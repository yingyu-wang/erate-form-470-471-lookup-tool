# E-rate Lookup for macOS — preview build

This app reuses the same C# Core project as the Windows app: live USAC Form 470/471 queries, the local California directory, C2 budgets, SQLite two-hour filing cache, 24-hour directory refresh, filing-window schedule, and CSV/Excel writer. The Windows Forms interface is replaced with an Avalonia interface in MacDesktop; the Windows project remains available in Desktop.

## Run the Mac app

1. Use a supported macOS version: **macOS 14 Sonoma or later** for .NET 10.
2. Install the **.NET 10 Runtime** matching your Mac from https://dotnet.microsoft.com/en-us/download/dotnet/10.0 . Select macOS **Arm64** for Apple Silicon (M1/M2/M3/M4 and later) or **x64** for Intel. Use the standard .NET Runtime, not Windows Desktop Runtime. The matching .NET 10 SDK also satisfies this prerequisite.
3. Download the matching ZIP: `ERateLookup-macOS-osx-arm64.zip` or `ERateLookup-macOS-osx-x64.zip`.
4. Extract it and open **ERateLookup.app**. The app itself has no installer and may be moved to Applications or another folder. Keep the .app bundle intact; its DLLs and native libraries are inside it.
5. Enter a BEN or type at least two name characters for local California suggestions. Select Form 470/471 and funding year. Names never query USAC per keystroke. Check a selected customer to retrieve its filings.
6. All filing rows start selected. Use **Command-click** for individual selections or **Shift-click** for a range, or Select all / Unselect all. CSV and Excel export only selected filings. PDFs open in the browser when available.

This is a compiled, unsigned, non-notarized preview, not a Mac-tested release. macOS may block a downloaded app. If you trust this project, use macOS's per-app Open Anyway option in System Settings > Privacy & Security after attempting to open it. Do not disable Gatekeeper globally. For normal broad distribution, sign with an Apple Developer ID and notarize the app on macOS. Those credentials are not included.

The .NET runtime is not bundled, consistent with the smaller Windows package. An absent or wrong-architecture runtime prevents launch. For diagnostic messages, run the executable from Terminal:

```bash
/path/to/ERateLookup.app/Contents/MacOS/ERateLookup
```

If an unzip tool lost execution permission, restore only the main executable's permission:

```bash
chmod +x /path/to/ERateLookup.app/Contents/MacOS/ERateLookup
```

## Local database and settings

The app creates `~/Library/Application Support/ERateLookup/cache/cache.sqlite3` from embedded California seed data on first run. Entity suggestions, budgets and separate 470/471 query-cache entries share this file. It attempts a directory refresh when the snapshot is at least 24 hours old, retaining old data if refresh fails. Successful filing queries, including empty responses, update only their own BEN/year/form cache, with a two-hour TTL and a Refresh live override. Theme choice is saved in the parent folder's theme.txt. macOS and Windows store separate per-machine databases; they do not synchronize automatically.

## Build from the same source repository

Install the .NET 10 SDK and Python 3. In Terminal, from the folder containing Core and MacDesktop:

```bash
dotnet run --project MacDesktop/MacDesktop.csproj
dotnet run --project Checks/Checks.csproj -c Release
bash Publish-Mac.sh osx-arm64
# For an Intel build instead:
bash Publish-Mac.sh osx-x64
```

Publish-Mac.sh publishes a framework-dependent app, creates Info.plist and the .app bundle, and writes the architecture-specific ZIP next to the source directory. Native macOS bundles require their supporting files inside Contents/MacOS. Signing and notarization are a separate distribution step on a Mac.

## Verify on a Mac

The shared Core tests pass in the build environment. Both Mac architectures are cross-compiled; GUI launch, Command-click selection, theme appearance, native save dialogs, PDF opening, SQLite native loading and offline behavior must be verified on actual macOS. Try CENIC BEN 225495: FY2027 Form 470 has four applications in the recorded live test, and FY2026 Form 471 has 85. Select two and verify that Excel contains only those two application IDs.

## Application icon

The Mint Glass document/search icon is included in `Assets` as PNG, Windows ICO, and macOS ICNS. Windows builds embed the ICO in the executable and main window. The Mac packaging script copies the ICNS into the app bundle and registers it in Info.plist. No additional runtime dependency is required.
