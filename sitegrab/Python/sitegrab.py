#!/usr/bin/env python3
# language: Python 3.10+, file: sitegrab3.py
# full-site mirror engine — static + dynamic + JS-rendered + product enumeration

import sys, re, time, json, hashlib, asyncio, argparse
from pathlib import Path
from urllib.parse import urljoin, urlparse, unquote, urlunparse, parse_qs, urlencode
from concurrent.futures import ThreadPoolExecutor, as_completed
from collections import deque
from xml.etree import ElementTree
from dataclasses import dataclass, field, asdict

import requests
from bs4 import BeautifulSoup

# ── config ───────────────────────────────────────────────────────────────────
ASSET_ATTRS = {
    "img":    ["src", "data-src", "data-lazy-src", "data-original", "srcset"],
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
CSS_URL     = re.compile(r'url\(["\']?([^"\')\s]+)["\']?\)', re.I)
SRCSET_PART = re.compile(r'(\S+)(?:\s+[\d.]+[wxh])?')
PAGINATION_KEYS = ["page", "p", "pg", "offset", "start", "skip", "from", "pageno", "page_num"]
MAX_PAGES_PER_KEY = 500          # exhaustion ceiling per pagination key
MAX_TOTAL_PAGES   = 200_000      # absolute crawl ceiling — prevents runaway

HEADERS = {
    "User-Agent": ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                   "AppleWebKit/537.36 (KHTML, like Gecko) "
                   "Chrome/128.0.0.0 Safari/537.36"),
    "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    "Accept-Language": "en-US,en;q=0.5",
    "Accept-Encoding": "gzip, deflate, br",
    "Connection": "keep-alive",
}

# ── URL helpers ──────────────────────────────────────────────────────────────
def normalise(url: str, base: str) -> str | None:
    try:
        full = urljoin(base, url.strip().split()[0])
        p    = urlparse(full)
        if p.scheme not in ("http", "https"):
            return None
        return urlunparse(p._replace(fragment=""))
    except Exception:
        return None

def is_same_domain(url: str, base_host: str) -> bool:
    h = urlparse(url).netloc
    return h == base_host or h.endswith("." + base_host)

def url_to_local(url: str, root: Path) -> Path:
    p   = urlparse(url)
    rel = unquote(p.path).lstrip("/") or "index.html"
    if not Path(rel).suffix:
        rel = rel.rstrip("/") + ".html"
    if p.query:
        safe_q = re.sub(r'[^\w=&-]', '_', p.query)[:80]
        stem   = Path(rel).stem + "_" + safe_q
        rel    = str(Path(rel).with_name(stem + Path(rel).suffix))
    # sanitize windows-unsafe chars
    rel = re.sub(r'[<>:"|?*]', '_', rel)
    return root / p.netloc / rel

# ── fetch layer ──────────────────────────────────────────────────────────────
class Fetcher:
    def __init__(self, delay: float = 0.0, timeout: int = 25):
        self.session = requests.Session()
        self.session.headers.update(HEADERS)
        self.delay = delay
        self.timeout = timeout
        self._last = 0.0

    def _throttle(self):
        if self.delay > 0:
            wait = self.delay - (time.time() - self._last)
            if wait > 0:
                time.sleep(wait)
        self._last = time.time()

    def get(self, url: str, stream: bool = False):
        self._throttle()
        try:
            r = self.session.get(url, timeout=self.timeout,
                                 stream=stream, allow_redirects=True)
            r.raise_for_status()
            return r
        except Exception as e:
            return None

    def fetch_to(self, url: str, dest: Path, is_page: bool = False):
        if dest.exists() and not is_page:
            return url, dest, None
        r = self.get(url, stream=True)
        if r is None:
            return None
        dest.parent.mkdir(parents=True, exist_ok=True)
        raw = r.content
        dest.write_bytes(raw)
        ct = r.headers.get("Content-Type", "")
        readable = is_page or "text" in ct or "json" in ct or "xml" in ct or "javascript" in ct
        return url, dest, raw if readable else None

