// language: C#, file: Crawler.cs
// SiteGrabber v3 — async recursive crawler

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using HtmlAgilityPack;

using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace SiteGrabber;

public class Crawler
{
    public event Action<LogEntry>?      OnLog;
    public event Action<int, int, int>? OnStats;

    private CrawlOptions _opts = null!;
    private CrawlStats   _stats = new();
    private Uri          _base  = null!;
    private CancellationToken _ct;

    private readonly ConcurrentDictionary<string, byte> _visitedPages = new();
    private readonly ConcurrentDictionary<string, byte> _queuedAssets = new();
    private readonly ConcurrentBag<(string Local, string Url)> _cssFiles = [];

    private static readonly HttpClient _http = BuildClient();

    private static readonly Regex ReCssUrl = new(
        @"url\([""']?(?!data:)([^""'\)\s]+)[""']?\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] PaginationKeys =
    {
        "page", "p", "pg", "offset", "start", "skip",
        "from", "pageno", "page_num"
    };

    private const int PagesPerKeyCeiling = 500;

    private static HttpClient BuildClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect        = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression   = DecompressionMethods.All,
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

        var sitemapUrls = new List<string>();
        if (opts.ParseSitemap)
            sitemapUrls = await FetchSitemapAsync();

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
                    var (html, _) = await FetchPageAsync(url);
                    if (html == null) return;

                    _stats.AddPage();
                    FireStats();

                    if (opts.DelayMs > 0)
                        await Task.Delay(opts.DelayMs, ct);

                    var (assets, links) = ExtractFromHtml(html, url);

                    var assetTasks = new List<Task>();
                    foreach (var a in assets)
                    {
                        if (_queuedAssets.TryAdd(NormalizeUrl(a), 0) && ShouldDownload(a))
                            assetTasks.Add(DownloadAssetAsync(a));
                    }
                    await Task.WhenAll(assetTasks);

                    if (opts.ExhaustPagination)
                    {
                        var expanded = new List<string>();
                        foreach (var lnk in links)
                            expanded.AddRange(ExpandPagination(lnk));
                        links.AddRange(expanded);
                    }

                    if (opts.FollowLinks && depth < opts.MaxDepth)
                    {
                        foreach (var lnk in links)
                            if (!_visitedPages.ContainsKey(NormalizeUrl(lnk)))
                                queue.Enqueue((lnk, depth + 1));
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log(LogEntry.Bad($"[task] {url}  ({ex.Message})"));
                }
                finally { semaphore.Release(); }
            }, ct);
        }

        for (int i = 0; i < opts.Threads; i++)
            await semaphore.WaitAsync(ct);

        await CssSweepAsync();

        // ── link rewriting pass ──────────────────────────────────────────────
        Log(LogEntry.Note(""));
        Log(LogEntry.Note("[rewrite] scanning mirrored HTML..."));

        var (htmlFiles, htmlRewrites) = LinkRewriter.RewriteAll(opts.OutputDir, _base.Host);
        Log(LogEntry.Ok($"[rewrite] {htmlRewrites} URLs in {htmlFiles} HTML files"));

        Log(LogEntry.Note("[rewrite] scanning mirrored CSS..."));
        var (cssFileCount, cssRewrites) = LinkRewriter.RewriteCss(opts.OutputDir, _base.Host);
        Log(LogEntry.Ok($"[rewrite] {cssRewrites} URLs in {cssFileCount} CSS files"));

        Log(LogEntry.Note(""));
        Log(LogEntry.Note($"pages   : {_stats.Pages}"));
        Log(LogEntry.Note($"assets  : {_stats.Assets}"));
        Log(LogEntry.Note($"errors  : {_stats.Errors}"));
        Log(LogEntry.Note($"output  : {opts.OutputDir}/{_base.Host}/"));
    }

    private async Task<List<string>> FetchSitemapAsync()
    {
        var found = new List<string>();
        string[] paths =
        {
            "/sitemap.xml", "/sitemap_index.xml", "/sitemap.txt",
            "/sitemap-products.xml", "/product-sitemap.xml"
        };

        foreach (var path in paths)
        {
            try
            {
                var sitemapUrl = $"{_base.Scheme}://{_base.Host}{path}";
                var res = await _http.GetAsync(sitemapUrl, _ct);
                if (!res.IsSuccessStatusCode) continue;

                var content = await res.Content.ReadAsStringAsync(_ct);
                var before  = found.Count;

                if (path.EndsWith(".txt"))
                {
                    foreach (var l in content.Split('\n'))
                    {
                        var t = l.Trim();
                        if (t.StartsWith("http")) found.Add(t);
                    }
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

                Log(LogEntry.Note($"[sitemap] {path} → {found.Count - before} urls"));
            }
            catch { }
        }
        return found;
    }

    private async Task<(string? Html, string Local)> FetchPageAsync(string url)
    {
        var local = UrlToLocalPath(url);
        try
        {
            var res = await _http.GetAsync(url, _ct);
            res.EnsureSuccessStatusCode();
            var bytes = await res.Content.ReadAsByteArrayAsync(_ct);
            SaveFile(local, bytes);
            Log(LogEntry.Ok($"[page] {url}"));
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

    private async Task DownloadAssetAsync(string url)
    {
        var local = UrlToLocalPath(url);
        if (File.Exists(local)) return;
        try
        {
            var res = await _http.GetAsync(url, _ct);
            res.EnsureSuccessStatusCode();
            var bytes = await res.Content.ReadAsByteArrayAsync(_ct);
            SaveFile(local, bytes);
            _stats.AddAsset();
            FireStats();

            var ct = res.Content.Headers.ContentType?.MediaType ?? "";
            if (ct.Contains("css") || local.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                _cssFiles.Add((local, url));
        }
        catch (Exception ex)
        {
            _stats.AddError();
            FireStats();
            Log(LogEntry.Bad($"[asset] {url}  ({ex.Message})"));
        }
    }

    private async Task CssSweepAsync()
    {
        var wave2 = new ConcurrentDictionary<string, string>();

        foreach (var (cssLocal, cssUrl) in _cssFiles)
        {
            if (!File.Exists(cssLocal)) continue;
            string text;
            try { text = await File.ReadAllTextAsync(cssLocal, _ct); }
            catch { continue; }

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

    private (List<string> Assets, List<string> Links) ExtractFromHtml(
        string html, string pageUrl)
    {
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var assetMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["img"]    = new[] { "src", "data-src", "data-lazy-src", "data-original", "srcset" },
            ["script"] = new[] { "src" },
            ["source"] = new[] { "src", "srcset" },
            ["video"]  = new[] { "src", "poster" },
            ["audio"]  = new[] { "src" },
            ["iframe"] = new[] { "src" },
            ["object"] = new[] { "data" },
            ["embed"]  = new[] { "src" },
            ["track"]  = new[] { "src" },
            ["link"]   = new[] { "href" },
        };

        foreach (var kv in assetMap)
        {
            var tag   = kv.Key;
            var attrs = kv.Value;
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes == null) continue;

            foreach (var node in nodes)
            {
                foreach (var attr in attrs)
                {
                    var val = node.GetAttributeValue(attr, "");
                    if (string.IsNullOrWhiteSpace(val) || val.StartsWith("data:"))
                        continue;

                    if (tag == "link")
                    {
                        var rel = node.GetAttributeValue("rel", "");
                        if (!rel.Contains("stylesheet") &&
                            !rel.Contains("icon") &&
                            !rel.Contains("preload") &&
                            !rel.Contains("manifest"))
                            continue;
                    }

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

        var styled = doc.DocumentNode.SelectNodes("//*[@style]");
        if (styled != null)
        {
            foreach (var node in styled)
            {
                var style = node.GetAttributeValue("style", "");
                foreach (Match m in ReCssUrl.Matches(style))
                {
                    var u = ResolveUrl(m.Groups[1].Value, pageUrl);
                    if (u != null) assets.Add(u);
                }
            }
        }

        var anchorNodes = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchorNodes != null)
        {
            foreach (var node in anchorNodes)
            {
                var href = node.GetAttributeValue("href", "");
                if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#"))
                    continue;
                var u = ResolveUrl(href, pageUrl);
                if (u != null && IsSameDomain(u)) links.Add(u);
            }
        }

        var formNodes = doc.DocumentNode.SelectNodes("//form[@action]");
        if (formNodes != null)
        {
            foreach (var node in formNodes)
            {
                var action = node.GetAttributeValue("action", "");
                var u = ResolveUrl(action, pageUrl);
                if (u != null && IsSameDomain(u)) links.Add(u);
            }
        }

        return (new List<string>(assets), new List<string>(links));
    }

    private static IEnumerable<string> ExpandPagination(string url)
    {
        Uri uri;
        try { uri = new Uri(url); } catch { yield break; }

        if (string.IsNullOrEmpty(uri.Query)) yield break;

        var q = HttpUtility.ParseQueryString(uri.Query);

        foreach (var key in PaginationKeys)
        {
            var val = q[key];
            if (val == null) continue;
            if (!int.TryParse(val, out var cur)) continue;

            int step = (key == "offset" || key == "start" || key == "skip") ? 10 : 1;

            for (int i = 1; i <= PagesPerKeyCeiling; i++)
            {
                int next = cur + step * i;
                q[key] = next.ToString();
                var builder = new UriBuilder(uri) { Query = q.ToString() };
                yield return builder.Uri.ToString();
            }
            yield break;
        }
    }

    private string? ResolveUrl(string raw, string baseUrl)
    {
        raw = raw.Trim().Split(' ')[0];
        if (string.IsNullOrEmpty(raw) || raw.StartsWith("javascript:"))
            return null;
        try
        {
            var b   = new Uri(baseUrl);
            var abs = new Uri(b, raw);
            if (abs.Scheme != "http" && abs.Scheme != "https") return null;
            return abs.GetLeftPart(UriPartial.Query);
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
        string ext;
        try { ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant(); }
        catch { return false; }

        if (string.IsNullOrEmpty(ext)) return true;

        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or
            ".webp" or ".avif" or ".svg" or ".ico" or ".bmp" => _opts.SaveImages,
            ".css"                                             => _opts.SaveCSS,
            ".js"  or ".mjs"                                   => _opts.SaveJS,
            ".woff" or ".woff2" or ".ttf" or ".otf" or ".eot" => _opts.SaveFonts,
            ".mp4" or ".webm" or ".ogg" or ".mp3" or ".wav"   => _opts.SaveVideos,
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx"  => _opts.SaveDocs,
            _                                                  => true
        };
    }

    private string UrlToLocalPath(string url)
    {
        try
        {
            var u   = new Uri(url);
            var rel = u.AbsolutePath.TrimStart('/');
            if (string.IsNullOrEmpty(rel) || rel.EndsWith('/'))
                rel += "index.html";
            if (string.IsNullOrEmpty(Path.GetExtension(rel)))
                rel += ".html";

            if (!string.IsNullOrEmpty(u.Query))
            {
                var q = Regex.Replace(u.Query.TrimStart('?'), @"[^\w=&-]", "_");
                if (q.Length > 80) q = q[..80];
                var ext  = Path.GetExtension(rel);
                var stem = rel[..^ext.Length];
                rel = $"{stem}_{q}{ext}";
            }

            rel = rel.Replace(":", "_");
            return Path.Combine(_opts.OutputDir, u.Host, rel);
        }
        catch
        {
            return Path.Combine(_opts.OutputDir, "misc", Guid.NewGuid() + ".bin");
        }
    }

    private static void SaveFile(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private void Log(LogEntry entry) => OnLog?.Invoke(entry);

    private void FireStats() =>
        OnStats?.Invoke(_stats.Pages, _stats.Assets, _stats.Errors);
}