#!/usr/bin/env python3
# language: Python 3.10+, file: sitegrab.py, target: any OS
# full-site crawler — HTML, CSS, JS, images, fonts, video, audio
# follows internal links recursively, parses sitemaps, handles query strings

import sys, re, time
from pathlib import Path
from urllib.parse import urljoin, urlparse, unquote, urlencode, parse_qs, urlunparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from collections import deque
from xml.etree import ElementTree

import requests
from bs4 import BeautifulSoup

# ── asset tag map ────────────────────────────────────────────────────────────
ASSET_ATTRS = {
    "img":    ["src", "data-src", "data-lazy-src", "data-original"],
    "script": ["src"],
    "link":   ["href"],
    "source": ["src", "srcset"],
    "video":  ["src", "poster"],
    "audio":  ["src"],
    "iframe": ["src"],
    "object": ["data"],
    "embed":  ["src"],
    "track":  ["src"],
}

CSS_URL     = re.compile(r'url\(["\']?([^"\')\s]+)["\']?\)', re.IGNORECASE)
SRCSET_PART = re.compile(r'(\S+)(?:\s+[\d.]+[wxh])?')

HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) "
        "Chrome/128.0.0.0 Safari/537.36"
    ),
    "Accept":          "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    "Accept-Language": "en-US,en;q=0.5",
    "Accept-Encoding": "gzip, deflate, br",
    "Connection":      "keep-alive",
}

# ── helpers ──────────────────────────────────────────────────────────────────
def normalise(url: str, base: str) -> str | None:
    try:
        full = urljoin(base, url.strip().split()[0])
        p    = urlparse(full)
        if p.scheme not in ("http", "https"):
            return None
        return urlunparse(p._replace(fragment=""))
    except Exception:
        return None

def url_to_local(url: str, root: Path) -> Path:
    p   = urlparse(url)
    rel = unquote(p.path).lstrip("/") or "index.html"
    if not Path(rel).suffix:
        rel = rel.rstrip("/") + ".html"
    # query string → safe filename suffix
    if p.query:
        safe_q = re.sub(r'[^\w=&]', '_', p.query)[:60]
        stem   = Path(rel).stem + "_" + safe_q
        rel    = str(Path(rel).with_name(stem + Path(rel).suffix))
    return root / p.netloc / rel

def is_same_domain(url: str, base_host: str) -> bool:
    h = urlparse(url).netloc
    return h == base_host or h.endswith("." + base_host)

# ── fetch ────────────────────────────────────────────────────────────────────
def fetch_url(session: requests.Session, url: str, dest: Path,
              is_page: bool = False) -> tuple[str, Path, bytes | None] | None:
    if dest.exists() and not is_page:
        return url, dest, None
    try:
        r = session.get(url, timeout=25, stream=True, allow_redirects=True)
        r.raise_for_status()
        dest.parent.mkdir(parents=True, exist_ok=True)
        raw = r.content
        with open(dest, "wb") as f:
            f.write(raw)
        ct = r.headers.get("Content-Type", "")
        print(f"  ✓ [{len(raw)//1024:>4}KB] {url}")
        return url, dest, raw if (is_page or "text" in ct) else None
    except Exception as e:
        print(f"  ✗ {url}  ({e})", file=sys.stderr)
        return None

# ── sitemap ──────────────────────────────────────────────────────────────────
def fetch_sitemap(session: requests.Session, base: str) -> list[str]:
    urls = []
    p    = urlparse(base)
    for path in ["/sitemap.xml", "/sitemap_index.xml", "/sitemap.txt"]:
        try:
            r = session.get(f"{p.scheme}://{p.netloc}{path}", timeout=10)
            if r.status_code == 200:
                if path.endswith(".txt"):
                    urls += [l.strip() for l in r.text.splitlines() if l.startswith("http")]
                else:
                    root_el = ElementTree.fromstring(r.content)
                    ns = {"sm": "http://www.sitemaps.org/schemas/sitemap/0.9"}
                    for loc in root_el.findall(".//sm:loc", ns):
                        if loc.text:
                            urls.append(loc.text.strip())
                print(f"  [sitemap] found {len(urls)} urls in {path}")
                break
        except Exception:
            pass
    return urls

