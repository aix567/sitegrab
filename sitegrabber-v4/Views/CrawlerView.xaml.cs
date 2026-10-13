// language: C#, file: Views/CrawlerView.xaml.cs
// Crawler view code-behind — wires UI to CrawlerEngine + AppState bindings

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SiteGrabberV3.Models;
using SiteGrabberV3.Services;

namespace SiteGrabberV3.Views;

public partial class CrawlerView : UserControl
{
    private AppState? _state;
    private CrawlerEngine? _engine;
    private CancellationTokenSource? _cts;

    public AppState? State
    {
        get => _state;
        set
        {
            _state = value;
            if (_state is null) return;
            GridFiles.ItemsSource = _state.CrawledUrls;
            CrawlMap.Nodes        = _state.MapNodes;

            _state.PropertyChanged += (_, e) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(AppState.UrlsCrawled):
                            LblUrls.Text = _state.UrlsCrawled.ToString();
                            break;
                        case nameof(AppState.FilesSaved):
                            LblFiles.Text = _state.FilesSaved.ToString();
                            break;
                        case nameof(AppState.FindingsCount):
                            LblFindings.Text = _state.FindingsCount.ToString();
                            LblFindings.Foreground = FindingsColor();
                            break;
                        case nameof(AppState.SubdomainCount):
                            LblSubdomains.Text = _state.SubdomainCount.ToString();
                            break;
                        case nameof(AppState.ConsoleLog):
                            AppendLog(_state.ConsoleLog);
                            break;
                        case nameof(AppState.IsRunning):
                            BtnAnalyze.IsEnabled = !_state.IsRunning;
                            BtnStop.IsEnabled     = _state.IsRunning;
                            break;
                    }
                });
            };

            _state.MapNodes.CollectionChanged += (_, _) =>
                Dispatcher.InvokeAsync(() =>
                    LblMapNodes.Text = $"{_state.MapNodes.Count} nodes");
        }
    }

    public CrawlerView()
    {
        InitializeComponent();
    }

    // ── Analyze ───────────────────────────────────────────────────────────────
    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_state is null) return;

        var url = TxtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) { AppendLog("[ERR] Enter a target URL."); return; }
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        // Reset state
        _state.CrawledUrls.Clear();
        _state.Findings.Clear();
        _state.MapNodes.Clear();
        _state.UrlsCrawled = _state.FilesSaved = _state.FindingsCount = _state.SubdomainCount = 0;
        _state.CurrentTarget = url;
        _state.ConsoleLog    = "";
        TxtConsole.Inlines.Clear();

        int depth = CmbDepth.SelectedIndex switch { 0 => 2, 1 => 3, 2 => 5, _ => 99 };

        _cts    = new CancellationTokenSource();
        _engine = new CrawlerEngine(_state);

        _state.IsRunning = true;
        try
        {
            await _engine.RunAsync(url, depth, _cts.Token);
        }
        catch (Exception ex)
        {
            AppendLog($"[ERR] Crawl failed: {ex.Message}");
        }
        finally
        {
            _state.IsRunning = false;
        }
    }

    // Findings stat color reflects the worst severity actually found, instead of
    // sitting on a static alarm-red even at 0 — that flat red-at-rest was the
    // "weird" look.
    private Brush FindingsColor()
    {
        if (_state is null || _state.Findings.Count == 0)
            return (Brush)FindResource("BrMuted");

        var worst = _state.Findings.Min(f => f.Severity); // enum order: Critical=0 … Info=4
        return worst switch
        {
            Severity.Critical => (Brush)FindResource("BrCritical"),
            Severity.High     => (Brush)FindResource("BrHigh"),
            Severity.Medium   => (Brush)FindResource("BrMedium"),
            Severity.Low      => (Brush)FindResource("BrLow"),
            _                 => (Brush)FindResource("BrInfo"),
        };
    }

    // ── Stop ──────────────────────────────────────────────────────────────────
    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    // ── Console log ───────────────────────────────────────────────────────────
    private void AppendLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        var run = new Run(line + "\n");
        if (line.StartsWith("[ERR"))  run.Foreground = Brushes.IndianRed;
        else if (line.StartsWith("[OK") || line.StartsWith("[SAVE")) run.Foreground = Brushes.MediumSeaGreen;
        else if (line.StartsWith("[FIND"))  run.Foreground = Brushes.Orange;
        else run.Foreground = new SolidColorBrush(Color.FromRgb(163, 185, 210));

        TxtConsole.Inlines.Add(run);
        ConsoleScroll.ScrollToBottom();
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        TxtConsole.Inlines.Clear();
    }
}
