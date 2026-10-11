# SiteGrab

Mirrors websites to disk. Follows links, parses sitemaps, handles search/product/pricing pages. Three implementations — C++ console tool, C# WinForms GUI, Python script.

---

## C++ (Windows, no dependencies)

Builds against `winhttp.lib` which ships with Windows — no vcpkg, no NuGet.

```cmd
cd CPP
build.bat
```

Requires the **x64 Native Tools Command Prompt** for VS 2022 (not a regular terminal).

```cmd
sitegrab.exe https://example.com
sitegrab.exe https://example.com output_dir 4 16
                                             ^   ^
                                          depth  threads
```

---

## C# GUI (Windows)

```cmd
cd CSharp_GUI
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Produces a single portable `.exe` in `bin\Release\net8.0-windows\win-x64\publish\`.  
Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

---

## Python (cross-platform)

```bash
pip install requests beautifulsoup4
python sitegrab.py https://example.com -o output_dir -d 4 -t 16
```

---

## What it grabs

- All reachable pages via `<a href>` up to the configured depth
- Every asset referenced in HTML and CSS (`<img>`, `<script>`, `<link>`, fonts, backgrounds)
- Sitemap URLs (`/sitemap.xml`, `/sitemap_index.xml`)
- Query-string variants — product filters, search results, pagination (`?page=N` auto-expanded)

Output mirrors the site's path structure under `grabbed/<hostname>/`.

---

## Building from source

| Target | Toolchain |
|---|---|
| C++ | MSVC 19+ (`/std:c++17`), links `winhttp.lib` |
| C# | .NET 8.0 SDK, `HtmlAgilityPack` (auto-restored) |
| Python | Python 3.10+, `requests`, `beautifulsoup4` |

---

## License

MIT — see [LICENSE](LICENSE).