# ── content extraction ───────────────────────────────────────────────────────
def extract_assets_and_links(soup: BeautifulSoup, page_url: str,
                             base_host: str):
    assets, links = set(), set()

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
                            if u: assets.add(u)
                else:
                    u = normalise(val, page_url)
                    if not u: continue
                    if tag == "link":
                        rel = el.get("rel", [""])
                        rel = rel[0] if isinstance(rel, list) else rel
                        if rel in ("stylesheet", "icon", "preload", "manifest"):
                            assets.add(u)
                    elif tag == "a":
                        if is_same_domain(u, base_host):
                            links.add(u)
                    else:
                        assets.add(u)

    # inline style
    for el in soup.find_all(style=True):
        for m in CSS_URL.findall(el["style"]):
            u = normalise(m, page_url)
            if u: assets.add(u)

    # forms → GET replay
    for form in soup.find_all("form"):
        action = form.get("action") or page_url
        method = (form.get("method") or "get").lower()
        if method == "get":
            u = normalise(action, page_url)
            if u and is_same_domain(u, base_host):
                links.add(u)

    # every <a> including external-as-internal (query params kept)
    for a in soup.find_all("a", href=True):
        u = normalise(a["href"], page_url)
        if u and is_same_domain(u, base_host):
            links.add(u)

    return assets, links

def extract_products(soup: BeautifulSoup, page_url: str) -> list[dict]:
    """pull structured product data from JSON-LD, og tags, microdata."""
    found = []

    # JSON-LD
    for script in soup.find_all("script", type="application/ld+json"):
        try:
            data = json.loads(script.string or "{}")
            items = data if isinstance(data, list) else [data]
            for item in items:
                if not isinstance(item, dict): continue
                if item.get("@type") in ("Product", "Offer", "ItemPage"):
                    found.append({"kind": "json-ld", "url": page_url, "data": item})
                # @graph
                for sub in item.get("@graph", []):
                    if isinstance(sub, dict) and sub.get("@type") == "Product":
                        found.append({"kind": "json-ld", "url": page_url, "data": sub})
        except Exception:
            pass

    # og:product / og:price
    og = {}
    for m in soup.find_all("meta"):
        prop = m.get("property") or m.get("name") or ""
        if prop.startswith(("og:", "product:", "twitter:")):
            og[prop] = m.get("content", "")
    if any(k.startswith("product:") for k in og) or "og:price:amount" in og:
        found.append({"kind": "opengraph", "url": page_url, "data": og})

    return found

# ── pagination exhaustion ────────────────────────────────────────────────────
def expand_pagination(url: str, ceiling: int = MAX_PAGES_PER_KEY) -> list[str]:
    """given a URL with ?page=N, generate N+1 .. ceiling."""
    p = urlparse(url)
    if not p.query:
        return []
    q = parse_qs(p.query, keep_blank_values=True)
    out = []
    for key in PAGINATION_KEYS:
        if key not in q: continue
        try:
            cur = int(q[key][0])
        except (ValueError, IndexError):
            continue
        # also handle 0-based offsets (start, offset, skip)
        step = 10 if key in ("start", "offset", "skip") else 1
        for n in range(cur + step, cur + step * ceiling, step):
            new_q = dict(q)
            new_q[key] = [str(n)]
            new_url = urlunparse(p._replace(query=urlencode(new_q, doseq=True)))
            out.append(new_url)
        break
    return out

# ── headless render (SPA support) ────────────────────────────────────────────
class Renderer:
    """lazy-init Playwright. only spins up if --render is passed."""
    def __init__(self):
        self._pw = None
        self._browser = None

    def start(self):
        if self._browser: return
        from playwright.sync_api import sync_playwright
        self._pw = sync_playwright().start()
        self._browser = self._pw.chromium.launch(headless=True)

    def render(self, url: str, wait_ms: int = 1500) -> str | None:
        self.start()
        try:
            page = self._browser.new_page()
            page.goto(url, wait_until="networkidle", timeout=30_000)
            page.wait_for_timeout(wait_ms)
            html = page.content()
            page.close()
            return html
        except Exception:
            return None

    def stop(self):
        if self._browser: self._browser.close()
        if self._pw: self._pw.stop()

# ── resume state ─────────────────────────────────────────────────────────────
class ResumeState:
    def __init__(self, path: Path):
        self.path = path
        self.visited: set[str] = set()
        self.queue:   list[tuple[str, int]] = []
        self.assets:  set[str] = set()
        self.products: list[dict] = []
        if path.exists():
            self._load()

    def _load(self):
        try:
            data = json.loads(self.path.read_text())
            self.visited  = set(data.get("visited", []))
            self.queue    = [tuple(x) for x in data.get("queue", [])]
            self.assets   = set(data.get("assets", []))
            self.products = data.get("products", [])
        except Exception:
            pass

    def save(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps({
            "visited":  list(self.visited),
            "queue":    [list(x) for x in self.queue],
            "assets":   list(self.assets),
            "products": self.products,
        }))

