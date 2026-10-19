# Install E-rate Lookup on Android

## Requirements and preview status

- Android 8.0 or newer (API 26+), ARM64 phone/tablet. The APK also includes x64 for emulators; 32-bit-only devices are not supported.
- Android v1.0.1 is a development preview included alongside the desktop v1.0.1 packages. It is not on Google Play and has not yet been validated on a physical device.
- The APK includes .NET and SQLite. You do **not** need a separate runtime, Android Studio, or a computer for the normal installation path.
- California name search works with bundled data offline after initialization. Live USAC filing queries and PDFs require internet; successful queries can use a fresh two-hour cache.

## Download the APK

For v1.0.1, open this repository’s **Releases** page and download **ERateLookup-v1.0.1-android-preview.zip**. Extract it in Files/your file manager; the ZIP contains **ERateLookup-v1.0.1-android-preview.apk**, this guide, and notices. Check the shared `SHA256SUMS.txt` if verifying your download.

The following Actions method is also available for development builds.

1. On your Android device, open [this project's GitHub Actions page](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/actions).
2. Open a successful **Android preview APK** run for the Android pull request/branch. At the bottom, download the **ERateLookup-Android-preview** artifact. GitHub requires sign-in to download workflow artifacts; they expire after 30 days.
3. Open the downloaded artifact ZIP in Files or your file manager and extract it. The installable file is **ERateLookup-v1.0.1-android-preview.apk**. Do not install GitHub's source-code ZIP. The artifact also contains a package ZIP with this guide, license, notices, and checksums.
4. Optional: verify the APK against the artifact's **SHA256SUMS.txt** with a trusted checksum tool. This detects an incomplete or altered download; a checksum alone does not verify publisher identity.

Tagged releases include the preview package ZIP after all platform builds and package checks succeed.

## Install on a phone or tablet

1. Tap **ERateLookup-v1.0.1-android-preview.apk** in Downloads/Files.
2. If Android says installation from this source is not allowed, tap **Settings**. Enable **Allow from this source** for the browser or file manager you are using. The usual path is **Settings → Apps → Special app access → Install unknown apps**; wording varies by manufacturer.
3. Return to the APK and tap **Install**, then **Open**. The launcher name is **E-rate Lookup**.
4. After installation, turn off **Allow from this source** again if you no longer need it. This does not remove the installed app. Keep Play Protect enabled.
5. Wait for **Ready** and the California snapshot label. Try the name **CENIC** with **Search CA**, or enter BEN **225495**, funding year **2027**, and **Form 470**, then tap **Look up**. Live counts can change.

Developer verification rules can add another installation step on some devices/regions. If Android specifically blocks an *unverified developer*, consult [Google's current installation guidance](https://support.google.com/android/answer/17065026) and your device's prompts. Ordinary **Install unknown apps** permission may not be sufficient. Device-management or parental policies may also block installation; contact the device administrator. Do not disable Play Protect to work around a malware warning.

## Save CSV, Excel, or JSON

1. Complete a lookup successfully.
2. Select the filings you want using their checkboxes, or **Select / clear all**. Changing the BEN, funding year, or form clears previous lookup/export results.
3. Tap **Save selected (CSV / Excel / JSON)**, choose Excel, CSV, or JSON, then select a folder/name in Android's file picker and tap **Save**.
4. Open the saved file in Files or an appropriate spreadsheet app. No broad storage permission is needed; access is granted only to your chosen destination.

The file uses the same columns as the command-line export, including `Source`, `Linked`, `Form470Count`, and `Form471Count`. `Linked` is `false` and the two counts are blank. JSON uses `application/json`. A successful lookup with no filings can export one row with a blank application number and a blank source. A failed or canceled query cannot be exported as a successful no-results query. Category 2 budget amounts stay on the screen and are not columns in the file. A missing on-screen budget means **Not reported**, not zero.

## Updating and uninstalling

An update must have the same application ID and signing certificate, and an appropriate version code. Normally, tap the newer APK and choose **Update**; app cache/data stay in place.

The first CI preview uses an automatically generated Android **debug signing certificate**, not a permanent publisher key. Different CI runs can use different certificates. If Android reports **App not installed** or a signature conflict, export any files you need, uninstall the old preview, and install the new one. **Uninstalling clears its local cache and settings**; exported files in your chosen folder remain. Use a permanent private signing key before distributing stable updates. No seven-day iOS-style provisioning expiry is used.

To remove the app: long-press **E-rate Lookup** and select **Uninstall**, or use **Settings → Apps → E-rate Lookup → Uninstall**.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Cannot download Actions artifact | Sign in to GitHub; select a successful run; artifact may have expired. |
| ZIP opens instead of installer | Extract it and tap the `.apk`, not the ZIP. |
| App not installed | Android 8+, ARM64 device, complete download, enough free space, and no conflicting old signing certificate. |
| Installation blocked by administrator | Ask the device administrator; a managed device may prohibit sideloading. |
| Ready never appears or app crashes | Report device model, Android version, APK hash/run number, and the visible error in a GitHub issue. |
| USAC query fails | Check internet and try again later; rate limits and service errors are shown separately from no-results. |
| PDF cannot open | Confirm a browser/PDF app is installed; only HTTPS links are opened. |

## Optional developer installation over USB

Install Android platform tools on a computer. Enable Developer options and USB debugging, connect the device, and authorize that computer. Then run:

```sh
adb devices
adb install -r ERateLookup-v1.0.1-android-preview.apk
```

Only use `-r` for a compatible signing certificate. Do not uninstall an existing copy without first exporting anything you need. Turn USB debugging off after testing.

Official references: [Google Pixel app installation](https://support.google.com/pixelphone/answer/7391672), [Android alternative distribution](https://developer.android.com/distribute/marketing-tools/alternative-distribution), [Android developer verification](https://support.google.com/android/answer/17065026), and [Android document picker](https://developer.android.com/training/data-storage/shared/documents-files).

## v1.0.2 development build

The released v1.0.1 instructions above remain valid for that release. To test the new
portfolio workflow, use a successful **PR #8** GitHub Actions artifact for your
architecture; package names contain **v1.0.2**. Follow the same extraction and
installation process, substituting that version in the commands. No v1.0.2
release is published yet.

Open **Saved BENs / Watchlists** and follow [the portfolio guide](PORTFOLIO_GUIDE.md).
The guide covers CSV import, refresh, changes, filters, summaries, dates, selection,
exports, saved/offline results, and Android sharing. It is also included in the package.
