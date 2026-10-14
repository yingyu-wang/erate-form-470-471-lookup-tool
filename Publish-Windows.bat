@echo off
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 goto missing
dotnet publish Desktop\ERateLookup.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish\win-x64-framework
if errorlevel 1 goto failed
echo Runtime-dependent portable executable: publish\win-x64-framework\ERateLookup.exe
pause
exit /b 0
:missing
echo Install the .NET 10 SDK first.
:failed
pause
exit /b 1
