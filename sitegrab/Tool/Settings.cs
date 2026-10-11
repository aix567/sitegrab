// language: C#, file: Settings.cs

using System.ComponentModel;
using System.Text.Json;

namespace SiteGrabber;

public class Settings
{
    [Category("Crawl"), Description("Recursive link-following depth. 0 = start page only.")]
    public int MaxDepth { get; set; } = 6;

    [Category("Crawl"), Description("Parallel HTTP workers.")]
    public int Threads { get; set; } = 16;

    [Category("Crawl"), Description("Delay between requests per worker (ms). 0 = no throttle.")]
    public int DelayMs { get; set; } = 0;

    [Category("Crawl"), Description("Follow <a href> links within the same domain.")]
    public bool FollowLinks { get; set; } = true;

    [Category("Crawl"), Description("Parse /sitemap.xml, /sitemap_index.xml, product sitemaps.")]
    public bool ParseSitemap { get; set; } = true;

    [Category("Crawl"), Description("Walk ?page=N / ?offset=N up to 500 pages per key.")]
    public bool ExhaustPagination { get; set; } = true;

    [Category("Assets"), Description("Download images (.jpg .png .gif .webp .svg).")]
    public bool SaveImages { get; set; } = true;

    [Category("Assets"), Description("Download stylesheets + CSS-referenced fonts and backgrounds.")]
    public bool SaveCSS { get; set; } = true;

    [Category("Assets"), Description("Download .js / .mjs files.")]
    public bool SaveJS { get; set; } = true;

    [Category("Assets"), Description("Download webfonts (.woff .woff2 .ttf .otf).")]
    public bool SaveFonts { get; set; } = true;

    [Category("Assets"), Description("Download .mp4 .webm .mp3 .wav.")]
    public bool SaveVideos { get; set; } = true;

    [Category("Assets"), Description("Download documents (.pdf .docx .xlsx).")]
    public bool SaveDocs { get; set; } = true;

    [Category("Product"), Description("Extract JSON-LD / OpenGraph product records into _products.ndjson.")]
    public bool ExtractProducts { get; set; } = true;

    [Category("Output"), Description("Default output directory.")]
    public string OutputDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "grabbed");

    [Category("Output"), Description("Max pages per crawl before hard stop.")]
    public int MaxPages { get; set; } = 200000;

    [Category("Output"), Description("Rewrite absolute URLs in mirrored HTML/CSS to relative local paths.")]
    public bool RewriteLinks { get; set; } = true;

    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "sitegrabber.settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var text = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<Settings>(text);
                if (s != null) return s;
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, opts));
        }
        catch { }
    }

    public CrawlOptions ToCrawlOptions(string startUrl)
    {
        return new CrawlOptions(
            StartUrl          : startUrl,
            OutputDir         : OutputDir,
            MaxDepth          : MaxDepth,
            Threads           : Threads,
            DelayMs           : DelayMs,
            FollowLinks       : FollowLinks,
            ParseSitemap      : ParseSitemap,
            SaveImages        : SaveImages,
            SaveCSS           : SaveCSS,
            SaveJS            : SaveJS,
            SaveFonts         : SaveFonts,
            SaveVideos        : SaveVideos,
            SaveDocs          : SaveDocs,
            ExhaustPagination : ExhaustPagination
        );
    }
}