# ── main engine ──────────────────────────────────────────────────────────────
@dataclass
class Options:
    start_url: str
    output: Path = Path("grabbed")
    depth: int = 6
    threads: int = 16
    delay: float = 0.0
    render: bool = False          # headless chromium for SPA
    exhaust_pagination: bool = True
    extract_products: bool = True
    same_domain_only: bool = True
    resume: bool = True
    max_pages: int = MAX_TOTAL_PAGES

class SiteGrabber:
    def __init__(self, opts: Options):
        self.opts = opts
        self.base_host = urlparse(opts.start_url).netloc
        self.state = ResumeState(opts.output / self.base_host / ".sitegrab_state.json") \
                     if opts.resume else ResumeState(Path("/dev/null"))
        self.fetcher = Fetcher(delay=opts.delay)
        self.renderer = Renderer() if opts.render else None
        self.product_manifest: list[dict] = list(self.state.products)

    def run(self):
        print(f"\n[sitegrab v3] target     → {self.opts.start_url}")
        print(f"[sitegrab v3] depth      → {self.opts.depth}")
        print(f"[sitegrab v3] threads    → {self.opts.threads}")
        print(f"[sitegrab v3] render     → {'headless chromium' if self.opts.render else 'static HTTP only'}")
        print(f"[sitegrab v3] pagination → {'exhaust' if self.opts.exhaust_pagination else 'follow links only'}")
        print(f"[sitegrab v3] products   → {self.opts.extract_products}")
        print(f"[sitegrab v3] output     → {self.opts.output / self.base_host}/\n")

        t0 = time.time()

        # seed queue
        if self.state.queue:
            page_queue = deque(self.state.queue)
            print(f"[resume] {len(page_queue)} pages from prior run")
        else:
            page_queue = deque([(self.opts.start_url, 0)])
            # sitemap seed
            for u in self._fetch_sitemaps():
                if is_same_domain(u, self.base_host):
                    page_queue.append((u, 1))

        visited = self.state.visited
        queued_assets = self.state.assets
        pages_done = 0

        while page_queue and pages_done < self.opts.max_pages:
            batch = []
            while page_queue and len(batch) < self.opts.threads * 2:
                url, depth = page_queue.popleft()
                if url in visited: continue
                visited.add(url)
                batch.append((url, depth))

            if not batch: break

            # ── fetch page batch ─────────────────────────────────────────────
            results = []
            with ThreadPoolExecutor(max_workers=self.opts.threads) as pool:
                futs = [pool.submit(self._fetch_page, url, depth) for url, depth in batch]
                for f in as_completed(futs):
                    results.append(f.result())

            for res in results:
                if not res: continue
                url, depth, dest, raw = res
                pages_done += 1
                print(f"  [{pages_done}] page {url}")

                if raw is None: continue
                html = raw.decode("utf-8", errors="replace")
                soup = BeautifulSoup(html, "lxml")

                # product extraction
                if self.opts.extract_products:
                    prods = extract_products(soup, url)
                    if prods:
                        self.product_manifest.extend(prods)
                        print(f"       + {len(prods)} product records")

                assets, links = extract_assets_and_links(soup, url, self.base_host)

                # queue assets
                new_assets = assets - queued_assets
                queued_assets |= assets

                # pagination exhaustion
                if self.opts.exhaust_pagination:
                    for lnk in list(links):
                        for extra in expand_pagination(lnk):
                            if extra not in visited:
                                links.add(extra)

                # enqueue child pages
                if depth < self.opts.depth:
                    for lnk in links:
                        if lnk not in visited:
                            page_queue.append((lnk, depth + 1))

            # ── download assets in parallel ──────────────────────────────────
            if new_assets:
                with ThreadPoolExecutor(max_workers=self.opts.threads) as pool:
                    futs = [pool.submit(self._fetch_asset, u) for u in new_assets]
                    for _ in as_completed(futs):
                        pass

            # checkpoint
            self.state.visited = visited
            self.state.queue   = list(page_queue)
            self.state.assets  = queued_assets
            self.state.products = self.product_manifest
            self.state.save()

        # ── css sweep ────────────────────────────────────────────────────────
        self._css_sweep(queued_assets)

        # ── write product manifest ───────────────────────────────────────────
        if self.product_manifest:
            manifest = self.opts.output / self.base_host / "_products.json"
            manifest.parent.mkdir(parents=True, exist_ok=True)
            manifest.write_text(json.dumps(self.product_manifest, indent=2))
            print(f"\n[sitegrab v3] products → {manifest}")

        elapsed = time.time() - t0
        print(f"\n{'─'*60}")
        print(f"[sitegrab v3] pages    : {pages_done}")
        print(f"[sitegrab v3] assets   : {len(queued_assets)}")
        print(f"[sitegrab v3] products : {len(self.product_manifest)}")
        print(f"[sitegrab v3] elapsed  : {elapsed:.1f}s")
        print(f"[sitegrab v3] output   : {self.opts.output / self.base_host}/")
        print(f"{'─'*60}\n")

    def _fetch_page(self, url: str, depth: int):
        dest = url_to_local(url, self.opts.output)
        try:
            if self.opts.render:
                html = self.renderer.render(url)
                if html:
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    raw = html.encode("utf-8")
                    dest.write_bytes(raw)
                    return url, depth, dest, raw
            result = self.fetcher.fetch_to(url, dest, is_page=True)
            if result is None: return None
            _, d, raw = result
            return url, depth, d, raw
        except Exception as e:
            print(f"  ✗ page {url}  ({e})")
            return None

    def _fetch_asset(self, url: str):
        dest = url_to_local(url, self.opts.output)
        try:
            r = self.fetcher.fetch_to(url, dest)
            if r: print(f"       asset {url}")
        except Exception:
            pass

    def _fetch_sitemaps(self) -> list[str]:
        urls = []
        p = urlparse(self.opts.start_url)
        for path in ("/sitemap.xml", "/sitemap_index.xml", "/sitemap.txt",
                     "/sitemap-products.xml", "/product-sitemap.xml"):
            try:
                r = self.fetcher.get(f"{p.scheme}://{p.netloc}{path}")
                if r is None: continue
                if path.endswith(".txt"):
                    urls += [l.strip() for l in r.text.splitlines() if l.startswith("http")]
                else:
                    root = ElementTree.fromstring(r.content)
                    ns = {"sm": "http://www.sitemaps.org/schemas/sitemap/0.9"}
                    for loc in root.findall(".//sm:loc", ns):
                        if loc.text:
                            urls.append(loc.text.strip())
                print(f"  [sitemap] {path} → {len(urls)} urls")
            except Exception:
                pass
        return urls

    def _css_sweep(self, already: set[str]):
        wave2 = {}
        css_dir = self.opts.output / self.base_host
        for css_path in css_dir.rglob("*.css"):
            try:
                text = css_path.read_text(encoding="utf-8", errors="replace")
                for m in CSS_URL.findall(text):
                    if m.startswith("data:"): continue
                    # resolve against original css url — approximate
                    u = normalise(m, f"https://{self.base_host}/")
                    if u and u not in already:
                        wave2[u] = url_to_local(u, self.opts.output)
            except Exception:
                pass
        if not wave2: return
        print(f"\n[css sweep] {len(wave2)} additional assets")
        with ThreadPoolExecutor(max_workers=self.opts.threads) as pool:
            futs = [pool.submit(self._fetch_asset, u) for u in wave2]
            for _ in as_completed(futs):
                pass
        already |= set(wave2)

# ── CLI ──────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="SiteGrabber v3 — full-site mirror")
    ap.add_argument("url")
    ap.add_argument("-o", "--output",   default="grabbed")
    ap.add_argument("-d", "--depth",    type=int, default=6)
    ap.add_argument("-t", "--threads",  type=int, default=16)
    ap.add_argument("--delay",          type=float, default=0.0)
    ap.add_argument("--render",         action="store_true", help="headless chromium for SPAs")
    ap.add_argument("--no-paginate",    action="store_true", help="don't exhaust pagination keys")
    ap.add_argument("--no-products",    action="store_true", help="skip product extraction")
    ap.add_argument("--no-resume",      action="store_true", help="ignore prior crawl state")
    ap.add_argument("--max-pages",      type=int, default=200_000)
    args = ap.parse_args()

    opts = Options(
        start_url = args.url,
        output    = Path(args.output),
        depth     = args.depth,
        threads   = args.threads,
        delay     = args.delay,
        render    = args.render,
        exhaust_pagination = not args.no_paginate,
        extract_products   = not args.no_products,
        resume    = not args.no_resume,
        max_pages = args.max_pages,
    )

    SiteGrabber(opts).run()