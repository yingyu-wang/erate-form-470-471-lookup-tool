# E-rate Form 470 / 471 Lookup Tool

<img src="Assets/ERateLookup.png" width="96" alt="E-rate Lookup icon">

A portable C#/.NET 10 desktop tool with an Android development preview for finding published certified E-rate Form 470 and Form 471 applications by BEN and funding year. Windows uses Windows Forms; macOS and Linux use Avalonia. All platforms share the lookup, export, budget, and SQLite code.

**Version 1.0.5 · MIT license · Windows x64/ARM64, macOS Arm64/x64, Linux x64/ARM64, and Android preview**

Source version 1.0.5 is not tagged and has no GitHub Release yet. Release files stay unsigned by design. Tag only after the latest nightly USAC check says `passed` or `passed after retry`.

## Download and run

Open this repository's **Releases** page and select **v1.0.2**. GitHub's automatic source ZIP contains source code; download a platform package to run the app.

| Release asset | Computer | Install first |
| --- | --- | --- |
| `ERateLookup-v1.0.2-win-x64.zip` | Windows x64 | .NET 10 Desktop Runtime, Windows x64 |
| `ERateLookup-v1.0.2-win-arm64.zip` | Windows on ARM | .NET 10 Desktop Runtime, Windows ARM64 |
| `ERateLookup-v1.0.2-macos-arm64.zip` | Apple Silicon Mac | .NET 10 Runtime, macOS Arm64 |
| `ERateLookup-v1.0.2-macos-x64.zip` | Intel Mac | .NET 10 Runtime, macOS x64 |
| `ERateLookup-v1.0.2-linux-x64.tar.gz` | Linux x64 desktop | .NET bundled; native Linux libraries required |
| `ERateLookup-v1.0.2-linux-arm64.tar.gz` | Linux ARM64 desktop | .NET bundled; native Linux libraries required |
| `ERateLookup-v1.0.2-android-preview.zip` | Android APK plus installation guide and notices | Extract the APK first |

