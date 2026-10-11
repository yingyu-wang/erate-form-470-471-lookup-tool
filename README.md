# E-rate Form 470 / 471 Lookup Tool

<img src="Assets/ERateLookup.png" width="96" alt="E-rate Lookup icon">

A portable C#/.NET 10 desktop tool with an Android development preview for finding published certified E-rate Form 470 and Form 471 applications by BEN and funding year. Windows uses Windows Forms; macOS and Linux use Avalonia. All platforms share the lookup, export, budget, and SQLite code.

**Version 1.0.1 · MIT license · Windows x64/ARM64, macOS Arm64/x64, Linux x64/ARM64, and Android preview**

## Download and run

Open this repository's **Releases** page and select **v1.0.1**. GitHub's automatic source ZIP contains source code; download a platform package to run the app.

| Release asset | Computer | Install first |
| --- | --- | --- |
| `ERateLookup-v1.0.1-win-x64.zip` | Windows x64 | .NET 10 Desktop Runtime, Windows x64 |
| `ERateLookup-v1.0.1-win-arm64.zip` | Windows on ARM | .NET 10 Desktop Runtime, Windows ARM64 |
| `ERateLookup-v1.0.1-macos-arm64.zip` | Apple Silicon Mac | .NET 10 Runtime, macOS Arm64 |
| `ERateLookup-v1.0.1-macos-x64.zip` | Intel Mac | .NET 10 Runtime, macOS x64 |
| `ERateLookup-v1.0.1-linux-x64.tar.gz` | Linux x64 desktop | .NET bundled; native Linux libraries required |
| `ERateLookup-v1.0.1-linux-arm64.tar.gz` | Linux ARM64 desktop | .NET bundled; native Linux libraries required |
| `ERateLookup-v1.0.1-android-preview.zip` | Android APK plus installation guide and notices | Extract the APK first |

