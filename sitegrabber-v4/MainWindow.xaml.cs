// language: C#, file: MainWindow.xaml.cs
// Shell — nav switching, right sidebar file tree, quick actions

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SiteGrabberV3.Controls;
using SiteGrabberV3.Models;

namespace SiteGrabberV3;

public partial class MainWindow : Window
{
    public readonly AppState State = new();
    private readonly PageSwitcher _switcher;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = State;

        _switcher = new PageSwitcher(new (string, FrameworkElement)[]
        {
            ("Crawler",    ViewCrawler),
            ("Results",    ViewResults),
            ("Http",       ViewHttp),
            ("Repeater",   ViewRepeater),
            ("Subdomains", ViewSubdomains),
            ("Settings",   ViewSettings),
        }, "Crawler");

        // Park the sliding nav bar on the first item once layout exists.
        Loaded += (_, _) => MoveNavIndicator(NavCrawler, animate: false);

        // Wire state to views that need it
        ViewCrawler.State    = State;
        ViewResults.State    = State;
        ViewHttp.State       = State;
        ViewSubdomains.State = State;

        // Status strip updates
        State.PropertyChanged += (_, e) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (e.PropertyName == nameof(AppState.IsRunning))
                {
                    StatusLabel.Text = State.IsRunning ? "Running…" : "Ready";
                    StatusDot.Fill   = State.IsRunning
                        ? (Brush)FindResource("BrMedium")
                        : (Brush)FindResource("BrLow");
                }
            });
        };

        // Build file tree when crawl completes
        State.CrawledUrls.CollectionChanged += (_, _) =>
            Dispatcher.InvokeAsync(RebuildFileTree);
    }

    // ── Nav ───────────────────────────────────────────────────────────────────
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Checked can fire while InitializeComponent is still running.
        if (_switcher is null) return;
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

        _switcher.Show(tag);
        MoveNavIndicator(rb, animate: true);
    }

    /// <summary>Glides the shared accent bar to the vertical centre of <paramref name="target"/>.</summary>
    private void MoveNavIndicator(RadioButton target, bool animate)
    {
        if (!target.IsLoaded || !NavHost.IsLoaded) return;

        var centre = target.TranslatePoint(new Point(0, target.ActualHeight / 2), NavHost).Y;
        var y      = centre - NavIndicator.Height / 2;

        if (!animate)
        {
            NavIndicatorShift.BeginAnimation(TranslateTransform.YProperty, null);
            NavIndicatorShift.Y = y;
            return;
        }

        NavIndicatorShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation
            {
                To             = y,
                Duration       = TimeSpan.FromMilliseconds(320),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    // ── File tree ─────────────────────────────────────────────────────────────
    private void RebuildFileTree()
    {
        FileTree.Items.Clear();
        if (string.IsNullOrEmpty(State.CurrentTarget)) return;

        var host = new Uri(State.CurrentTarget).Host;
        var root = new TreeViewItem
        {
            Header      = host,
            Foreground  = (Brush)FindResource("BrAccent"),
            IsExpanded  = true,
        };
        FileTree.Items.Add(root);

        // Group paths by top-level segment
        var dirs = new Dictionary<string, TreeViewItem>();
        foreach (var cu in State.CrawledUrls)
        {
            try
            {
                var path  = new Uri(cu.Url).AbsolutePath.TrimStart('/');
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                var dir = parts[0];
                if (!dirs.TryGetValue(dir, out var dirNode))
                {
                    dirNode = new TreeViewItem
                    {
                        Header     = "📁 " + dir + "/",
                        Foreground = (Brush)FindResource("BrText"),
                    };
                    root.Items.Add(dirNode);
                    dirs[dir] = dirNode;
                }

                if (parts.Length > 1)
                {
                    var fileName = parts[^1];
                    dirNode.Items.Add(new TreeViewItem
                    {
                        Header     = "📄 " + fileName,
                        Foreground = (Brush)FindResource("BrMuted"),
                    });
                }
            }
            catch { }
        }
    }

    private void RefreshTree_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => RebuildFileTree();

    // ── Quick actions ─────────────────────────────────────────────────────────
    private void BtnSaveReport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"sitegrabber_{DateTime.Now:yyyyMMdd_HHmm}",
            DefaultExt = ".json",
            Filter     = "JSON Report|*.json|Text Report|*.txt"
        };
        if (dlg.ShowDialog() != true) return;

        var report = new
        {
            Target     = State.CurrentTarget,
            Generated  = DateTime.Now,
            UrlsCrawled= State.UrlsCrawled,
            FilesSaved = State.FilesSaved,
            Findings   = State.Findings.Select(f => new
            {
                f.Severity, f.CategoryLabel, f.Target, f.Location, f.Proof, f.Status
            }),
            Subdomains = State.Subdomains.Select(s => new
            {
                s.Subdomain, s.IP, s.ARecord, s.Status
            }),
        };
        File.WriteAllText(dlg.FileName,
            System.Text.Json.JsonSerializer.Serialize(report,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "crawled_urls", DefaultExt = ".csv", Filter = "CSV|*.csv"
        };
        if (dlg.ShowDialog() != true) return;
        var lines = new List<string> { "URL,Type,Status,Size,ContentType" };
        foreach (var u in State.CrawledUrls)
            lines.Add($"{u.Url},{u.Type},{u.Status},{u.SizeBytes},{u.ContentType}");
        File.WriteAllLines(dlg.FileName, lines);
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var target = State.CurrentTarget;
        if (string.IsNullOrEmpty(target)) return;
        var host   = new Uri(target).Host;
        var folder = Path.Combine("grabbed", host);
        if (Directory.Exists(folder))
            Process.Start("explorer.exe", folder);
        else
            MessageBox.Show("Output folder not found.\nRun a crawl first.", "SiteGrabber");
    }
}
