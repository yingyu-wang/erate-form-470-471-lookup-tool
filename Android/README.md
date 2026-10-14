# Android development preview

Native C# / Android UI sharing the existing `Core` project. Development version **1.0.3**, version code **113**, application ID **com.yingyuwang.eratelookup.android**. Android 8.0+; ARM64 devices and x64 emulators. No iOS support is included.

Features: offline California name search, nationwide name search, BEN/year/Form 470/471 lookup, two-hour query cache, C2 budget snapshot and forecast labels, directory refresh, cancellation, selected-filing CSV/XLSX export through the Android document picker, and HTTPS PDF opening in another app. The existing PNG icon is reused. Native Android styling replaces the desktop themes.

## Install and use

See [the Android installation guide](../docs/ANDROID_INSTALL.md), including APK extraction, per-source installation permission, updates/signature conflicts, file exports, and USB installation. Preview builds bundle their runtime. No Play Store release or physical-device validation is claimed.

## Build

Use .NET SDK **10.0.401**, its Android workload (36.1.69), JDK 21, and Android SDK platform 36/build tools. Install Android SDK components and accept their licenses using Android Studio's SDK Manager or Microsoft's dependency target. Keep these tools outside the source tree.

```sh
dotnet workload install android --version 10.0.401
# Use absolute local paths; read/accept the SDK terms as appropriate.
dotnet build Android/ERateLookup.Android.csproj -t:InstallAndroidDependencies \
  -p:AndroidSdkDirectory=/absolute/path/android-sdk \
  -p:JavaSdkDirectory=/absolute/path/jdk-21 \
  -p:AcceptAndroidSDKLicenses=True
dotnet test tests/ERateLookup.Tests/ERateLookup.Tests.csproj -c Release --filter Category!=Live
dotnet publish Android/ERateLookup.Android.csproj -c Release \
  -p:AndroidSdkDirectory=/absolute/path/android-sdk \
  -p:JavaSdkDirectory=/absolute/path/jdk-21
```

The `*-Signed.apk` in the publish output is installable and includes ARM64 and x64. `PublishTrimmed=false` preserves the shared reflection-based JSON models; AOT is disabled in this first preview. The desktop solution does not reference Android, so desktop builders do not need an Android workload.

The **Android preview APK** workflow builds on Android/Core/guide changes and can be run manually. It runs all shared offline checks, signs/packages a standalone preview APK, verifies APK signature, manifest, CPU types and bundled SQLite, and uploads the APK, package ZIP, and checksum file for 30 days. It does not publish to Google Play or change desktop release assets.

## Signing stable updates

The default build uses the Android debug keystore. CI creates a new key on a clean runner. It is suitable for initial testing, but **not** a stable update channel. Do not commit keystores or passwords. For stable release APKs, keep a backed-up private release key and supply these MSBuild properties via private CI secrets/local configuration:

```text
AndroidKeyStore=true
AndroidSigningKeyStore=/private/path/release.keystore
AndroidSigningKeyAlias=<alias>
AndroidSigningKeyPass=env:ANDROID_KEY_PASSWORD
AndroidSigningStorePass=env:ANDROID_STORE_PASSWORD
```

Increment `ApplicationVersion` for new releases, keep the same application ID/key, and use appropriate developer registration before distributing widely. Google Play needs its own release bundle, console setup, policy/privacy information and review; none are provided by the preview APK workflow.

## Validation scope

The [Android APK build and package checks](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/actions/runs/38103271597) passed for app commit `741e90e665cc12147d40a6d6ee6d6e92326e27d9`. The installable preview APK and installation guide are in that run's **ERateLookup-Android-preview** artifact. See [the validation record](../docs/ANDROID_VALIDATION.md) for hashes and exact test scope.

Shared-core checks cover query filters, result collapsing, bundled data/cache logic, budget rules and CSV/XLSX safety. APK inspection verifies build packaging, not runtime UI or live networking. Before calling Android supported, test on a physical ARM64 phone/tablet: first launch offline/SQLite seed import; CA match selection; Form 470 and 471 queries; budget forecast/missing labels; cancellation/offline cache; selected-only exports and picker cancel; PDF links; rotation, keyboard, large text and TalkBack; and update signing.

Code/docs are MIT-licensed; root third-party/data notices apply and are included in preview packages.

Version 1.0.1 includes optional-request fallback, 50-item result pages, cancellable export generation/copy, and automatic stale-snapshot refresh with offline fallback. Tagged releases include the preview package ZIP alongside desktop assets; the APK is inside it.

## v1.0.2 portfolio development

Use the **Saved BENs / Watchlists** button to open the private portfolio activity.
[Portfolio usage](../docs/PORTFOLIO_GUIDE.md) explains CSV import through the system
file picker, cross-page selection/export, offline results, and the share chooser.
Build commands above compile the current 1.0.2 source. For CI tests, use the
Android artifact from a successful PR #8 Actions run. The public release remains
v1.0.1. Physical-device/lifecycle checks remain outstanding.
