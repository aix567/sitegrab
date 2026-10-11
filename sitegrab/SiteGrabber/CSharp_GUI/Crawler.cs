// language: C#, file: Crawler.cs
// async recursive website crawler — links, assets, sitemaps, CSS sweep

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using HtmlAgilityPack;

namespace SiteGrabber;

public class Crawler
{
    // ── events ────────────────────────────────────────────────────────────────
    public event Action<LogEntry>?       OnLog;
    public event Action<int, int, int>?  OnStats;      // pages, assets, errors
    public event Action<int>?            OnQueueSize;  // total known urls

    // ── state ─────────────────────────────────────────────────────────────────
    private CrawlOptions _opts = null!;
    private CrawlStats   _stats = new();
    private Uri          _base  = null!;
    private CancellationToken _ct;

    private readonly ConcurrentDictionary<string, byte> _visitedPages  = new();
    private readonly ConcurrentDictionary<string, byte> _queuedAssets  = new();
    private readonly ConcurrentBag<(string Local, string Url)> _cssFiles = [];

    private static readonly HttpClient _http = BuildClient();

    private static readonly HashSet<string> _assetExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",".jpeg",".png",".gif",".webp",".avif",".svg",".ico",".bmp",
        ".css",".js",".mjs",
        ".woff",".woff2",".ttf",".otf",".eot",
        ".mp4",".webm",".ogg",".mp3",".wav",
        ".pdf",".zip",".doc",".docx",".xls",".xlsx",
        ".json",".xml",".txt"
    };

    private static readonly Regex ReCssUrl = new(
        @"url\([""']?(?!data:)([^""'\)\s]+)[""']?\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── HTTP client ───────────────────────────────────────────────────────────
    private static HttpClient BuildClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect      = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.All,
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        return c;
    }

    // ── public entry point ────────────────────────────────────────────────────
    public async Task RunAsync(CrawlOptions opts, CancellationToken ct)
    {
        _opts  = opts;
        _ct    = ct;
        _stats = new CrawlStats();
        _visitedPages.Clear();
        _queuedAssets.Clear();
        _cssFiles.Clear();

        _base = new Uri(opts.StartUrl);

        Log(LogEntry.Note($"target    → {opts.StartUrl}"));
        Log(LogEntry.Note($"depth     → {opts.MaxDepth}"));
        Log(LogEntry.Note($"threads   → {opts.Threads}"));
        Log(LogEntry.Note($"output    → {opts.OutputDir}/{_base.Host}/"));
        Log(LogEntry.Note(""));

        // ── sitemap discovery ─────────────────────────────────────────────────
        var sitemapUrls = new List<string>();
        if (opts.ParseSitemap)
            sitemapUrls = await FetchSitemapAsync();

        // ── BFS page crawl ────────────────────────────────────────────────────
        var queue = new Queue<(string Url, int Depth)>();
        queue.Enqueue((opts.StartUrl, 0));
        foreach (var u in sitemapUrls) queue.Enqueue((u, 1));

        var semaphore = new SemaphoreSlim(opts.Threads);

        while (queue.Count > 0 && !ct.IsCancellationRequested)
        {
            var (url, depth) = queue.Dequeue();
            if (!_visitedPages.TryAdd(NormalizeUrl(url), 0)) continue;

            await semaphore.WaitAsync(ct);
            _ = Task.Run(async () =>
            {
                try
                {
                    var (html, localPath) = await FetchPageAsync(url);
                    if (html == null) return;

                    _stats.AddPage();
                    FireStats();
                    Log(LogEntry.Ok($"[page] {url}"));

                    if (opts.DelayMs > 0)
                        await Task.Delay(opts.DelayMs, ct);

                    // extract + queue assets
                    var (assets, links) = ExtractFromHtml(html, url);

                    var assetTasks = new List<Task>();
                    foreach (var a in assets)
                    {
                        if (_queuedAssets.TryAdd(NormalizeUrl(a), 0) && ShouldDownload(a))
                        {
                            var captured = a;
                            assetTasks.Add(DownloadAssetAsync(captured));
                        }
                    }
                    await Task.WhenAll(assetTasks);

                    // enqueue new page links
                    if (opts.FollowLinks && depth < opts.MaxDepth)
                    {
                        lock (queue)
                        {
                            foreach (var lnk in links)
                                if (!_visitedPages.ContainsKey(NormalizeUrl(lnk)))
                                    queue.Enqueue((lnk, depth + 1));
                        }
                    }
                }
                finally { semaphore.Release(); }
            }, ct);
        }

        // wait for all in-flight tasks
        for (int i = 0; i < opts.Threads; i++) await semaphore.WaitAsync(ct);

        // ── CSS sweep for fonts + bg images ───────────────────────────────────
        await CssSweepAsync();

        Log(LogEntry.Note(""));
        Log(LogEntry.Note($"pages   : {_stats.Pages}"));
        Log(LogEntry.Note($"assets  : {_stats.Assets}"));
        Log(LogEntry.Note($"errors  : {_stats.Errors}"));
        Log(LogEntry.Note($"output  : {opts.OutputDir}/{_base.Host}/"));
    }

    // ── sitemap ───────────────────────────────────────────────────────────────
    private async Task<List<string>> FetchSitemapAsync()
    {
        var found = new List<string>();
        string[] paths = ["/sitemap.xml", "/sitemap_index.xml", "/sitemap.txt"];

        foreach (var path in paths)
        {
            try
            {
                var sitemapUrl = $"{_base.Scheme}://{_base.Host}{path}";
                var res = await _http.GetAsync(sitemapUrl, _ct);
                if (!res.IsSuccessStatusCode) continue;

                var content = await res.Content.ReadAsStringAsync(_ct);

                if (path.EndsWith(".txt"))
                {
                    found.AddRange(content.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.StartsWith("http")));
                }
                else
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(content);
                    var ns = new XmlNamespaceManager(doc.NameTable);
                    ns.AddNamespace("sm", "http://www.sitemaps.org/schemas/sitemap/0.9");
                    var nodes = doc.SelectNodes("//sm:loc", ns);
                    if (nodes != null)
                        foreach (XmlNode n in nodes)
                            if (!string.IsNullOrEmpty(n.InnerText))
                                found.Add(n.InnerText.Trim());
                }

                Log(LogEntry.Note($"[sitemap] found {found.Count} URLs in {path}"));
                break;
            }
            catch { /* not found, try next */ }
        }
        return found;
    }

    // ── page fetch + save ─────────────────────────────────────────────────────
    private async Task<(string? Html, string Local)> FetchPageAsync(string url)
    {
        var local = UrlToLocalPath(url);
        try
        {
            var res = await _http.GetAsync(url, _ct);
            res.EnsureSuccessStatusCode();
            var bytes = await res.Content.ReadAsByteArrayAsync(_ct);
            SaveFile(local, bytes);
            var html = Encoding.UTF8.GetString(bytes);
            return (html, local);
        }
        catch (Exception ex)
        {
            _stats.AddError();
            FireStats();
            Log(LogEntry.Bad($"[page] {url}  ({ex.Message})"));
            return (null, local);
        }
    }

    // ── asset download ────────────────────────────────────────────────────────
    private async Task DownloadAssetAsync(string url)
    {
        var local = UrlToLocalPath(url);
        if (File.Exists(local)) return;
        try
        {
            var res   = await _http.GetAsync(url, _ct);
            res.EnsureSuccessStatusCode();
            var bytes = await res.Content.ReadAsByteArrayAsync(_ct);
            SaveFile(local, bytes);
            _stats.AddAsset();
            FireStats();
            Log(LogEntry.Ok($"[{SizeLabel(bytes.Length)}] {url}"));

            var ct = res.Content.Headers.ContentType?.MediaType ?? "";
            if (ct.Contains("css") || local.EndsWith(".css"))
                _cssFiles.Add((local, url));
        }
        catch (Exception ex)
        {
            _stats.AddError();
            FireStats();
            Log(LogEntry.Bad($"[asset] {url}  ({ex.Message})"));
        }
    }

    // ── CSS sweep ─────────────────────────────────────────────────────────────
    private async Task CssSweepAsync()
    {
        var wave2 = new ConcurrentDictionary<string, string>(); // url → local

        foreach (var (cssLocal, cssUrl) in _cssFiles)
        {
            if (!File.Exists(cssLocal)) continue;
            var text = await File.ReadAllTextAsync(cssLocal, _ct);
            foreach (Match m in ReCssUrl.Matches(text))
            {
                var raw = m.Groups[1].Value;
                if (raw.StartsWith("data:")) continue;
                var abs = ResolveUrl(raw, cssUrl);
                if (abs == null) continue;
                if (_queuedAssets.TryAdd(NormalizeUrl(abs), 0))
                    wave2.TryAdd(abs, UrlToLocalPath(abs));
            }
        }

        if (wave2.IsEmpty) return;
        Log(LogEntry.Note($"[css sweep] {wave2.Count} more assets"));

        var tasks = wave2.Keys.Select(DownloadAssetAsync);
        await Task.WhenAll(tasks);
    }

    // ── HTML extraction ───────────────────────────────────────────────────────
    private (List<string> Assets, List<string> Links) ExtractFromHtml(
        string html, string pageUrl)
    {
        var assets = new HashSet<string>();
        var links  = new HashSet<string>();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // assets from tags
        var assetMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["img"]    = ["src", "data-src", "data-lazy-src", "data-original"],
            ["script"] = ["src"],
            ["source"] = ["src"],
            ["video"]  = ["src", "poster"],
            ["audio"]  = ["src"],
            ["iframe"] = ["src"],
            ["object"] = ["data"],
            ["embed"]  = ["src"],
            ["track"]  = ["src"],
            ["link"]   = ["href"],
        };

        foreach (var (tag, attrs) in assetMap)
        {
            foreach (var node in doc.DocumentNode.SelectNodes($"//{tag}") ?? [])
            {
                foreach (var attr in attrs)
                {
                    var val = node.GetAttributeValue(attr, "");
                    if (string.IsNullOrWhiteSpace(val) || val.StartsWith("data:")) continue;

                    // for <link> only take stylesheet/icon/preload
                    if (tag == "link")
                    {
                        var rel = node.GetAttributeValue("rel", "");
                        if (!rel.Contains("stylesheet") && !rel.Contains("icon") &&
                            !rel.Contains("preload"))
                            continue;
                    }

                    // srcset handling
                    if (attr == "srcset")
                    {
                        foreach (var part in val.Split(','))
                        {
                            var src = part.Trim().Split(' ')[0];
                            var u   = ResolveUrl(src, pageUrl);
                            if (u != null) assets.Add(u);
                        }
                    }
                    else
                    {
                        var u = ResolveUrl(val, pageUrl);
                        if (u != null) assets.Add(u);
                    }
                }
            }
        }

        // inline style url()
        foreach (var node in doc.DocumentNode
                     .SelectNodes("//*[@style]") ?? [])
        {
            var style = node.GetAttributeValue("style", "");
            foreach (Match m in ReCssUrl.Matches(style))
            {
                var u = ResolveUrl(m.Groups[1].Value, pageUrl);
                if (u != null) assets.Add(u);
            }
        }

        // <a href> internal links
        foreach (var node in doc.DocumentNode.SelectNodes("//a[@href]") ?? [])
        {
            var href = node.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#")) continue;
            var u = ResolveUrl(href, pageUrl);
            if (u != null && IsSameDomain(u)) links.Add(u);
        }

        // <form action> (search forms, filter POSTs shown as GET)
        foreach (var node in doc.DocumentNode.SelectNodes("//form[@action]") ?? [])
        {
            var action = node.GetAttributeValue("action", "");
            var u = ResolveUrl(action, pageUrl);
            if (u != null && IsSameDomain(u)) links.Add(u);
        }

        // pagination: look for ?page=N, ?p=N, ?offset=N patterns in existing links
        var paginatedLinks = links
            .Where(l => l.Contains("?"))
            .SelectMany(l => ExpandPagination(l))
            .ToList();
        foreach (var p in paginatedLinks) links.Add(p);

        return ([..assets], [..links]);
    }

    // ── pagination expander — follow pages 2-10 for search/product listings ──
    private IEnumerable<string> ExpandPagination(string url)
    {
        var patterns = new[] { "page", "p", "pg", "offset", "start" };
        var uri = new Uri(url);
        var q   = System.Web.HttpUtility.ParseQueryString(uri.Query);

        foreach (var key in patterns)
        {
            var val = q[key];
            if (val == null || !int.TryParse(val, out var cur)) continue;

            for (int i = cur + 1; i <= cur + 9; i++)
            {
                q[key] = i.ToString();
                var builder = new UriBuilder(uri) { Query = q.ToString() };
                yield return builder.Uri.ToString();
            }
            yield break;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private string? ResolveUrl(string raw, string baseUrl)
    {
        raw = raw.Trim().Split(' ')[0]; // srcset pair
        if (string.IsNullOrEmpty(raw) || raw.StartsWith("javascript:")) return null;
        try
        {
            var b   = new Uri(baseUrl);
            var abs = new Uri(b, raw);
            if (abs.Scheme != "http" && abs.Scheme != "https") return null;
            return abs.GetLeftPart(UriPartial.Query); // strip fragment
        }
        catch { return null; }
    }

    private bool IsSameDomain(string url)
    {
        try
        {
            var h = new Uri(url).Host;
            return h == _base.Host || h.EndsWith("." + _base.Host);
        }
        catch { return false; }
    }

    private static string NormalizeUrl(string url)
        => url.TrimEnd('/').ToLowerInvariant();

    private bool ShouldDownload(string url)
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLower();

        if (string.IsNullOrEmpty(ext)) return true; // no extension = probably a page

        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or
            ".webp" or ".avif" or ".svg" or ".ico" or ".bmp" => _opts.SaveImages,
            ".css"                                             => _opts.SaveCSS,
            ".js"  or ".mjs"                                   => _opts.SaveJS,
            ".woff" or ".woff2" or ".ttf" or ".otf" or ".eot" => _opts.SaveFonts,
            ".mp4" or ".webm" or ".ogg" or ".mp3" or ".wav"   => _opts.SaveVideos,
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx"  => _opts.SaveDocs,
            _ => true
        };
    }

    private string UrlToLocalPath(string url)
    {
        try
        {
            var u    = new Uri(url);
            var rel  = u.AbsolutePath.TrimStart('/');
            if (string.IsNullOrEmpty(rel) || rel.EndsWith('/'))
                rel += "index.html";
            if (string.IsNullOrEmpty(Path.GetExtension(rel)))
                rel += ".html";

            // embed query string safely
            if (!string.IsNullOrEmpty(u.Query))
            {
                var q    = Regex.Replace(u.Query.TrimStart('?'), @"[^\w=&]", "_");
                if (q.Length > 60) q = q[..60];
                var ext  = Path.GetExtension(rel);
                var stem = rel[..^ext.Length];
                rel = $"{stem}_{q}{ext}";
            }

            // sanitize
            rel = rel.Replace(":", "_");
            return Path.Combine(_opts.OutputDir, u.Host, rel);
        }
        catch { return Path.Combine(_opts.OutputDir, "misc", Guid.NewGuid() + ".bin"); }
    }

    private static void SaveFile(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private static string SizeLabel(int bytes) =>
        bytes >= 1_048_576 ? $"{bytes / 1_048_576}MB"
      : bytes >= 1_024     ? $"{bytes / 1_024}KB"
      : $"{bytes}B";

    private void Log(LogEntry entry) => OnLog?.Invoke(entry);

    private void FireStats() =>
        OnStats?.Invoke(_stats.Pages, _stats.Assets, _stats.Errors);
}
