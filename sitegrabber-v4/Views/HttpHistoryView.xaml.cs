// language: C#, file: Views/HttpHistoryView.xaml.cs

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SiteGrabberV3.Controls;
using SiteGrabberV3.Models;

namespace SiteGrabberV3.Views;

public partial class HttpHistoryView : UserControl
{
    private AppState? _state;
    private ObservableCollection<HttpEntry>? _all;
    private bool _filterPlaceholder = true;
    private PageSwitcher? _tabs;

    public AppState? State
    {
        get => _state;
        set
        {
            _state = value;
            if (_state is null) return;
            _all = _state.HttpHistory;
            GridHttp.ItemsSource = _all;
        }
    }

    public HttpHistoryView()
    {
        InitializeComponent();
        _tabs = new PageSwitcher(new (string, FrameworkElement)[]
        {
            ("Request",  PaneRequest),
            ("Response", PaneResponse),
        }, "Request");
    }

    // ── Selection ─────────────────────────────────────────────────────────────
    private void GridHttp_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridHttp.SelectedItem is not HttpEntry entry) return;
        TxtRequest.Text  = BuildRequestText(entry);
        TxtResponse.Text = BuildResponseText(entry);
    }

    private static string BuildRequestText(HttpEntry e) =>
        $"{e.Method} {e.Url} HTTP/1.1\r\n{e.RequestHeaders}\r\n\r\n{e.RequestBody}";

    private static string BuildResponseText(HttpEntry e) =>
        $"HTTP/1.1 {e.Status}\r\n{e.ResponseHeaders}\r\n\r\n{e.ResponseBody}";

    // ── Tab toggle ────────────────────────────────────────────────────────────
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_tabs is null) return;   // Checked can fire during InitializeComponent
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        _tabs.Show(tag);
    }

    // ── Filter ────────────────────────────────────────────────────────────────
    private void TxtFilter_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_filterPlaceholder) { TxtFilter.Text = ""; _filterPlaceholder = false; }
    }

    private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_all is null || _filterPlaceholder) return;
        var q = TxtFilter.Text;
        GridHttp.ItemsSource = string.IsNullOrEmpty(q)
            ? _all
            : new ObservableCollection<HttpEntry>(
                _all.Where(h => h.Url.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }
}
