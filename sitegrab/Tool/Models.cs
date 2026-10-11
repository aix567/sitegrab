// language: C#, file: Models.cs

namespace SiteGrabber;

public record CrawlOptions(
    string  StartUrl,
    string  OutputDir,
    int     MaxDepth          = 4,
    int     Threads           = 16,
    int     DelayMs           = 0,
    bool    FollowLinks       = true,
    bool    ParseSitemap      = true,
    bool    SaveImages        = true,
    bool    SaveCSS           = true,
    bool    SaveJS            = true,
    bool    SaveFonts         = true,
    bool    SaveVideos        = true,
    bool    SaveDocs          = true,
    bool    ExhaustPagination = true
);

public class CrawlStats
{
    private int _pages, _assets, _errors;

    public int Pages  => _pages;
    public int Assets => _assets;
    public int Errors => _errors;

    public void AddPage()  => Interlocked.Increment(ref _pages);
    public void AddAsset() => Interlocked.Increment(ref _assets);
    public void AddError() => Interlocked.Increment(ref _errors);
}

public enum LogLevel { Info, Success, Error, Warning }

public record LogEntry(LogLevel Level, string Message, DateTime Time)
{
    public static LogEntry Ok(string msg)   => new(LogLevel.Success, msg,  DateTime.Now);
    public static LogEntry Bad(string msg)  => new(LogLevel.Error,   msg,  DateTime.Now);
    public static LogEntry Note(string msg) => new(LogLevel.Info,    msg,  DateTime.Now);
    public static LogEntry Warn(string msg) => new(LogLevel.Warning, msg,  DateTime.Now);
}