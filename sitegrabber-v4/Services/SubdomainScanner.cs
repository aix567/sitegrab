// language: C#, file: Services/SubdomainScanner.cs
// Runs subdomain_enum.exe (Rust) as subprocess; falls back to C# DNS bruteforce

using System.Diagnostics;
using System.IO;
using System.Net;
using SiteGrabberV3.Models;

namespace SiteGrabberV3.Services;

public class SubdomainScanner
{
    private readonly AppState _state;
    private static readonly string[] _commonWords = [
        "www","mail","api","dev","staging","test","blog","shop","app","cdn",
        "admin","auth","login","portal","dashboard","static","assets","media",
        "upload","download","docs","support","help","forum","ftp","smtp",
        "vpn","remote","git","gitlab","github","jira","jenkins","ci","build",
        "beta","alpha","preview","sandbox","demo","old","new","secure","ssl",
        "ns1","ns2","mx","pop","imap","autodiscover","webmail","cpanel",
        "whm","direct","mobile","m","api2","v1","v2","v3","internal","intranet",
        "monitor","status","health","metrics","grafana","prometheus","elastic",
        "kibana","redis","mysql","db","database","backup","archive","logs",
    ];

    public SubdomainScanner(AppState state) => _state = state;

    public async Task RunAsync(string root, CancellationToken ct)
    {
        // Try Rust binary first
        var rustExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "Rust", "subdomain_enum", "target", "release", "subdomain_enum.exe");

        try
        {
            if (File.Exists(rustExe))
                await RunRustAsync(rustExe, root, ct);
            else
                await RunBuiltInAsync(root, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log($"[ERR] Subdomain scan failed: {ex.Message}");
            throw;
        }
    }

    // ── Rust subprocess ───────────────────────────────────────────────────────
    private async Task RunRustAsync(string exe, string root, CancellationToken ct)
    {
        Log("[INFO] Using Rust subdomain enumerator…");
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = root,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };

        proc.Start();
        while (!proc.StandardOutput.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await proc.StandardOutput.ReadLineAsync(ct);
            if (line is null) break;
            ParseSubdomainLine(line, root);
        }
        await proc.WaitForExitAsync(ct);
    }

    // ── Built-in C# DNS bruteforce ────────────────────────────────────────────
    private async Task RunBuiltInAsync(string root, CancellationToken ct)
    {
        Log("[INFO] Rust binary not found — using built-in DNS bruteforce");

        // crt.sh passive first
        await QueryCrtSh(root, ct);

        // Then wordlist bruteforce
        var sem   = new SemaphoreSlim(50, 50);
        var tasks = new List<Task>();

        foreach (var word in _commonWords)
        {
            if (ct.IsCancellationRequested) break;
            await sem.WaitAsync(ct);
            var sub = $"{word}.{root}";
            tasks.Add(Task.Run(async () =>
            {
                try { await ResolveAsync(sub, root, ct); }
                finally { sem.Release(); }
            }, ct));
        }
        await Task.WhenAll(tasks);
        Log($"[INFO] Subdomain scan complete. {_state.Subdomains.Count} found.");
    }

    private async Task QueryCrtSh(string root, CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync(
                $"https://crt.sh/?q=%.{root}&output=json", ct);

            using var doc  = System.Text.Json.JsonDocument.Parse(json);
            var subs = doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("name_value", out var v) ? v.GetString() ?? "" : "")
                .Where(s => s.EndsWith("." + root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Log($"[INFO] crt.sh returned {subs.Count} candidate subdomains");
            foreach (var sub in subs)
                await ResolveAsync(sub.TrimStart('*', '.'), root, ct);
        }
        catch (Exception ex)
        {
            Log($"[WARN] crt.sh query failed: {ex.Message}");
        }
    }

    private async Task ResolveAsync(string sub, string root, CancellationToken ct)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(sub, ct);
            var ip    = entry.AddressList.FirstOrDefault()?.ToString() ?? "";
            var e = new SubdomainEntry
            {
                Subdomain = sub,
                Root      = root,
                IP        = ip,
                ARecord   = ip,
                LastSeen  = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Status    = "Active",
            };
            App.Current.Dispatcher.InvokeAsync(() =>
            {
                _state.Subdomains.Add(e);
                _state.SubdomainCount = _state.Subdomains.Count;
            });
            Log($"[OK] {sub} → {ip}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // NXDOMAIN or timeout — skip silently
        }
    }

    private void ParseSubdomainLine(string line, string root)
    {
        // Rust output: "sub.example.com 1.2.3.4"
        var parts = line.Split(' ');
        if (parts.Length < 1) return;
        var sub = parts[0].Trim();
        var ip  = parts.Length > 1 ? parts[1].Trim() : "";

        var e = new SubdomainEntry
        {
            Subdomain = sub,
            Root      = root,
            IP        = ip,
            ARecord   = ip,
            LastSeen  = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Status    = string.IsNullOrEmpty(ip) ? "Error" : "Active",
        };
        App.Current.Dispatcher.InvokeAsync(() =>
        {
            _state.Subdomains.Add(e);
            _state.SubdomainCount = _state.Subdomains.Count;
        });
    }

    private void Log(string msg)
        => App.Current.Dispatcher.InvokeAsync(() => _state.ConsoleLog = msg);
}
