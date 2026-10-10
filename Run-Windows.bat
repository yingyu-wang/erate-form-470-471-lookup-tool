@echo off
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 goto missing
dotnet run --project Desktop\ERate470Lookup.csproj
if errorlevel 1 pause
exit /b
:missing
echo Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
pause
exit /b 1