# ── extract assets + links from HTML ────────────────────────────────────────
def extract(soup: BeautifulSoup, page_url: str,
            base_host: str) -> tuple[list[str], list[str]]:
    assets, links = [], []

    for tag, attrs in ASSET_ATTRS.items():
        for el in soup.find_all(tag):
            for attr in attrs:
                val = el.get(attr, "")
                if not val or val.startswith("data:"):
                    continue
                if attr == "srcset":
                    for part in val.split(","):
                        m = SRCSET_PART.match(part.strip())
                        if m:
                            u = normalise(m.group(1), page_url)
                            if u:
                                assets.append(u)
                else:
                    u = normalise(val, page_url)
                    if u:
                        if tag == "link" and el.get("rel", [""])[0] in ("stylesheet", "icon", "preload"):
                            assets.append(u)
                        elif tag in ("script", "img", "source", "video",
                                     "audio", "object", "embed", "track"):
                            assets.append(u)
                        elif tag == "a":
                            pass
                        elif tag == "iframe":
                            assets.append(u)

    # <a href> — internal page links
    for a in soup.find_all("a", href=True):
        u = normalise(a["href"], page_url)
        if u and is_same_domain(u, base_host):
            links.append(u)

    # inline style url()
    for el in soup.find_all(style=True):
        for m in CSS_URL.findall(el["style"]):
            u = normalise(m, page_url)
            if u:
                assets.append(u)

    # <form action> (search boxes)
    for form in soup.find_all("form", action=True):
        u = normalise(form["action"], page_url)
        if u and is_same_domain(u, base_host):
            links.append(u)

    return list(set(assets)), list(set(links))

# ── CSS second-pass ──────────────────────────────────────────────────────────
def css_assets(css_text: str, css_url: str) -> list[str]:
    found = []
    for m in CSS_URL.findall(css_text):
        if not m.startswith("data:"):
            u = normalise(m, css_url)
            if u:
                found.append(u)
    return found

