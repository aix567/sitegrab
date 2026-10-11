@echo off
:: SiteGrabber C++ build script
:: Run from a "Developer Command Prompt for VS 2022" or "x64 Native Tools Command Prompt"
:: No external libraries needed — uses WinHTTP (built into Windows)

echo [build] SiteGrabber v3.0 (C++17, WinHTTP)
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
    echo   sitegrab.exe https://shop.example.com grabbed 6 16
    echo   sitegrab.exe https://shop.example.com grabbed 6 16 --no-paginate
    echo   sitegrab.exe https://shop.example.com grabbed 6 16 --no-products
    echo   sitegrab.exe https://shop.example.com grabbed 6 16 --no-resume
    echo.
    echo Flags:
    echo   --no-paginate   do not exhaust ?page=N / ?offset=N up to 500 pages
    echo   --no-products   skip JSON-LD / OpenGraph product extraction
    echo   --no-resume     ignore .sitegrab_state.tsv and start fresh
    echo.
    echo Output:
    echo   grabbed\^<host^>\              pages + assets + _products.ndjson
    echo   grabbed\^<host^>\.sitegrab_state.tsv   resumable crawl state
) else (
    echo.
    echo [FAIL] Build failed. Make sure you are running from:
    echo   "x64 Native Tools Command Prompt for VS 2022"
    echo   (Start menu ^> Visual Studio 2022 ^> x64 Native Tools Command Prompt)
)