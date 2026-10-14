# Put this project on GitHub

The source is ready for a normal Git repository. Open **ERateLookup.sln** to work on the complete C# solution. It contains the Core library, Windows desktop app, Mac desktop app, and xUnit test projects.

## Repository contents

Keep these in Git:

- `ERateLookup.sln`, `.gitignore`, and `.gitattributes`
- `Core/` source, project file, and bundled public California data snapshots
- `Desktop/` source, project file, and `Assets/mint-glass-background.png`
- `tests/` xUnit projects (`ERateLookup.Tests` and the Windows `ERateLookup.Desktop.Tests` project)
- `README.md`, `VALIDATION.md`, `GITHUB_SETUP.md`, and `verification/`
- `Run-Windows.bat` and `Publish-Windows.bat`

The `.gitignore` excludes compiled EXEs, DLLs, build output, ZIP packages, SQLite cache files, logs, and user preferences. The supplied EXE remains in the download but is not added to Git. Distribute compiled downloads through GitHub Releases; keep source in the repository.

## First upload from Windows

1. Install Git for Windows if needed: https://git-scm.com/downloads/win
2. Extract the complete project download into your chosen project folder.
3. On GitHub, create an **empty repository**, for example `erate-470-lookup`. Choose the visibility you want. Do not initialize it with a README, license, or `.gitignore`, because this project already supplies the initial files.
4. Open PowerShell in the extracted project folder, where `ERateLookup.sln` is located. Run:

```powershell
git init -b main
git add .
git status
git commit -m "Initial C# desktop app with USAC lookup and local cache"
```

5. Set the repository URL below to the actual URL GitHub gives you. Replace `YOUR_USERNAME` and the repository name as necessary:

```powershell
git remote add origin https://github.com/YOUR_USERNAME/erate-470-lookup.git
git push -u origin main
```

Git will prompt for authentication through its configured credential flow. Do not put access tokens into the remote URL or source files.

If Git asks for your commit identity, configure your chosen name and email for this repository, then retry the commit:

```powershell
git config user.name "Your Name"
git config user.email "Your GitHub commit email"
```

If you already have a remote or existing repository, use its normal clone/edit workflow instead of reinitializing it with these first-upload commands.

## Sync later changes

```powershell
git pull --ff-only
git add .
git status
git commit -m "Describe the change"
git push
```

Resolve any reported local-change or merge issues before continuing. The status command lets you review what will be committed.

## Build and publish from a clone

Developers need the **.NET 10 SDK**, not only the Desktop Runtime:

```powershell
dotnet build ERateLookup.sln -c Release
dotnet test tests/ERateLookup.Tests/ERateLookup.Tests.csproj -c Release --filter Category!=Live
```

On Windows, run `Publish-Windows.bat` to create the smaller runtime-dependent EXE at:

`publish\win-x64-framework\ERateLookup.exe`

The Windows executable assembly name is `ERateLookup.exe`. Existing installs keep their data under `%LOCALAPPDATA%\ERate470LookupDotNet`.

Create a GitHub Release for a version tag and attach the compiled EXE or a ZIP containing the EXE and end-user instructions. The end-user .NET 10 Desktop Runtime prerequisite remains documented in README.md.

## Where the data belongs

The bundled California JSON snapshots are public source assets and stay in Git so a clone can build the same app. Live SQLite caches are generated on each user's PC under `%LOCALAPPDATA%\ERate470LookupDotNet` and do not belong in Git. Runtime theme choices are local as well.

This preparation does not create or upload a GitHub repository. The initial push needs your chosen repository URL and GitHub authentication.

## Release tag moves

Rebuilding an existing version from **Windows build and release** can move the lightweight tag onto the build commit. That move is the `align-release-tag` job, and it runs only when `release_tag` is supplied. The job uses the GitHub environment `release-tag-move`, so a reviewer must approve it before the tag changes. The publish job then checks that the tag already points at the build and uploads the archives. It does not move the tag itself.

Create the environment before the first rebuild:

1. Open the repository **Settings → Environments → New environment**.
2. Name it exactly `release-tag-move`.
3. Add required reviewers, including the repository owner.
4. Limit deployment branches to `main`.

Until that environment exists with required reviewers, a rebuild can move the tag without a separate approval. Tagged releases that are created by pushing a `v*` tag do not use this environment. Nightly live USAC checks are `.github/workflows/nightly-usac.yml` (`schedule` plus `workflow_dispatch`, contents read-only) and do not publish anything. That workflow retries the live filter three times, waiting about 30 seconds and then 60 seconds. If USAC is still unreachable, the job stays green and its summary says it was skipped. An assertion or build failure still fails the run.
