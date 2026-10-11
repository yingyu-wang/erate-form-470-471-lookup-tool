@echo off
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 goto missing
dotnet publish Desktop\ERate470Lookup.csproj -c Release -r win-arm64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish\win-arm64-framework
if errorlevel 1 goto failed
echo Runtime-dependent portable executable: publish\win-arm64-framework\ERate470Lookup.exe
pause
exit /b 0
:missing
echo Install the .NET 10 SDK first.
:failed
pause
exit /b 1
