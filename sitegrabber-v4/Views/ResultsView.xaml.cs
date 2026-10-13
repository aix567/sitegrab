// language: C#, file: Views/ResultsView.xaml.cs

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SiteGrabberV3.Controls;
using SiteGrabberV3.Models;

namespace SiteGrabberV3.Views;

public partial class ResultsView : UserControl
{
    private AppState? _state;
    private ObservableCollection<Finding>? _allFindings;
    private string _filter = "All";

    public AppState? State
    {
        get => _state;
        set
        {
            _state = value;
            if (_state is null) return;
            _allFindings = _state.Findings;
            GridFindings.ItemsSource = _allFindings;
            RefreshSummary();
            _allFindings.CollectionChanged += (_, _) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ApplyFilter(animate: false);
                    RefreshSummary();
                });
        }
    }

    public ResultsView()
    {
        InitializeComponent();
        RefreshSummary();
    }

    // ── Badge + summary ───────────────────────────────────────────────────────
    // Red circle  → at least one Critical/High finding (needs attention).
    // Green circle (blinking) → nothing critical.
    private void RefreshSummary()
    {
        var all   = _allFindings;
        var total = all?.Count ?? 0;
        var crit  = all?.Count(f => f.Severity is Severity.Critical or Severity.High) ?? 0;

        BadgeCount.Content = total;
        BadgeCount.Tag     = crit > 0 ? "crit" : "ok";
        BadgeCount.ToolTip = crit > 0
            ? $"{crit} critical / high finding{(crit == 1 ? "" : "s")} need attention"
            : "No critical findings";

        LblSummary.Text = total == 0
            ? "Run a crawl to collect findings"
            : crit > 0
                ? $"{total} total · {crit} critical / high"
                : $"{total} total · nothing critical";

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var shown = GridFindings.ItemsSource is System.Collections.ICollection c ? c.Count : 0;
        EmptyHint.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = (_allFindings?.Count ?? 0) == 0
            ? "No findings yet"
            : $"No {_filter.ToLowerInvariant()} findings";
    }

    // ── Filter ────────────────────────────────────────────────────────────────
    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (_allFindings is null) return;
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

        _filter = tag;
        ApplyFilter(animate: true);
    }

    private void ApplyFilter(bool animate)
    {
        if (_allFindings is null) return;

        // "All" binds the live collection directly; don't re-assign it on every
        // new finding or the user's row selection would be reset mid-crawl.
        if (_filter == "All")
        {
            if (!ReferenceEquals(GridFindings.ItemsSource, _allFindings))
                GridFindings.ItemsSource = _allFindings;
        }
        else
        {
            GridFindings.ItemsSource = new ObservableCollection<Finding>(
                _allFindings.Where(f => f.SeverityLabel == _filter));
        }

        UpdateEmptyState();
        if (animate) PageSwitcher.Pop(GridFindings);
    }

    // ── Selection → detail ────────────────────────────────────────────────────
    private void GridFindings_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridFindings.SelectedItem is not Finding f) return;
        DetailTarget.Text   = f.Target;
        DetailLocation.Text = f.Location;
        DetailProof.Text    = f.Proof;
        CmbStatus.Text      = f.Status;
    }

    private void BtnUpdateStatus_Click(object sender, RoutedEventArgs e)
    {
        if (GridFindings.SelectedItem is not Finding f) return;
        f.Status = (CmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "New";
    }

    // ── Export CSV ────────────────────────────────────────────────────────────
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_allFindings is null || _allFindings.Count == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "findings",
            DefaultExt = ".csv",
            Filter = "CSV files|*.csv"
        };
        if (dlg.ShowDialog() != true) return;

        var lines = new List<string> { "Severity,Category,Target,Location,Proof,Status" };
        foreach (var f in _allFindings)
            lines.Add($"{f.Severity},{f.CategoryLabel},{Csv(f.Target)},{Csv(f.Location)},{Csv(f.Proof)},{f.Status}");

        File.WriteAllLines(dlg.FileName, lines);
    }

    private static string Csv(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
}
