// language: C#, file: ProductQuery.cs

using System.Text.Json;
using System.Text.RegularExpressions;

namespace SiteGrabber;

public class ProductQuery
{
    public record QueryRow(string Url, string Kind, string Snippet);

    private static readonly Regex ReClause = new(
        @"(?<field>[\w\.]+)\s*(?<op>==|contains|>|<|>=|<=)\s*""(?<val>[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<QueryRow> Run(string ndjsonPath, string query, int limit = 500)
    {
        var rows = new List<QueryRow>();
        if (!File.Exists(ndjsonPath)) return rows;

        query = query.Trim();
        int limitIdx = query.IndexOf("LIMIT", StringComparison.OrdinalIgnoreCase);
        if (limitIdx >= 0)
        {
            var limStr = query[(limitIdx + 5)..].Trim();
            query = query[..limitIdx].Trim();
            if (int.TryParse(limStr, out var l)) limit = l;
        }

        var orParts = Regex.Split(query, @"\s+OR\s+", RegexOptions.IgnoreCase)
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .ToList();
        if (orParts.Count == 0) orParts.Add(query);

        var clausesByOr = orParts.Select(part =>
            Regex.Split(part, @"\s+AND\s+", RegexOptions.IgnoreCase)
                 .Select(s => s.Trim())
                 .Where(s => !string.IsNullOrWhiteSpace(s))
                 .ToList()).ToList();

        foreach (var line in File.ReadLines(ndjsonPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(line); } catch { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                var url  = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                var blob = root.GetRawText();

                bool matched = false;
                foreach (var andGroup in clausesByOr)
                {
                    if (andGroup.Count == 0) { matched = true; break; }
                    if (andGroup.All(c => Match(blob, c))) { matched = true; break; }
                }
                if (!matched) continue;

                var snippet = blob.Length > 140 ? blob[..140] + "..." : blob;
                rows.Add(new QueryRow(url, kind, snippet));
                if (rows.Count >= limit) break;
            }
        }
        return rows;
    }

    private static bool Match(string blob, string clause)
    {
        var m = ReClause.Match(clause);
        if (!m.Success)
            return blob.Contains(clause, StringComparison.OrdinalIgnoreCase);

        var field = m.Groups["field"].Value.ToLowerInvariant();
        var op    = m.Groups["op"].Value.ToLowerInvariant();
        var val   = m.Groups["val"].Value;

        bool hasField = blob.Contains($"\"{field}\"", StringComparison.OrdinalIgnoreCase);
        if (!hasField) return false;

        switch (op)
        {
            case "==":
                return Regex.IsMatch(blob, $"\"{Regex.Escape(field)}\"\\s*:\\s*\"?{Regex.Escape(val)}\"?",
                                     RegexOptions.IgnoreCase);
            case "contains":
                return blob.Contains(val, StringComparison.OrdinalIgnoreCase);
            case ">":
            case "<":
            case ">=":
            case "<=":
                var numMatch = Regex.Match(blob,
                    $"\"{Regex.Escape(field)}\"\\s*:\\s*\"?([-\\d\\.]+)\"?",
                    RegexOptions.IgnoreCase);
                if (!numMatch.Success) return false;
                if (!double.TryParse(numMatch.Groups[1].Value, out var have)) return false;
                if (!double.TryParse(val, out var want)) return false;
                return op switch
                {
                    ">"  => have >  want,
                    "<"  => have <  want,
                    ">=" => have >= want,
                    "<=" => have <= want,
                    _    => false,
                };
            default:
                return false;
        }
    }
}