# SiteGrabber v2.0

Full-site downloader — HTML, CSS, JS, images, fonts, video, audio, documents.
Follows internal links recursively. Parses sitemaps. Handles search/product/pricing pages via query string support and pagination expansion.

---

## Three versions

| Version     | File                          | Use case                     |
|-------------|-------------------------------|------------------------------|
| **Python**  | `Python/sitegrab.py`          | Quick terminal use, any OS   |
| **C++**     | `CPP/sitegrab.cpp`            | Max speed, no .NET needed    |
| **C# GUI**  | `CSharp_GUI/SiteGrabber.csproj` | Windows EXE with dark-theme UI |

---

## C# GUI — Build & Run (Windows)

### Requirements
- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022  OR  `dotnet` CLI

### Build single EXE
```cmd
cd CSharp_GUI
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```
Output: `bin\Release\net8.0-windows\win-x64\publish\SiteGrabber.exe`

### Or open in Visual Studio
Double-click `SiteGrabber.csproj` → Build → Run

### GUI overview
```
URL:     [https://shop.example.com      ]   Output: [C:\grabbed\]
Depth: 4   Threads: 16   Delay: 0ms
☑ Follow Links   ☑ Parse Sitemap
☑ Images  ☑ CSS  ☑ JavaScript  ☑ Fonts  ☑ Video  ☑ Documents
[ ▶ START ]  [ ■ STOP ]  [ 📂 Open Output ]
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✓ [page] https://shop.example.com/
  ✓ [12KB] https://shop.example.com/style.css
  ✓ [page] https://shop.example.com/products
  ✓ [page] https://shop.example.com/pricing
```

---

## C++ — Build (Windows)

### Requirements
- Visual Studio 2022 with C++ workload
- [vcpkg](https://github.com/microsoft/vcpkg) for libcurl

```cmd
vcpkg install curl:x64-windows
cd CPP
mkdir build && cd build
cmake .. -DCMAKE_TOOLCHAIN_FILE=C:/vcpkg/scripts/buildsystems/vcpkg.cmake
cmake --build . --config Release
```
Output: `build\Release\sitegrab.exe`

### Usage
```cmd
sitegrab.exe https://example.com grabbed 4 16
#            ^URL                 ^out    ^depth ^threads
```

---

## Python — Run (any OS)

```bash
pip install requests beautifulsoup4
python sitegrab.py https://example.com -o grabbed -d 4 -t 16
```

Options:
```
-o / --output   output folder (default: grabbed)
-d / --depth    crawl depth   (default: 4)
-t / --threads  threads       (default: 16)
--delay         seconds between requests (default: 0)
```

---

## What gets downloaded

- All HTML pages found via `<a href>` links (recursive, depth-limited)
- All assets: `<img>`, `<script>`, `<link rel=stylesheet>`, `<source>`, `<video>`, `<audio>`, `<iframe>`, `<object>`, `<embed>`
- Inline `style="background: url(...)"` images
- CSS second-pass: fonts, background images referenced inside `.css` files
- Sitemap pages (`/sitemap.xml`, `/sitemap_index.xml`, `/sitemap.txt`)
- Search/filter pages: query strings preserved (e.g. `?category=shoes&page=2`)
- Pagination: auto-expands `?page=N` up to 10 pages per URL

## Output structure
```
grabbed/
└── example.com/
    ├── index.html
    ├── products.html
    ├── pricing.html
    ├── search_q=shoes.html
    ├── css/
    │   └── style.css
    ├── js/
    │   └── app.js
    └── images/
        └── hero.webp
```
