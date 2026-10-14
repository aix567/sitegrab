# SiteGrab

**Full-site mirror + exposed-data hunter + recon suite.**

Crawl any website, save it offline, scan it for leaked credentials, and inspect
every request it made — from one Windows app.

---

## Download

**SiteGrabber v3** (WPF dashboard, the current release) —
[⬇ Download for Windows](https://github.com/aix567/sitegrab/releases/latest)

Single-file exe. Self-contained. No .NET runtime install required. Windows 10/11 x64.

Older versions:

- **old one** — WinForms tabbed UI + C++ + Python engines → [`sitegrab/`](sitegrab/)
- **v3 source** — the WPF project → [`sitegrabber-v4/`](sitegrabber-v4/)

---

## What it does

### 1. Mirror

Recursive same-domain crawl. Saves HTML, CSS, JS, images, fonts, video, audio,
documents. Follows `<a href>`, `<form action>`, sitemaps. Handles lazy-loaded
images (`data-src`, `srcset`). Skips duplicates. Keeps query strings as
distinct pages.

### 2. Link rewriting

A second pass rewrites every absolute URL in the mirrored HTML and CSS to a
relative local path. **Open the mirror folder in a browser, disconnect Wi-Fi,
and it still works.** CSS resolves, links navigate locally, images load.

### 3. Pagination exhaustion

Detects `?page=N`, `?p=N`, `?offset=N`, `?start=N`, `?skip=N`, `?from=N`,
`?pageno=N`, `?page_num=N` on any crawled link. Walks each key up to **500
pages**. Search backends and product catalogs get walked to exhaustion.

### 4. Sitemaps

Parses `/sitemap.xml`, `/sitemap_index.xml`, `/sitemap.txt`,
`/sitemap-products.xml`, `/product-sitemap.xml`. Seeds the crawl queue from
them.

### 5. Product extraction

Reads JSON-LD and OpenGraph product records from every crawled page. Writes
`_products.ndjson` — one JSON object per line, one per product.

### 6. Exposed-data hunter

Scans every mirrored HTML/CSS/JS file for:

| Rule | Severity | Detects |
|---|---|---|
| `aws_access_key` | critical | `AKIA...` |
| `aws_secret` | critical | `aws_secret_access_key` |
| `stripe_live_secret` | critical | `sk_live_...` |
| `github_token` | critical | `ghp_`, `gho_`, `ghs_`, `ghr_` |
| `private_key` | critical | PEM private key header |
| `env_file` | critical | Multi-line `KEY=VALUE` blocks |
| `git_exposure` | critical | `.git/HEAD`, `.git/config` reachable |
| `google_api_key` | high | `AIza...` |
| `slack_token` | high | `xox[baprs]-` |
| `jwt` | high | three-part JWT |
| `firebase_config` | high | apiKey + authDomain pair |
| `phpmyadmin` | high | `/phpmyadmin`, `/adminer` |
| `debug_endpoint` | high | `/debug`, `/actuator`, `/server-status` |
| `s3_reference` | medium | `s3://`, `*.amazonaws.com` |
| `graphql_endpoint` | medium | `/graphql` paths |
| `wp_login` | medium | `/wp-login.php`, `/wp-admin` |
| `admin_panel` | medium | `/admin`, `/cpanel`, `/webmin` |
| `stack_trace` | medium | Framework stack traces |
| `source_map` | low | `sourceMappingURL=*.map` |
| `email_address` | low | Emails on the page |
| `internal_ip` | low | `10.x`, `172.16-31.x`, `192.168.x` |

Results written to `_findings.ndjson`. **Every secret is redacted in the
output** — the first 6 and last 6 characters only, with the length noted.

### 7. HTTP History

Every request the crawler makes is logged with full headers, timing, status,
and body. Searchable, filterable, clickable.

### 8. Request Repeater

Double-click any request in History → edit method, URL, headers, body → send
→ response appears beside it. Results feed back into History.

### 9. Subdomain enumeration

DNS-only scan against a wordlist. Live hosts appear as they resolve. Optional
batch replay of 12 common paths against every found subdomain.

### 10. CSV export

History and subdomain results export to CSV for offline analysis.

### 11. Resume-safe

Writes crawl state after every batch. Kill the process mid-crawl, relaunch, it
picks up where it left off.

---



## Video Demo for the Software v4.0.0

https://drive.google.com/file/d/1xTUM0qdtHqRuOjS1vssY-BxhNzplRssS/view?usp=drive_link

<img width="1298" height="777" alt="image" src="https://github.com/user-attachments/assets/ed93b56d-8ebd-4069-a3b1-f126b069a856" />

<img width="1302" height="779" alt="image" src="https://github.com/user-attachments/assets/6517e15e-bb34-4e0b-8d1b-9d8e09d2a85f" />

<img width="1299" height="779" alt="image" src="https://github.com/user-attachments/assets/1ea8fb7a-b050-4882-a390-7644e71f2fd8" />



## Building from source

| Target | Toolchain |
|---|---|
| C++ | MSVC 19+ (`/std:c++17`), links `winhttp.lib` |
| C# | .NET 8.0 SDK, `HtmlAgilityPack` (auto-restored) |
| Python | Python 3.10+, `requests`, `beautifulsoup4` |

---
## Build from source

### v3 — WPF dashboard (recommended)

**Requirements:** .NET 8.0 SDK, Windows 10/11.

```powershell
cd sitegrabber-v4
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

```

## License

MIT — see [LICENSE](LICENSE).



