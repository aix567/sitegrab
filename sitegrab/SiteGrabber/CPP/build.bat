@echo off
:: SiteGrabber C++ build script
:: Run from a "Developer Command Prompt for VS 2022" or "x64 Native Tools Command Prompt"
:: No external libraries needed — uses WinHTTP (built into Windows)

echo [build] SiteGrabber v2.0 (C++17, WinHTTP)
echo.

cl.exe ^
    /std:c++17 ^
    /O2 ^
    /EHsc ^
    /W3 ^
    /D_WIN32_WINNT=0x0A00 ^
    /DWIN32 ^
    /DUNICODE ^
    /D_UNICODE ^
    sitegrab.cpp ^
    /Fe:sitegrab.exe ^
    winhttp.lib ^
    /nologo

if %ERRORLEVEL% == 0 (
    echo.
    echo [OK] sitegrab.exe built successfully
    echo.
    echo Usage:
    echo   sitegrab.exe https://example.com
    echo   sitegrab.exe https://shop.example.com grabbed 4 16
) else (
    echo.
    echo [FAIL] Build failed. Make sure you are running from:
    echo   "x64 Native Tools Command Prompt for VS 2022"
    echo   (Start menu ^> Visual Studio 2022 ^> x64 Native Tools Command Prompt)
)
