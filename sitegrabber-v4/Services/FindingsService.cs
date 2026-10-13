// language: C#, file: Services/FindingsService.cs
// Built-in findings analyzer — scans the crawled output directory for secrets,
// configs, endpoints and path disclosures. Runs in-process (no python subprocess),
// so it always works regardless of what's installed on the target machine.

using System.IO;
using System.Text.RegularExpressions;
using SiteGrabberV3.Models;

namespace SiteGrabberV3.Services;

public class FindingsService
{
    private readonly AppState _state;
    public FindingsService(AppState state) => _state = state;

    private record Rule(Severity Sev, FindingCategory Cat, Regex Rx, string Label);

    private static readonly Rule[] _rules =
    [
        new(Severity.Critical, FindingCategory.Secret,
            Re(@"sk_live_[0-9A-Za-z]{24,}"), "Stripe Live Secret Key"),
        new(Severity.High, FindingCategory.Secret,
            Re(@"sk_test_[0-9A-Za-z]{24,}"), "Stripe Test Secret Key"),
        new(Severity.Critical, FindingCategory.Secret,
            Re(@"AKIA[0-9A-Z]{16}"), "AWS Access Key ID"),
        new(Severity.Critical, FindingCategory.Secret,
            Re(@"(?i)aws_secret_access_key\s*[=:]\s*[""']?([A-Za-z0-9+/]{40})"), "AWS Secret Access Key"),
        new(Severity.High, FindingCategory.Secret,
            Re(@"AIza[0-9A-Za-z\-_]{35}"), "Google API Key"),
        new(Severity.Critical, FindingCategory.Secret,
            Re(@"ghp_[A-Za-z0-9]{36}"), "GitHub Personal Access Token"),
        new(Severity.Critical, FindingCategory.Secret,
            Re(@"github_pat_[A-Za-z0-9_]{82}"), "GitHub Fine-grained PAT"),
        new(Severity.Critical, FindingCategory.Secret,
            Re(@"-----BEGIN (?:RSA |EC |DSA )?PRIVATE KEY-----"), "Private Key"),
        new(Severity.High, FindingCategory.Config,
            Re(@"(?i)(postgres|mysql|mongodb|redis|mssql)://[^/\s""']{8,}"), "Database Connection String"),
        new(Severity.High, FindingCategory.Config,
            Re(@"(?i)(?:password|passwd|pwd)\s*[=:]\s*[""']?([^\s""'&]{6,})"), "Hardcoded Password"),
        new(Severity.High, FindingCategory.Config,
            Re(@"(?i)DATABASE_PASSWORD\s*[=:]\s*[""']?([^\s""'&]{6,})"), "Database Password"),
        new(Severity.Medium, FindingCategory.Secret,
            Re(@"(?i)api[_\-]?key\s*[=:]\s*[""']?([A-Za-z0-9\-_]{16,})"), "Generic API Key"),
        new(Severity.Medium, FindingCategory.Secret,
            Re(@"eyJ[A-Za-z0-9\-_]+\.eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+"), "JWT Token"),
        new(Severity.Medium, FindingCategory.Secret,
            Re(@"(?i)bearer\s+([A-Za-z0-9\-_.~+/]{20,})"), "Bearer Token"),
        new(Severity.High, FindingCategory.Config,
            Re(@"(?i)(\.env|config\.php|wp-config\.php|\.htpasswd|\.git/config)"), "Sensitive Config File"),
        new(Severity.Medium, FindingCategory.PathDisclosure,
            Re(@"(?:/var|/etc|/home|/usr|/opt|/srv|C:\\[A-Za-z]|D:\\[A-Za-z])[/\\][^\s""'<>]{4,}"), "Server Path Disclosure"),
        new(Severity.Low, FindingCategory.Endpoint,
            Re(@"(?i)(?:href|src|action|url)\s*=\s*[""'](/(?:admin|api|internal|debug|test|console|phpmyadmin|swagger|graphql)[^""']*)"), "Sensitive Endpoint"),
        new(Severity.Medium, FindingCategory.Config,
            Re(@"(?i)debug\s*[=:]\s*(?:true|1|on|yes)"), "Debug Mode Enabled"),
        new(Severity.Low, FindingCategory.Config,
            Re(@"(?i)access-control-allow-origin:\s*\*"), "CORS Wildcard"),
        new(Severity.Info, FindingCategory.Information,
            Re(@"\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b"), "Internal IP Address"),
        new(Severity.Info, FindingCategory.Information,
            Re(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}"), "Email Address"),
    ];

    private static readonly HashSet<string> _scannableExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".js", ".json", ".xml", ".txt",
        ".css", ".php", ".py", ".rb", ".env", ".cfg",
        ".conf", ".ini", ".yaml", ".yml", ".toml", ".sql",
    };

    private static Regex Re(string pattern) => new(pattern, RegexOptions.Multiline | RegexOptions.Compiled);

    public Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_state.CurrentTarget))
            {
                Log("[WARN] No target set — skipping findings analysis");
                return Task.CompletedTask;
            }

            var host   = new Uri(_state.CurrentTarget).Host;
            var outDir = Path.Combine("grabbed", host);
            if (!Directory.Exists(outDir))
            {
                Log("[WARN] Output folder not found — crawl first");
                return Task.CompletedTask;
            }

            Log("[INFO] Running findings analysis…");
            var seen = new HashSet<string>();
            int scanned = 0;

            foreach (var file in Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested) break;
                if (!_scannableExt.Contains(Path.GetExtension(file))) continue;

                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }

                scanned++;
                var rel = Path.GetRelativePath(outDir, file).Replace('\\', '/');

                foreach (var rule in _rules)
                {
                    foreach (Match m in rule.Rx.Matches(text))
                    {
                        var proof = m.Value.Length > 120 ? m.Value[..120] : m.Value;
                        var key   = $"{rule.Cat}:{proof[..Math.Min(60, proof.Length)]}";
                        if (!seen.Add(key)) continue;

                        var lineNo = text.AsSpan(0, m.Index).Count('\n') + 1;
                        var finding = new Finding
                        {
                            Severity = rule.Sev,
                            Category = rule.Cat,
                            Target   = rel,
                            Proof    = proof.Length > 200 ? proof[..200] : proof,
                            Location = $"{rel} (Line {lineNo}) — {rule.Label}",
                            Status   = "New",
                        };
                        App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            _state.Findings.Add(finding);
                            _state.FindingsCount = _state.Findings.Count;
                        });
                        Log($"[FIND] [{rule.Sev}] {rule.Label} in {rel}");
                    }
                }
            }

            Log($"[INFO] Analysis done. Scanned {scanned} files, {_state.Findings.Count} findings.");
        }
        catch (Exception ex)
        {
            Log($"[ERR] Findings analysis failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private void Log(string msg)
        => App.Current.Dispatcher.InvokeAsync(() => _state.ConsoleLog = msg);
}
