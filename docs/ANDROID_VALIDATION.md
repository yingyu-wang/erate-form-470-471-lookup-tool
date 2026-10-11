# Android v1.0.1 validation

Version 1.0.1 uses Android version code **111**. Local validation passed 52 host checks (including 15 Android behavior regressions), four verifier security regressions, and Android Activity compilation against API 36. Release CI rebuilds and verifies all packages before uploading them. Use the release’s `SHA256SUMS.txt` for 1.0.1 hashes; the historical hashes below apply only to the earlier 1.1.0 preview. Physical-device UI testing remains outstanding.

# Android v1.1.0 preview validation

App commit: `741e90e665cc12147d40a6d6ee6d6e92326e27d9`.

- [Android CI run 38103271597](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/actions/runs/38103271597): **passed**.
- [Desktop CI run 38103271642](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/actions/runs/38103271642): **passed** for Windows x64/ARM64 and macOS ARM64/x64.
- All **37** existing shared-core offline checks passed locally and on Android's CI build host.
- The Android Activity compiles against Android API 36; the standalone universal APK was built with .NET 10.0.401 / Android workload 36.1.69 and JDK 21.
- `apksigner verify` passed APK v2 and v3 signatures. This preview uses a generated **Android Debug** certificate, not a permanent publisher signing key.
- Manifest: `com.yingyuwang.eratelookup.android`, version **1.1.0**, version code **110**, minimum API **26**, target API **36**, launcher **E-rate Lookup**, icon included, INTERNET permission, no broad storage permissions.
- ARM64 (`arm64-v8a`) and x64 (`x86_64`) native SQLite ELF architecture checked. The dependency's ARM64 SQLite load segments use 16 KB alignment.
- APK and distribution ZIP passed archive integrity checks. License and third-party notices are included. The distribution ZIP also includes `ANDROID_INSTALL.md`.
- Downloaded GitHub artifact SHA-256 matches GitHub's digest, and each file matches the artifact's `SHA256SUMS.txt`.

| File | SHA-256 |
| --- | --- |
| `ERateLookup-v1.1.0-android-preview.apk` | `3a11e3f00275089d2eaae0f5d67477280532c47c6380f98b88c6a264e192b505` |
| `ERateLookup-v1.1.0-android-preview.zip` | `cd3f055840db11663b769c967887112d41970fe9b457b0590585b08891430b5e` |
| GitHub artifact ZIP | `ac88b55083bf1320047a549028439039f209918af7ccbe2d66ae32585faef279` |

Preview signing certificate SHA-256: `4e13ef7b447f2d9889d7c45630dad5352cc66a1856bf77979dff16c0c9d114cd`.

This validates **compilation and packaging**, not Android device behavior. No emulator/physical-device launch, on-device SQLite import, live query, export picker interaction, rotation, large-text or TalkBack test has been performed. The existing shared tests run on the build host. Android 8–10's legacy window inset branch is intentionally guarded by the runtime API level; platform analyzers still emit warnings for the fallback and newer inset APIs.

Before a stable Android release, complete the manual scenarios in [Android/README.md](../Android/README.md), test a physical ARM64 phone/tablet, and configure a permanent private signing key for compatible updates. Android's developer-verification rules may affect installation depending on the device/region. No Play Store or GitHub Release APK has been published by this PR.
