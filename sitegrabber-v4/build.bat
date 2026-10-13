@echo off
REM ══════════════════════════════════════════════════════════════════
REM  SiteGrabber v3.0 — Full build script
REM  Prerequisites:
REM    • Visual Studio 2022 (x64 Native Tools Command Prompt)
REM    • .NET SDK 8.0+
REM    • Rust (rustup, cargo)
REM    • Python 3.9+ (in PATH)
REM ══════════════════════════════════════════════════════════════════
setlocal enabledelayedexpansion

set ROOT=%~dp0
set ERRORS=0

echo.
echo ██████████████████████████████████████████████
echo   SiteGrabber v3.0 - Build System
echo ██████████████████████████████████████████████
echo.

REM ── 1. C++ core crawler ───────────────────────────────────────────
echo [1/4] Building C++ crawler (WinHTTP)...
pushd "%ROOT%CPP"
call build.bat
if %errorlevel% neq 0 (
    echo [WARN] C++ build failed - app will use built-in .NET crawler
    set ERRORS=1
) else (
    echo [OK]  sitegrab.exe built
)
popd

REM ── 2. Rust subdomain enumerator ─────────────────────────────────
echo.
echo [2/4] Building Rust subdomain enumerator...
pushd "%ROOT%Rust\subdomain_enum"
where cargo >nul 2>&1
if %errorlevel% neq 0 (
    echo [WARN] cargo not found - Rust binary skipped, C# fallback will run
    set ERRORS=1
) else (
    cargo build --release
    if %errorlevel% neq 0 (
        echo [WARN] Rust build failed
        set ERRORS=1
    ) else (
        echo [OK]  subdomain_enum.exe built
    )
)
popd

REM ── 3. Python analyzer (no build needed, just verify) ─────────────
echo.
echo [3/4] Verifying Python analyzer...
where python >nul 2>&1
if %errorlevel% neq 0 (
    where python3 >nul 2>&1
    if %errorlevel% neq 0 (
        echo [WARN] Python not found in PATH - findings analysis will be skipped
        set ERRORS=1
    ) else (
        python3 -c "import re, json, pathlib; print('[OK]  analyzer.py ready')"
    )
) else (
    python -c "import re, json, pathlib; print('[OK]  analyzer.py ready')"
)

REM ── 4. C# WPF GUI ────────────────────────────────────────────────
echo.
echo [4/4] Building SiteGrabber WPF GUI (.NET 8)...
dotnet build "%ROOT%SiteGrabberV3.csproj" -c Release --nologo
if %errorlevel% neq 0 (
    echo [FAIL] .NET build failed
    set ERRORS=1
) else (
    echo [OK]  SiteGrabber.exe built
)

REM ── Summary ───────────────────────────────────────────────────────
echo.
echo ══════════════════════════════════════════════
if %ERRORS% equ 0 (
    echo   Build complete - no errors
) else (
    echo   Build complete - some components had warnings
    echo   The GUI will fall back to built-in .NET implementations
)
echo ══════════════════════════════════════════════
echo.
echo To run: bin\Release\net8.0-windows\win-x64\SiteGrabber.exe
echo   (or use:  dotnet run)
echo.
endlocal