Get the matching runtime from [Microsoft's .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Windows requires the **Desktop Runtime**. macOS requires the standard **.NET Runtime**. The SDK is needed only for development and builds. Desktop runtime files are not bundled in the Windows/macOS downloads. The Android APK and both Linux archives include their runtime.

Extract the ZIP, then run `ERateLookup.exe` on Windows or `ERateLookup.app` on macOS. Keep the Mac .app bundle intact. Mac builds target macOS 14 or later and are unsigned, non-notarized previews. For a blocked app you trust, use the per-app **Open Anyway** option in macOS Privacy & Security.

## Install on Linux

Download the matching **linux-x64** or **linux-arm64** `.tar.gz` from the release,
extract it, and run `ERateLookup` from the extracted directory. Keep all files
in that directory together. No .NET installation is required. Ubuntu 24.04 is
the native build/launch-check baseline; an X11 desktop or XWayland and native
system libraries are needed. Use **Ctrl-click** for individual row selection.

See [Linux installation and build instructions](docs/LINUX_INSTALL.md) for
architecture detection, dependencies, commands, checksums, and the optional
application-menu shortcut with the app icon.

## Install on Android

1. On your phone/tablet, open [release v1.0.2](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.2) and download **ERateLookup-v1.0.2-android-preview.zip**. Extract it in Files/your file manager to find **ERateLookup-v1.0.2-android-preview.apk**, the installation guide, and notices. `SHA256SUMS.txt` covers all platform packages.
2. Open the APK from Downloads/Files. If prompted, allow installation from the browser/file manager through **Settings → Apps → Special app access → Install unknown apps → Allow from this source**. The wording varies by device.
3. Return to the APK, tap **Install**, then **Open**. Wait for **Ready** in **E-rate Lookup**. You do not need .NET, Android Studio, or a computer to install the APK.
4. Turn off **Allow from this source** afterward if no longer needed. Keep Play Protect enabled. Device policies or developer-verification prompts may require additional steps; see the detailed guide.
5. Try **CENIC**, BEN **225495**, FY **2027**, **Form 470**. Select filings and use **Save selected CSV / Excel** to choose an export destination.

Requires **Android 8.0+** and an **ARM64** phone/tablet; x64 emulators are also included. 32-bit-only devices are unsupported. Android is a **development preview**; physical-device UI testing remains outstanding. Builds use a generated debug signing key. If a newer preview reports a signature conflict, export needed files, uninstall the previous preview, then install the new APK. Uninstalling clears the app’s local cache/settings.

See the [complete Android installation, update, troubleshooting, and USB guide](docs/ANDROID_INSTALL.md) and [Android developer guide](Android/README.md).

## Features

- Choose Form 470 or Form 471, funding year, and an exact BEN or California customer-name suggestion.
- Search names locally after two characters, with up to 15 suggestions.
- View all distinct certified applications in the published dataset, one row per application.
- View C2 budget snapshot information with forecast labeling where applicable.
- Select all, unselect all, or choose individual/range selections; export only selected filings to CSV or Excel.
- Choose Mint Glass, Midnight, or Classic themes. The app remembers your choice.
- Use bundled seed data to create the local SQLite database without an initial download.

Successful filing queries are cached for two hours by **BEN + funding year + form type**. Each successful query replaces only its own cache entry. Directory and budget snapshots refresh separately when at least 24 hours old, retaining existing records if refresh fails. USAC query failures are shown as errors rather than reported as no filings.

## v1.0.2 saved BEN portfolios

The new **Saved BENs / Watchlists** button opens named district lists, CSV imports,
selected-BEN or whole-list refresh, offline snapshots, persistent unread changes,
filters, multi-year summaries, requested-funding comparison, source-backed dates,
selected exports, and Android sharing. The October 12 v1.0.2 rebuild also lets you
select one or more partial-name search matches and choose **Add selected BENs to
watchlist**, without requiring a filing lookup or a published 2027 Form 470.
See the [portfolio user guide](docs/PORTFOLIO_GUIDE.md).

These features were merged in [implementation PR #8](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/pull/8).
Download the matching **v1.0.2** package from [the release page](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.2).
The release workflow builds and attaches all seven packages plus `SHA256SUMS.txt`; downloads appear after successful packaging.
CI passed for the merged implementation across Windows/macOS x64 and ARM64, Linux x64 and ARM64, and Android.
Hands-on desktop and physical-device testing remains outstanding.

The [implementation plan](docs/V1_0_2_PLAN.md), [technical design](docs/V1_0_2_DESIGN.md),
[source contracts](docs/V1_0_2_SOURCE_CONTRACTS.md), and
[validation checklist](docs/V1_0_2_VALIDATION.md) document scope and release gates.

## First test

Try CENIC, **BEN 225495**, **Form 470**, **FY2027**. A previous live verification returned four applications. Select two and check that CSV and Excel contain only those selected filings. Live counts may change as USAC publishes data.

## Command line

`ERateLookup.Cli` checks published Form 470 and Form 471 rows from a terminal on Windows, macOS, and Linux. It calls the same read-only USAC lookup as the desktop apps. It does not create, change, certify, or submit a filing. Flags, samples, output formats, exit codes, and build steps are in the [1.0.5 command-line guide](docs/CLI.md). Windows, macOS, and Linux release assets include the CLI starting with v1.0.4. v1.0.5 packages are not published until that version is tagged.

```sh
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --ben 143662 --fy 2027
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --name "San Diego City" --fy 2027 --csv --output sdusd.csv
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form both --ben 143662 --fy 2027 --json
dotnet run --project Cli/ERateLookup.Cli.csproj -- --form 470 --ben 16057656 --fy 2027 --app 270000266 --diff
```

`--diff` compares published Form 470 Original and Current rows from USAC Open Data dataset `jt8s-3q52`. It does not store a local history of intermediate revisions, and it does not determine whether a change was substantial. Search by BEN, by the published applicant name, or by both. `--form both` lists Form 470 and Form 471 for that applicant and funding year. A name-only link is printed only when every row shares one published BEN. A published Form 470 does not establish a Form 471. Every table, CSV, and JSON row includes a USAC Open Data source link. The published 1.0.4 CLI already writes JSON (`--json`) and CSV (`--csv`). Version 1.0.5 keeps both. CSV rows also include `Linked`, `Form470Count`, and `Form471Count`. The default output is a text table. `--output` writes the body to a file. `--help` prints the full usage text.

The San Diego Unified School District RFP calls the applicant "San Diego Unified." In the bundled California directory, USAC publishes that district as **San Diego City Unif Sch Dist**, BEN **143662**. Name search matches the published name. It does not translate an RFP title into a BEN.

| Exit code | Meaning |
| --- | --- |
| 0 | At least one published row matched |
| 1 | Invalid arguments |
| 2 | The lookup succeeded and nothing matched |
| 3 | USAC could not be reached |
| 4 | The lookup failed for another reason |

An empty result is not proof that no filing exists. A published FY2027 Form 470 for BEN 143618 was not in USAC open data when this command was added; FY2026 for that BEN did return certified rows.

## Build from source

Install the .NET 10 SDK. Open this folder in VS Code or open `ERateLookup.sln` in Visual Studio.

```sh
dotnet test tests/ERateLookup.Tests/ERateLookup.Tests.csproj -c Release --filter Category!=Live
```

On Windows, use `Run-Windows.bat` to launch from source or `Publish-Windows.bat` to create the framework-dependent EXE under `publish/win-x64-framework`. For Windows on ARM, use `Publish-Windows-ARM64.bat`; its native ARM64 executable is written to `publish/win-arm64-framework`. See [Windows ARM64 instructions](docs/WINDOWS_ARM64.md).

On macOS, install Python 3 as well, then run:

```sh
bash Publish-Mac.sh osx-arm64
# Intel:
bash Publish-Mac.sh osx-x64
```

### Compile Linux packages

Install .NET SDK 10 and Python 3, then run:

```bash
bash Publish-Linux.sh linux-x64
bash Publish-Linux.sh linux-arm64
```

Self-contained archives appear under `artifacts/`. GitHub Actions builds and
launch-checks each package on its matching Ubuntu 24.04 x64/ARM64 runner.
See [Linux instructions](docs/LINUX_INSTALL.md).

### Compile the Android APK

Install **.NET SDK 10.0.401**, **JDK 21**, and the **Android SDK platform 36 / Build Tools 36.0.0** (Android Studio’s SDK Manager can install the SDK components). Run these commands from the repository root. Replace both absolute paths with your SDK/JDK locations; on Windows, use paths such as `C:/Android/Sdk` and your JDK 21 installation folder.

```sh
dotnet workload install android --version 10.0.401
dotnet build Android/ERateLookup.Android.csproj -t:InstallAndroidDependencies -p:AndroidSdkDirectory="/absolute/path/android-sdk" -p:JavaSdkDirectory="/absolute/path/jdk-21" -p:AcceptAndroidSDKLicenses=True
dotnet test tests/ERateLookup.Tests/ERateLookup.Tests.csproj -c Release --filter Category!=Live
python -m unittest discover -s scripts -p test_verify_android_apk.py -v
dotnet publish Android/ERateLookup.Android.csproj -c Release -p:AndroidSdkDirectory="/absolute/path/android-sdk" -p:JavaSdkDirectory="/absolute/path/jdk-21" -o publish/android
```

The dependency command accepts Android SDK licenses; read the terms before using it. Python 3 is required for the verifier regression tests. Install the **`*-Signed.apk`** from `publish/android/`; it bundles .NET and SQLite for ARM64/x64. The default local build uses a debug signing key. For stable updates, follow the [private release-key instructions](Android/README.md#signing-stable-updates). Keep keystores/passwords out of git.

For cloud builds, run the **Android preview APK** workflow in GitHub Actions and download its **ERateLookup-Android-preview** artifact. Tagged releases also build and attach the Android preview package ZIP.

## GitHub Actions and releases

The workflow builds on pushes and pull requests to `main`. Pushing a `v*` tag builds Windows, macOS, and Linux x64/ARM64 packages plus the Android preview, runs the shared/security checks, and attaches the Windows/macOS ZIPs, Linux tar.gz archives, Android package ZIP, and aggregate `SHA256SUMS.txt` to a GitHub Release.

Tagged release runs use one repository-wide build slot, so a second release waits
until the active release run finishes. The platform jobs within one release still
run in parallel. Every publisher (tagged release, existing-CI recovery, and the
historical Linux updater) also shares a publishing lock for its target tag.
Active runs are never canceled by these locks. GitHub retains one pending run
per group; a newer request can replace an older pending request. These safeguards apply to workflow definitions containing the fix;
rerunning an older tag uses that tag's older workflow. Use the current recovery
publisher when updating an older release.

```sh
git add .
git commit -m "Describe the change"
git push origin main
# Choose an unused version for the next release:
git tag v1.0.3
git push origin v1.0.3
```

To rebuild an existing version from updated source, run **Windows build and
release** on `main`, supply `release_tag` (for example `v1.0.2`), and supply the
current lightweight tag's commit SHA as `expected_tag_sha`. Package metadata must
match that version. After all platform builds and checks succeed, the
`align-release-tag` job waits for approval in the `release-tag-move` environment
and then moves the lightweight tag onto the build commit. The publisher checks
that the tag already points at that commit, replaces the seven archives, and
regenerates `SHA256SUMS.txt`. The release notes record both commit SHAs. Create
the environment and required reviewers before the first rebuild; see
[GITHUB_SETUP.md](GITHUB_SETUP.md). Leave `release_tag` blank to generate build
artifacts without changing a release. Nightly live USAC checks run separately
and do not publish.

The historical **Publish Linux to v1.0.1** workflow added Linux packages to v1.0.1. Starting with v1.0.2, tagged release automation builds all seven packages from the same tagged source.

Keep executables, package archives, caches, and user settings out of the source repository; `.gitignore` excludes them. Compiled files belong in Releases. Each package includes instructions, the MIT license, and third-party notices.

## Project layout

| Path | Purpose |
| --- | --- |
| `Core/` | USAC queries, cache, seed data, budget display logic, and exports |
| `Cli/` | Windows, macOS, and Linux command-line lookup |
| `Cli.Tests/` | Offline CLI argument and output tests |
| `Desktop/` | Windows Forms interface |
| `MacDesktop/` | Avalonia interface shared by macOS and Linux |
| `LinuxDesktop/` | Linux entry point, icon, launch check, and shortcut helper |
| `Android/` | Native Android development preview |
| `Assets/` | PNG, ICO, and ICNS app icon |
| `tests/` | xUnit projects. Live USAC cases run only when `ERATE_LIVE_USAC=1`, including the nightly workflow |
| `scripts/package_mac.py` | Creates Mac app bundles and ZIP files |
| `Publish-Linux.sh` / `scripts/package_linux.py` | Creates self-contained Linux tar.gz archives |
| `.github/workflows/linux.yml` | Native Linux x64/ARM64 builds and window launch checks |
| `.github/workflows/linux-release.yml` | Adds Linux packages to the existing v1.0.1 release |
| `docs/LINUX_INSTALL.md` | Linux installation, dependencies, shortcuts, and compilation |
| `.github/workflows/windows.yml` | All-platform tagged release automation |
| `.github/workflows/android.yml` | Android APK build and package verification |
| `.github/workflows/cli.yml` | CLI tests and publish on Windows, macOS, and Linux |
| `docs/CLI.md` | Command-line install, flags, output formats, and exit codes |
| `docs/ANDROID_INSTALL.md` | Android installation, updates, exports, and troubleshooting |

## Current limitations

GUI launch and appearance still require hands-on Windows and Mac testing. Linux CI validates native window launch on x64 and ARM64; interactive lookup, selection, export, and browser opening still require hands-on desktop testing. Packages are unsigned. The funding-year selector currently covers 2016–2027; local name suggestions cover California. USAC data publishing delays can affect filing results. Budget figures are snapshot information, not a substitute for USAC's authoritative records. This project is independent of USAC.

See [Windows user guide](docs/USER_GUIDE.md), [1.0.5 command-line guide](docs/CLI.md), [Mac instructions](MACOS_README.md), [validation record](VALIDATION.md), and [release notes](RELEASE_NOTES.md).

## License

Original project code and documentation are available under the [MIT License](LICENSE). Third-party components and USAC data retain their own terms; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Contributors

See [CONTRIBUTORS.md](CONTRIBUTORS.md), including credit for Codex (OpenAI), the AI coding assistant used during development.
