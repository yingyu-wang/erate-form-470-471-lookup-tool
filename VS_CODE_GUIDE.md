# VS Code and GitHub walkthrough

## Development setup
Install [VS Code](https://code.visualstudio.com/), [Git for Windows](https://git-scm.com/downloads/win), and the Windows x64 [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). End users only need the .NET 10 Desktop Runtime x64; developers need the SDK. Install the official OpenAI Codex extension if you want Codex to edit and run the project, and sign in. Microsoft C# tooling is also useful for editing.

Extract this package. In VS Code, use File > Open Folder and choose the directory containing ERateLookup.sln, Core, Desktop, and tests. Open Terminal > New Terminal. Run:

```powershell
dotnet --version
git --version
dotnet build ERateLookup.sln -c Release
dotnet test tests/ERateLookup.Tests/ERateLookup.Tests.csproj -c Release --filter Category!=Live
dotnet run --project Desktop/ERateLookup.csproj
```

Check both form modes, year switching, local name suggestions, empty results, refresh, exports, and themes on Windows. Automated checks do not establish that the GUI displays correctly.

## Create the remote and push source
On GitHub create an empty repository named erate-470-lookup; do not initialize it with a README, license or gitignore. Copy its HTTPS URL. From the extracted project root in the VS Code terminal:

```powershell
git init -b main
git config user.name "Your Name"
git config user.email "YOUR_GITHUB_EMAIL_OR_NOREPLY_ADDRESS"
git add .
git status
git commit -m "Initial E-rate desktop application"
git remote add origin https://github.com/YOUR_USERNAME/erate-470-lookup.git
git push -u origin main
```

Replace placeholders. Complete GitHub sign-in when prompted. If this folder already has Git initialized or an origin remote, inspect `git status` and `git remote -v` instead of adding a second remote. The .gitignore keeps EXEs, binaries and cache databases out of the source repository. Public seed data and design assets stay tracked.

## Automated builds and releases
The included .github/workflows/windows.yml runs builds/checks and uploads a ZIP on pushes to main and pull requests. After pushing, open the repository's Actions tab, open the run, and download the build artifact once successful. GitHub supplies the Windows runner and SDK; you do not need to compile each release locally.

To publish the tested commit as a release:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

A version tag triggers the workflow to publish a GitHub Release with a Windows x64 ZIP. This requires Actions to be enabled and the repository to allow the workflow's contents:write permission. Ordinary pushes produce build artifacts; version tags produce public release entries according to repository visibility. Saving local files alone never triggers GitHub. Review the commit before tagging it. Each new release needs a new tag, such as v1.0.1; do not reuse an existing published tag.

For later changes:

```powershell
git pull --ff-only
git add .
git status
git commit -m "Describe the change"
git push
```

## Local publishing alternative
Run .\Publish-Windows.bat from PowerShell. It writes publish/win-x64-framework/ERateLookup.exe. Package it with the README and upload it as a GitHub Release asset if you prefer a manual release. The app remains runtime-dependent and does not include a .NET runtime.

## Using Codex
Open the Codex sidebar in this project. Ask it to inspect the solution, implement your change, run `dotnet test`, build the Desktop project and review failures. For GitHub operations, explicitly tell it to commit/push or tag a tested commit. Codex needs GitHub authentication on your PC independently of your ChatGPT sign-in. It cannot report hosted workflow success until the actual run has completed.

## macOS in VS Code

Install the matching macOS .NET 10 SDK (Arm64 for Apple Silicon or x64 for Intel), VS Code, Git, and optionally C# Dev Kit/Codex. Open this same repository folder. Compile the Avalonia interface with `dotnet build MacDesktop/MacDesktop.csproj -c Release` and run it with `dotnet run --project MacDesktop/MacDesktop.csproj`. On macOS, do not run the Windows Forms Desktop project. See MACOS_README.md and Publish-Mac.sh for .app packaging; Python 3 is needed by that script. Xcode is not the C# compiler for this project. Apple signing/notarization uses macOS tools separately.
