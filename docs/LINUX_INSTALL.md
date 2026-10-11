# Linux x64 / ARM64 — E-rate Lookup v1.0.1

The Linux GUI shares the macOS Avalonia interface and the Windows/Android lookup,
SQLite, budget, and CSV/Excel code. These portable packages include .NET 10;
you do not need to install .NET to run them.

## Choose a package

Download from [GitHub Releases](https://github.com/yingyu-wang/erate-form-470-471-lookup-tool/releases/tag/v1.0.1):

| `uname -m` | Package |
| --- | --- |
| `x86_64` | `ERateLookup-v1.0.1-linux-x64.tar.gz` |
| `aarch64` or `arm64` | `ERateLookup-v1.0.1-linux-arm64.tar.gz` |

Requires a 64-bit **glibc-based Linux desktop**. Ubuntu 24.04 LTS is the build and
launch-check baseline on both architectures. GNOME and KDE desktops can run the
app. An X11 session or XWayland is needed for the default Avalonia backend.
Alpine/musl, headless servers without a display, and 32-bit systems are not targets.

## Install and run

1. Install native system dependencies if your desktop does not already include them.
   Ubuntu 24.04 / Debian:

   ```bash
   sudo apt update
   sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libicu-dev libssl3t64 zlib1g libstdc++6 fonts-dejavu-core xdg-utils
   ```

   On Debian releases with `libssl3` rather than `libssl3t64`, use `libssl3`.
   Fedora uses `libX11`, `libICE`, `libSM`, `fontconfig`, `libicu`, `openssl-libs`,
   `zlib`, `libstdc++`, `dejavu-sans-fonts`, and `xdg-utils`.
   A Wayland desktop also needs its distribution's XWayland package
   (`xwayland` on Ubuntu/Debian; `xorg-x11-server-Xwayland` on Fedora).
2. Extract the matching archive into a directory you own; keep **all files together**.

   ```bash
   mkdir -p "$HOME/Applications"
   tar -xzf ERateLookup-v1.0.1-linux-x64.tar.gz -C "$HOME/Applications"
   "$HOME/Applications/ERateLookup-v1.0.1-linux-x64/ERateLookup"
   ```

   Replace `linux-x64` with `linux-arm64` for ARM64.
3. Optional: add an application-menu shortcut, with the included icon:

   ```bash
   python3 "$HOME/Applications/ERateLookup-v1.0.1-linux-x64/install-shortcut.py"
   ```

   No administrator privileges are needed for this step. Keep the app directory
   at the same location; rerun the script after moving it. For the optional shortcut,
   the app path must not contain an equals sign; the launcher uses `/usr/bin/env`. Remove the shortcut
   by deleting `~/.local/share/applications/org.eratelookup.desktop.desktop`
   (or the corresponding file under `$XDG_DATA_HOME`). Python 3 is needed only
   for this optional shortcut step.

Verify downloads with the release's **SHA256SUMS.txt** (`sha256sum --ignore-missing
-c SHA256SUMS.txt` in the download directory). If executable permissions were
lost during extraction, run `chmod +x ERateLookup` in the app directory.

## Using the app

Enter a BEN (for example **225495**, CENIC) or part of a California customer name,
select FY2027 and Form 470, and search. Use **Ctrl-click** for individual rows,
**Shift-click** for a range, or **Select all**. Export selected filings as CSV or
Excel; PDF links open in your default browser via `xdg-open`.

The local SQLite cache and saved theme live under
`$XDG_DATA_HOME/ERateLookup` (normally `~/.local/share/ERateLookup`). Live USAC
lookups require Internet access; bundled California suggestions remain usable
when a directory refresh fails. Deleting this directory removes cached data/settings.

## Build from source

Install .NET SDK 10 and Python 3, then run from the repository root:

```bash
dotnet run --project Checks/Checks.csproj -c Release
bash Publish-Linux.sh linux-x64
bash Publish-Linux.sh linux-arm64
```

Archives are written to `artifacts/`. Cross-publishing creates either architecture's
package, but launching requires matching hardware. GitHub Actions builds each on
its matching Ubuntu 24.04 runner, runs the shared checks, and launches the packaged
window under Xvfb. `ERateLookup --smoke-test` performs that offline launch check;
it skips startup network refresh and exits after showing the window.

Window launch checks do not cover all interactive behaviors: verify live lookup,
selection, file-picker export, and browser opening on your own GNOME/KDE desktop.
These downloads are unsigned portable previews. There is no package-manager or
system-wide installation requirement.

References: [Avalonia Linux](https://docs.avaloniaui.net/docs/platform-specific-guides/linux),
[.NET Linux prerequisites](https://learn.microsoft.com/en-us/dotnet/core/install/linux).

## v1.0.2 development build

The released v1.0.1 instructions above remain valid for that release. To test the new
portfolio workflow, use a successful **PR #8** GitHub Actions artifact for your
architecture; package names contain **v1.0.2**. Follow the same extraction and
installation process, substituting that version in the commands. No v1.0.2
release is published yet.

Open **Saved BENs / Watchlists** and follow [the portfolio guide](PORTFOLIO_GUIDE.md).
The guide covers CSV import, refresh, changes, filters, summaries, dates, selection,
exports, saved/offline results, and Android sharing. It is also included in the package.
