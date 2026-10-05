@echo off
rem Publish a single-file framework-dependent executable.
rem Target machine needs the .NET 8 Desktop Runtime installed.
setlocal
cd /d "%~dp0..\src\ThemeSwitcher"
if errorlevel 1 exit /b 1

dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
if errorlevel 1 exit /b 1

echo.
echo Output: %CD%\bin\Release\net8.0-windows\win-x64\publish\ThemeSwitcher.exe
echo.
rem Self-contained portable build (no .NET runtime needed on target machine, larger file):
rem dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