# ── main crawler ─────────────────────────────────────────────────────────────
def grab(start_url: str, root: Path, max_depth: int = 4,
         workers: int = 16, delay: float = 0.0):

    session             = requests.Session()
    session.headers.update(HEADERS)
    session.verify      = True

    start_url           = normalise(start_url, start_url) or start_url
    base_host           = urlparse(start_url).netloc
    root.mkdir(parents=True, exist_ok=True)

    visited_pages: set[str]  = set()
    queued_assets: set[str]  = set()
    css_files: list[tuple[Path, str]] = []

    stats = {"pages": 0, "assets": 0, "errors": 0}

    # ── sitemap seed ─────────────────────────────────────────────────────────
    sitemap_urls = fetch_sitemap(session, start_url)

    # BFS page queue: (url, depth)
    page_queue: deque[tuple[str, int]] = deque([(start_url, 0)])
    for u in sitemap_urls:
        if is_same_domain(u, base_host):
            page_queue.append((u, 1))

    print(f"\n[sitegrab] target    → {start_url}")
    print(f"[sitegrab] max depth → {max_depth}")
    print(f"[sitegrab] threads   → {workers}")
    print(f"[sitegrab] output    → {root / base_host}/\n")
    t0 = time.time()

    while page_queue:
        batch = []
        while page_queue and len(batch) < workers * 2:
            url, depth = page_queue.popleft()
            if url in visited_pages:
                continue
            visited_pages.add(url)
            batch.append((url, depth))

        # fetch pages in parallel
        def fetch_page(item):
            url, depth = item
            dest = url_to_local(url, root)
            result = fetch_url(session, url, dest, is_page=True)
            if delay:
                time.sleep(delay)
            return result, url, depth

        with ThreadPoolExecutor(max_workers=workers) as pool:
            futs = [pool.submit(fetch_page, item) for item in batch]
            for fut in as_completed(futs):
                r = fut.result()
                if not r:
                    stats["errors"] += 1
                    continue
                result, page_url, depth = r
                if not result:
                    stats["errors"] += 1
                    continue
                stats["pages"] += 1

                _, dest_path, raw = result
                if raw is None:
                    continue
                try:
                    text = raw.decode("utf-8", errors="replace")
                except Exception:
                    continue

                soup = BeautifulSoup(text, "html.parser")
                new_assets, new_links = extract(soup, page_url, base_host)

                for a in new_assets:
                    if a not in queued_assets:
                        queued_assets.add(a)

                if depth < max_depth:
                    for lnk in new_links:
                        if lnk not in visited_pages:
                            page_queue.append((lnk, depth + 1))

    # ── download all assets in parallel ──────────────────────────────────────
    asset_list = list(queued_assets)
    print(f"\n[sitegrab] pages crawled → {stats['pages']}")
    print(f"[sitegrab] assets queued → {len(asset_list)}")
    print(f"[sitegrab] downloading assets...\n")

    with ThreadPoolExecutor(max_workers=workers) as pool:
        def dl(url):
            dest   = url_to_local(url, root)
            result = fetch_url(session, url, dest)
            if delay:
                time.sleep(delay / 4)
            return result

        futs = {pool.submit(dl, u): u for u in asset_list}
        for fut in as_completed(futs):
            r = fut.result()
            if r:
                stats["assets"] += 1
                _, p, raw = r
                if raw and p.suffix.lower() == ".css":
                    try:
                        css_files.append((p, futs[fut]))
                    except Exception:
                        pass
            else:
                stats["errors"] += 1

    # ── CSS sweep for fonts / bg images ──────────────────────────────────────
    wave2 = {}
    for css_path, css_url in css_files:
        try:
            text = css_path.read_text(encoding="utf-8", errors="replace")
            for u in css_assets(text, css_url):
                if u not in queued_assets:
                    wave2[u] = url_to_local(u, root)
        except Exception:
            pass

    if wave2:
        print(f"\n[sitegrab] CSS sweep → {len(wave2)} more assets\n")
        with ThreadPoolExecutor(max_workers=workers) as pool:
            futs = {pool.submit(fetch_url, session, u, p): u
                    for u, p in wave2.items()}
            for fut in as_completed(futs):
                r = fut.result()
                if r:
                    stats["assets"] += 1
                else:
                    stats["errors"] += 1

    elapsed = time.time() - t0
    print(f"\n{'─'*60}")
    print(f"[sitegrab] pages   : {stats['pages']}")
    print(f"[sitegrab] assets  : {stats['assets']}")
    print(f"[sitegrab] errors  : {stats['errors']}")
    print(f"[sitegrab] elapsed : {elapsed:.1f}s")
    print(f"[sitegrab] output  : {root / base_host}/")
    print(f"{'─'*60}\n")

# ── CLI ───────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description="SiteGrabber — full-site downloader")
    ap.add_argument("url",                          help="target URL")
    ap.add_argument("-o", "--output",  default="grabbed", help="output folder")
    ap.add_argument("-d", "--depth",   type=int, default=4, help="crawl depth (default 4)")
    ap.add_argument("-t", "--threads", type=int, default=16, help="parallel threads")
    ap.add_argument("--delay",         type=float, default=0.0, help="delay between requests (sec)")
    args = ap.parse_args()

    try:
        import bs4
    except ImportError:
        print("installing dependencies...")
        import subprocess
        subprocess.check_call([sys.executable, "-m", "pip", "install",
                               "requests", "beautifulsoup4", "--quiet"])

    grab(
        start_url = args.url,
        root      = Path(args.output),
        max_depth = args.depth,
        workers   = args.threads,
        delay     = args.delay,
    )
