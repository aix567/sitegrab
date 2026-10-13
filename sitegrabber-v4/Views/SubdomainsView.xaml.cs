// language: C#, file: Views/SubdomainsView.xaml.cs

using System.IO;
using System.Windows;
using System.Windows.Controls;
using SiteGrabberV3.Models;
using SiteGrabberV3.Services;

namespace SiteGrabberV3.Views;

public partial class SubdomainsView : UserControl
{
    private AppState? _state;
    private SubdomainScanner? _scanner;
    private CancellationTokenSource? _cts;

    public AppState? State
    {
        get => _state;
        set
        {
            _state = value;
            if (_state is null) return;
            GridSubs.ItemsSource = _state.Subdomains;
            _state.Subdomains.CollectionChanged += (_, _) =>
                Dispatcher.InvokeAsync(() => BadgeCount.Content = _state.Subdomains.Count);
        }
    }

    public SubdomainsView() => InitializeComponent();

    private bool _scanning;

    private async void BtnEnumerate_Click(object sender, RoutedEventArgs e)
    {
        if (_state is null || _scanning) return;
        var root = TxtRoot.Text.Trim();
        if (string.IsNullOrEmpty(root)) { LblProgress.Text = "Enter a root domain first."; return; }

        _scanning = true;
        BtnEnumerate.IsEnabled = false;
        _state.Subdomains.Clear();
        BadgeCount.Content = 0;
        LblProgress.Text = "Scanning…";
        LblScanStatus.Text = "Scanning";
        BtnStopScan.IsEnabled = true;

        _cts     = new CancellationTokenSource();
        _scanner = new SubdomainScanner(_state);
        try
        {
            await _scanner.RunAsync(root, _cts.Token);
            LblScanStatus.Text = "Done";
            LblProgress.Text   = $"Found {_state.Subdomains.Count} subdomains";
        }
        catch (OperationCanceledException)
        {
            LblScanStatus.Text = "Stopped";
            LblProgress.Text   = $"Cancelled — {_state.Subdomains.Count} found so far";
        }
        catch (Exception ex)
        {
            LblScanStatus.Text = "Error";
            LblProgress.Text   = $"Scan failed: {ex.Message}";
        }
        finally
        {
            BtnStopScan.IsEnabled = false;
            BtnEnumerate.IsEnabled = true;
            _scanning = false;
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_state is null || _state.Subdomains.Count == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "subdomains", DefaultExt = ".csv", Filter = "CSV|*.csv"
        };
        if (dlg.ShowDialog() != true) return;
        var lines = new List<string> { "Subdomain,IP,ARecord,CNAME,Status" };
        foreach (var s in _state.Subdomains)
            lines.Add($"{s.Subdomain},{s.IP},{s.ARecord},{s.CName},{s.Status}");
        File.WriteAllLines(dlg.FileName, lines);
    }
}
