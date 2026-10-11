// language: C#, file: LinkRewriter.cs
// post-crawl pass — rewrite absolute URLs in mirrored HTML/CSS to relative local paths

using System.Text;
using System.Text.RegularExpressions;

namespace SiteGrabber;

public static class LinkRewriter
{
    private static readonly string[] UrlAttrs =
    {
        "href", "src", "action", "poster", "data",
        "data-src", "data-original", "data-lazy-src"
    };

    private static readonly Regex ReAttrUrl = new(
        @"(?<attr>" + string.Join("|", UrlAttrs) + @")\s*=\s*(?<q>[""'])(?<url>https?://[^""']+)\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReSrcset = new(
        @"srcset\s*=\s*(?<q>[""'])(?<val>[^""']+)\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReCssUrl = new(
        @"url\((?<q>[""']?)(?<url>https?://[^""'\)\s]+)\k<q>\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (int files, int rewrites) RewriteAll(string outputRoot, string host)
    {
        var baseDir = Path.Combine(outputRoot, host);
        if (!Directory.Exists(baseDir)) return (0, 0);

        int filesTouched = 0;
        int totalRewrites = 0;

        foreach (var htmlPath in Directory.EnumerateFiles(baseDir, "*.html", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(htmlPath, Encoding.UTF8); }
            catch { continue; }

            var before = text;
            int rewrites = 0;

            text = ReAttrUrl.Replace(text, m =>
            {
                var url = m.Groups["url"].Value;
                var rel = MakeRelative(htmlPath, url, baseDir);
                if (rel == null) return m.Value;
                rewrites++;
                return $"{m.Groups["attr"].Value}={m.Groups["q"].Value}{rel}{m.Groups["q"].Value}";
            });

            text = ReSrcset.Replace(text, m =>
            {
                var q   = m.Groups["q"].Value;
                var val = m.Groups["val"].Value;
                var parts = val.Split(',');
                var rewritten = new List<string>();

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length == 0) continue;

                    var sp  = trimmed.IndexOf(' ');
                    var url = sp >= 0 ? trimmed[..sp] : trimmed;
                    var suf = sp >= 0 ? trimmed[sp..]  : "";

                    if (url.StartsWith("http://") || url.StartsWith("https://"))
                    {
                        var rel = MakeRelative(htmlPath, url, baseDir);
                        if (rel != null) { rewrites++; url = rel; }
                    }
                    rewritten.Add(url + suf);
                }
                return $"srcset={q}{string.Join(", ", rewritten)}{q}";
            });

            text = ReCssUrl.Replace(text, m =>
            {
                var url = m.Groups["url"].Value;
                var rel = MakeRelative(htmlPath, url, baseDir);
                if (rel == null) return m.Value;
                rewrites++;
                var q = m.Groups["q"].Value;
                return $"url({q}{rel}{q})";
            });

            if (text != before)
            {
                try { File.WriteAllText(htmlPath, text, Encoding.UTF8); }
                catch { continue; }
                filesTouched++;
                totalRewrites += rewrites;
            }
        }
        return (filesTouched, totalRewrites);
    }

    public static (int files, int rewrites) RewriteCss(string outputRoot, string host)
    {
        var baseDir = Path.Combine(outputRoot, host);
        if (!Directory.Exists(baseDir)) return (0, 0);

        int filesTouched = 0;
        int totalRewrites = 0;

        foreach (var cssPath in Directory.EnumerateFiles(baseDir, "*.css", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(cssPath, Encoding.UTF8); }
            catch { continue; }

            var before = text;
            int rewrites = 0;

            text = ReCssUrl.Replace(text, m =>
            {
                var url = m.Groups["url"].Value;
                var rel = MakeRelative(cssPath, url, baseDir);
                if (rel == null) return m.Value;
                rewrites++;
                var q = m.Groups["q"].Value;
                return $"url({q}{rel}{q})";
            });

            if (text != before)
            {
                try { File.WriteAllText(cssPath, text, Encoding.UTF8); }
                catch { continue; }
                filesTouched++;
                totalRewrites += rewrites;
            }
        }
        return (filesTouched, totalRewrites);
    }

    private static string? MakeRelative(string fromHtml, string absoluteUrl, string baseDir)
    {
        try
        {
            var u = new Uri(absoluteUrl);
            var hostDir = Path.GetFileName(baseDir);
            if (!u.Host.Equals(hostDir, StringComparison.OrdinalIgnoreCase))
                return null;

            var target     = LocalFor(u);
            var targetFull = Path.Combine(baseDir, target);
            var fromDir    = Path.GetDirectoryName(fromHtml) ?? baseDir;

            var rel = Path.GetRelativePath(fromDir, targetFull).Replace('\\', '/');
            return rel;
        }
        catch { return null; }
    }

    private static string LocalFor(Uri u)
    {
        var rel = u.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(rel) || rel.EndsWith('/')) rel += "index.html";
        if (string.IsNullOrEmpty(Path.GetExtension(rel)))   rel += ".html";

        if (!string.IsNullOrEmpty(u.Query))
        {
            var q = Regex.Replace(u.Query.TrimStart('?'), @"[^\w=&-]", "_");
            if (q.Length > 80) q = q[..80];
            var ext  = Path.GetExtension(rel);
            var stem = rel[..^ext.Length];
            rel = $"{stem}_{q}{ext}";
        }
        rel = rel.Replace(":", "_");
        return rel;
    }
}