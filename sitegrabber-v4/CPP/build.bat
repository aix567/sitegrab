@echo off
REM ──────────────────────────────────────────────────────────────────
REM  SiteGrabber v3.0 — C++ core crawler build
REM  Run from: x64 Native Tools Command Prompt for VS 2022
REM ──────────────────────────────────────────────────────────────────
echo [BUILD] sitegrab.exe (C++17 / WinHTTP)
cl.exe /std:c++17 /O2 /EHsc /D_WIN32_WINNT=0x0A00 /DUNICODE /D_UNICODE ^
       /W3 /nologo ^
       sitegrab.cpp ^
       /Fe:..\sitegrab.exe ^
       winhttp.lib
if %errorlevel% neq 0 (
    echo [FAIL] C++ build failed
    exit /b 1
)
echo [OK]   sitegrab.exe
