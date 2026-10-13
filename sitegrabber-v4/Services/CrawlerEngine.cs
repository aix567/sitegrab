// language: C#, file: Services/CrawlerEngine.cs
// Orchestrates the C++ sitegrab.exe subprocess; falls back to built-in HttpClient crawler
// when the binary isn't found so the app still runs out-of-box.

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web;
using SiteGrabberV3.Models;

namespace SiteGrabberV3.Services;

public class CrawlerEngine
{
    private readonly AppState _state;
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    }) { Timeout = TimeSpan.FromSeconds(20) };

    public CrawlerEngine(AppState state) => _state = state;

    // ── Entry point ───────────────────────────────────────────────────────────
    public async Task RunAsync(string url, int depth, CancellationToken ct)
    {
        var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sitegrab.exe");
        try
        {
            if (File.Exists(exePath))
                await RunSubprocessAsync(exePath, url, depth, ct);
            else
                await RunBuiltInAsync(url, depth, ct);
        }
        catch (OperationCanceledException)
        {
            Log("[INFO] Crawl stopped by user.");
        }
        catch (Exception ex)
        {
            Log($"[ERR] Crawl failed: {ex.Message}");
        }

        // After crawl, run findings analysis — built-in, always runs
        try
        {
            var svc = new FindingsService(_state);
            await svc.RunAsync(ct);
        }
        catch (Exception ex)
        {
            Log($"[ERR] Findings analysis failed: {ex.Message}");
        }
    }

    // ── Subprocess mode ───────────────────────────────────────────────────────
    private async Task RunSubprocessAsync(string exe, string url, int depth, CancellationToken ct)
    {
        var host  = new Uri(url).Host;
        var outDir = Path.Combine("grabbed", host);
        Directory.CreateDirectory(outDir);

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = $"\"{url}\" \"{outDir}\" {depth}",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            },
            EnableRaisingEvents = true,
        };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _state.ConsoleLog = e.Data;
            ParseCrawlerLine(e.Data, url);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _state.ConsoleLog = $"[ERR] {e.Data}";
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);
    }

    // ── Built-in BFS crawler ──────────────────────────────────────────────────
    private async Task RunBuiltInAsync(string startUrl, int maxDepth, CancellationToken ct)
    {
        Log("[INFO] sitegrab.exe not found — using built-in crawler");
        Log($"[INFO] Target: {startUrl}");

        var baseUri  = new Uri(startUrl);
        var host     = baseUri.Host;
        var outDir   = Path.Combine("grabbed", host);
        Directory.CreateDirectory(outDir);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue   = new Queue<(string Url, int Depth)>();
        queue.Enqueue((startUrl, 0));

        // Add root node to map
        AddMapNode(host, host, NodeType.Root, []);

        var sem = new SemaphoreSlim(8, 8);
        var tasks = new List<Task>();

        while ((queue.Count > 0 || tasks.Any(t => !t.IsCompleted)) && !ct.IsCancellationRequested)
        {
            while (queue.Count > 0 && !ct.IsCancellationRequested)
            {
                var (url, depth) = queue.Dequeue();
                if (!visited.Add(url)) continue;
                if (depth > maxDepth) continue;

                await sem.WaitAsync(ct);
                var capturedUrl   = url;
                var capturedDepth = depth;
                tasks.Add(Task.Run(async () =>
                {
                    try { await CrawlOneAsync(capturedUrl, capturedDepth, baseUri, outDir, queue, visited, maxDepth, ct); }
                    finally { sem.Release(); }
                }, ct));
            }

            // Let tasks advance
            if (tasks.Count > 0)
            {
                await Task.WhenAny(tasks.Where(t => !t.IsCompleted)
                    .DefaultIfEmpty(Task.Delay(50, ct)));
                tasks.RemoveAll(t => t.IsCompleted);
            }
        }

        await Task.WhenAll(tasks);
        Log($"[INFO] Crawl complete. {_state.UrlsCrawled} URLs, {_state.FilesSaved} files saved.");
    }

    private async Task CrawlOneAsync(
        string url, int depth, Uri baseUri, string outDir,
        Queue<(string, int)> queue, HashSet<string> visited,
        int maxDepth, CancellationToken ct)
    {
        HttpResponseMessage? resp = null;
        try
        {
            resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            Log($"[ERR] {url} — {ex.Message}");
            return;
        }

        var status = (int)resp.StatusCode;
        var ct_    = resp.Content.Headers.ContentType?.MediaType ?? "";
        var size   = resp.Content.Headers.ContentLength ?? 0;
        var body   = await resp.Content.ReadAsStringAsync(ct);

        // Record crawled URL
        var cu = new CrawledUrl
        {
            Url         = url,
            Status      = status,
            ContentType = ct_,
            SizeBytes   = size > 0 ? size : body.Length,
            Type        = ct_.Contains("html") ? "Page" : ct_.Contains("css") || ct_.Contains("javascript") ? "Asset" : "File",
        };

        lock (_state.CrawledUrls)
        {
            if (!_state.CrawledUrls.Any(u => u.Url == url))
            {
                App.Current.Dispatcher.InvokeAsync(() => _state.CrawledUrls.Add(cu));
                _state.UrlsCrawled++;
            }
        }

        // Save to disk
        try
        {
            var localPath = UrlToLocalPath(url, outDir);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            await File.WriteAllTextAsync(localPath, body, ct);
            _state.FilesSaved++;
            Log($"[SAVE] {url}");
        }
        catch { }

        // Add to map
        var uriParsed = new Uri(url);
        var pathSeg   = uriParsed.AbsolutePath.TrimStart('/').Split('/');
        var nodeId    = uriParsed.AbsolutePath;
        AddMapNode(nodeId, "/" + (pathSeg.Length > 0 ? pathSeg[^1] : ""), NodeType.Page, [baseUri.Host]);

        // Parse links from HTML
        if (!ct_.Contains("html") || depth >= maxDepth) return;

        var links = ExtractLinks(body, baseUri, url);
        lock (visited)
        {
            foreach (var link in links)
                if (!visited.Contains(link))
                    queue.Enqueue((link, depth + 1));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static readonly Regex _hrefRx = new(
        @"href\s*=\s*[""']([^""'#>]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _srcRx  = new(
        @"src\s*=\s*[""']([^""'#>]+)[""']",  RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static IEnumerable<string> ExtractLinks(string html, Uri baseUri, string pageUrl)
    {
        var pageUri = new Uri(pageUrl);
        var seen    = new HashSet<string>();

        foreach (Match m in _hrefRx.Matches(html).Concat(_srcRx.Matches(html)))
        {
            var raw = m.Groups[1].Value.Trim();
            if (!Uri.TryCreate(pageUri, raw, out var abs)) continue;
            if (abs.Host != baseUri.Host) continue;
            var norm = abs.GetLeftPart(UriPartial.Query);
            if (seen.Add(norm)) yield return norm;
        }
    }

    private static string UrlToLocalPath(string url, string outDir)
    {
        var uri  = new Uri(url);
        var path = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(path) || path.EndsWith('/')) path += "index.html";
        var q    = HttpUtility.ParseQueryString(uri.Query);
        if (q.Count > 0) path += "_" + string.Join("_", q.AllKeys.Select(k => k + "=" + q[k]));

        // Sanitize
        foreach (var ch in Path.GetInvalidFileNameChars())
            path = path.Replace(ch, '_');
        path = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(outDir, path);
    }

    private void AddMapNode(string id, string label, NodeType type, List<string> edges)
    {
        App.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_state.MapNodes.Any(n => n.Id == id)) return;
            _state.MapNodes.Add(new MapNode { Id = id, Label = label, Type = type, Edges = edges });
        });
    }

    private void ParseCrawlerLine(string line, string baseUrl)
    {
        if (line.Contains("[SAVE]"))
        {
            _state.UrlsCrawled++;
            _state.FilesSaved++;
        }
    }

    private void Log(string msg)
        => App.Current.Dispatcher.InvokeAsync(() => _state.ConsoleLog = msg);
}
