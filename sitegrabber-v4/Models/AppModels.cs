// language: C#, file: Models/AppModels.cs
// All shared data models for SiteGrabber v3

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiteGrabberV3.Models;

// ── INotify base ─────────────────────────────────────────────────────────────
public class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

// ── Crawl result ─────────────────────────────────────────────────────────────
public class CrawledUrl : ObservableBase
{
    private string _url = "";
    private string _type = "Page";     // Page | Asset | Directory
    private int    _status;
    private long   _sizeBytes;
    private string _contentType = "";
    private string _source = "";

    public string Url         { get => _url;         set => Set(ref _url, value); }
    public string Type        { get => _type;        set => Set(ref _type, value); }
    public int    Status      { get => _status;      set => Set(ref _status, value); }
    public long   SizeBytes   { get => _sizeBytes;   set => Set(ref _sizeBytes, value); }
    public string ContentType { get => _contentType; set => Set(ref _contentType, value); }
    public string Source      { get => _source;      set => Set(ref _source, value); }

    public string SizeKB => $"{SizeBytes / 1024.0:F1} KB";
    public string Path   => new Uri(Url).AbsolutePath;
}

// ── Finding ──────────────────────────────────────────────────────────────────
public enum Severity { Critical, High, Medium, Low, Info }
public enum FindingCategory { Secret, Config, PathDisclosure, Endpoint, Information }

public class Finding : ObservableBase
{
    private Severity         _severity = Severity.Info;
    private FindingCategory  _category = FindingCategory.Information;
    private string _target = "", _proof = "", _location = "", _status = "New";

    public Severity        Severity { get => _severity; set => Set(ref _severity, value); }
    public FindingCategory Category { get => _category; set => Set(ref _category, value); }
    public string Target   { get => _target;   set => Set(ref _target, value); }
    public string Proof    { get => _proof;    set => Set(ref _proof, value); }
    public string Location { get => _location; set => Set(ref _location, value); }
    public string Status   { get => _status;   set => Set(ref _status, value); }

    public string SeverityLabel => Severity.ToString();
    public string CategoryLabel => Category switch
    {
        FindingCategory.Secret        => "🔑 Secret",
        FindingCategory.Config        => "⚠ Config",
        FindingCategory.PathDisclosure=> "📁 Path Disclosure",
        FindingCategory.Endpoint      => "🔗 Endpoint",
        _                             => "ℹ Information",
    };
}

// ── HTTP entry ────────────────────────────────────────────────────────────────
public class HttpEntry : ObservableBase
{
    private DateTime _time = DateTime.Now;
    private string _method = "GET", _url = "", _source = "", _risk = "";
    private int _status;
    private double _sizeKb;
    private string _requestHeaders = "", _requestBody = "";
    private string _responseHeaders = "", _responseBody = "";

    public DateTime Time            { get => _time;            set => Set(ref _time, value); }
    public string   Method          { get => _method;          set => Set(ref _method, value); }
    public string   Url             { get => _url;             set => Set(ref _url, value); }
    public int      Status          { get => _status;          set => Set(ref _status, value); }
    public double   SizeKb          { get => _sizeKb;          set => Set(ref _sizeKb, value); }
    public string   Source          { get => _source;          set => Set(ref _source, value); }
    public string   Risk            { get => _risk;            set => Set(ref _risk, value); }
    public string   RequestHeaders  { get => _requestHeaders;  set => Set(ref _requestHeaders, value); }
    public string   RequestBody     { get => _requestBody;     set => Set(ref _requestBody, value); }
    public string   ResponseHeaders { get => _responseHeaders; set => Set(ref _responseHeaders, value); }
    public string   ResponseBody    { get => _responseBody;    set => Set(ref _responseBody, value); }

    public string TimeStr  => _time.ToString("HH:mm:ss");
    public string SizeStr  => $"{_sizeKb:F1}";
    public string ShortUrl => _url.Length > 60 ? _url[..57] + "…" : _url;
}

// ── Subdomain ─────────────────────────────────────────────────────────────────
public class SubdomainEntry : ObservableBase
{
    private string _subdomain = "", _root = "", _ip = "", _aRecord = "";
    private string _cname = "", _lastSeen = "", _status = "Active";

    public string Subdomain { get => _subdomain; set => Set(ref _subdomain, value); }
    public string Root      { get => _root;      set => Set(ref _root, value); }
    public string IP        { get => _ip;        set => Set(ref _ip, value); }
    public string ARecord   { get => _aRecord;   set => Set(ref _aRecord, value); }
    public string CName     { get => _cname;     set => Set(ref _cname, value); }
    public string LastSeen  { get => _lastSeen;  set => Set(ref _lastSeen, value); }
    public string Status    { get => _status;    set => Set(ref _status, value); }
}

// ── Crawl map node ────────────────────────────────────────────────────────────
public enum NodeType { Root, Page, Directory, File, Subdomain }

public class MapNode
{
    public string   Id       { get; set; } = "";
    public string   Label    { get; set; } = "";
    public NodeType Type     { get; set; } = NodeType.Page;
    public double   X        { get; set; }
    public double   Y        { get; set; }
    public double   Vx       { get; set; }  // velocity for force sim
    public double   Vy       { get; set; }
    public List<string> Edges { get; set; } = [];
}

// ── App state ─────────────────────────────────────────────────────────────────
public class AppState : ObservableBase
{
    private int    _urlsCrawled, _filesSaved, _findingsCount, _subdomainCount;
    private bool   _isRunning;
    private string _currentTarget = "";
    private string _consoleLog    = "";

    public int    UrlsCrawled    { get => _urlsCrawled;    set => Set(ref _urlsCrawled, value); }
    public int    FilesSaved     { get => _filesSaved;     set => Set(ref _filesSaved, value); }
    public int    FindingsCount  { get => _findingsCount;  set => Set(ref _findingsCount, value); }
    public int    SubdomainCount { get => _subdomainCount; set => Set(ref _subdomainCount, value); }
    public bool   IsRunning      { get => _isRunning;      set => Set(ref _isRunning, value); }
    public string CurrentTarget  { get => _currentTarget;  set => Set(ref _currentTarget, value); }
    public string ConsoleLog     { get => _consoleLog;     set => Set(ref _consoleLog, value); }

    public ObservableCollection<CrawledUrl>    CrawledUrls   { get; } = [];
    public ObservableCollection<Finding>       Findings      { get; } = [];
    public ObservableCollection<HttpEntry>     HttpHistory   { get; } = [];
    public ObservableCollection<SubdomainEntry> Subdomains   { get; } = [];
    public ObservableCollection<MapNode>       MapNodes      { get; } = [];
}