Get the matching runtime from [Microsoft's .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Windows requires the **Desktop Runtime**. macOS requires the standard **.NET Runtime**. The SDK is needed only for development and builds. Desktop runtime files are not bundled in the Windows/macOS downloads. The Android APK and both Linux archives include their runtime.

Extract the ZIP, then run `ERate470Lookup.exe` on Windows or `ERateLookup.app` on macOS. Keep the Mac .app bundle intact. Mac builds target macOS 14 or later and are unsigned, non-notarized previews. For a blocked app you trust, use the per-app **Open Anyway** option in macOS Privacy & Security.

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

1. On your phone/tablet, open [release v1.0.1](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.1) and download **ERateLookup-v1.0.1-android-preview.zip**. Extract it in Files/your file manager to find **ERateLookup-v1.0.1-android-preview.apk**, the installation guide, and notices. `SHA256SUMS.txt` covers all platform packages.
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

## Planned v1.0.2 work

The [v1.0.2 implementation plan](docs/V1_0_2_PLAN.md) proposes saved district
watchlists, CSV batch lookup, change highlighting, service/keyword filters,
source-backed deadlines, district history, customer summaries, offline saved
results, expanded selected-result exports, and Android sharing. These are planned
features; the published app remains v1.0.1.

Work starts with shared data contracts, then watchlists/offline snapshots, batch
lookup/change tracking, review/export tools, history/deadlines, Android sharing,
and seven-platform-package validation. See the [technical design](docs/V1_0_2_DESIGN.md)
and [validation/release checklist](docs/V1_0_2_VALIDATION.md) for dependencies,
acceptance criteria, testing, and documentation requirements.

## First test

Try CENIC, **BEN 225495**, **Form 470**, **FY2027**. A previous live verification returned four applications. Select two and check that CSV and Excel contain only those selected filings. Live counts may change as USAC publishes data.

## Build from source

Install the .NET 10 SDK. Open this folder in VS Code or open `ERate470Lookup.sln` in Visual Studio.

```sh
dotnet run --project Checks/Checks.csproj -c Release
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
dotnet run --project Checks/Checks.csproj -c Release
python -m unittest discover -s scripts -p test_verify_android_apk.py -v
dotnet publish Android/ERateLookup.Android.csproj -c Release -p:AndroidSdkDirectory="/absolute/path/android-sdk" -p:JavaSdkDirectory="/absolute/path/jdk-21" -o publish/android
```

The dependency command accepts Android SDK licenses; read the terms before using it. Python 3 is required for the verifier regression tests. Install the **`*-Signed.apk`** from `publish/android/`; it bundles .NET and SQLite for ARM64/x64. The default local build uses a debug signing key. For stable updates, follow the [private release-key instructions](Android/README.md#signing-stable-updates). Keep keystores/passwords out of git.

For cloud builds, run the **Android preview APK** workflow in GitHub Actions and download its **ERateLookup-Android-preview** artifact. Tagged releases also build and attach the Android preview package ZIP.

## GitHub Actions and releases

The workflow builds on pushes and pull requests to `main`. Pushing a `v*` tag builds Windows, macOS, and Linux x64/ARM64 packages plus the Android preview, runs the shared/security checks, and attaches the Windows/macOS ZIPs, Linux tar.gz archives, Android package ZIP, and aggregate `SHA256SUMS.txt` to a GitHub Release.

```sh
git add .
git commit -m "Describe the change"
git push origin main
# Choose an unused version for the next release:
git tag v1.0.2
git push origin v1.0.2
```

The **Publish Linux to v1.0.1** workflow can add the Linux packages to the existing release from `main`, regenerate the seven-package checksum file, and record the exact Linux source commit. The original v1.0.1 tag remains unchanged.

Keep executables, package archives, caches, and user settings out of the source repository; `.gitignore` excludes them. Compiled files belong in Releases. Each package includes instructions, the MIT license, and third-party notices.

## Project layout

| Path | Purpose |
| --- | --- |
| `Core/` | USAC queries, cache, seed data, budget display logic, and exports |
| `Desktop/` | Windows Forms interface |
| `MacDesktop/` | Avalonia interface shared by macOS and Linux |
| `LinuxDesktop/` | Linux entry point, icon, launch check, and shortcut helper |
| `Android/` | Native Android development preview |
| `Assets/` | PNG, ICO, and ICNS app icon |
| `Checks/` | Shared verification program |
| `scripts/package_mac.py` | Creates Mac app bundles and ZIP files |
| `Publish-Linux.sh` / `scripts/package_linux.py` | Creates self-contained Linux tar.gz archives |
| `.github/workflows/linux.yml` | Native Linux x64/ARM64 builds and window launch checks |
| `.github/workflows/linux-release.yml` | Adds Linux packages to the existing v1.0.1 release |
| `docs/LINUX_INSTALL.md` | Linux installation, dependencies, shortcuts, and compilation |
| `.github/workflows/windows.yml` | All-platform tagged release automation |
| `.github/workflows/android.yml` | Android APK build and package verification |
| `docs/ANDROID_INSTALL.md` | Android installation, updates, exports, and troubleshooting |

## Current limitations

GUI launch and appearance still require hands-on Windows and Mac testing. Linux CI validates native window launch on x64 and ARM64; interactive lookup, selection, export, and browser opening still require hands-on desktop testing. Packages are unsigned. The funding-year selector currently covers 2016–2027; local name suggestions cover California. USAC data publishing delays can affect filing results. Budget figures are snapshot information, not a substitute for USAC's authoritative records. This project is independent of USAC.

See [Windows user guide](docs/USER_GUIDE.md), [Mac instructions](MACOS_README.md), [validation record](VALIDATION.md), and [release notes](RELEASE_NOTES.md).

## License

Original project code and documentation are available under the [MIT License](LICENSE). Third-party components and USAC data retain their own terms; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Contributors

See [CONTRIBUTORS.md](CONTRIBUTORS.md), including credit for Codex (OpenAI), the AI coding assistant used during development.
