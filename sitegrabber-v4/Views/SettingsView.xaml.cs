// language: C#, file: Views/SettingsView.xaml.cs

using System.Windows;
using System.Windows.Controls;
using SiteGrabberV3.Controls;

namespace SiteGrabberV3.Views;

public partial class SettingsView : UserControl
{
    private PageSwitcher? _panes;

    public SettingsView()
    {
        InitializeComponent();
        _panes = new PageSwitcher(new (string, FrameworkElement)[]
        {
            ("Crawler",  PaneCrawler),
            ("Proxy",    PaneProxy),
            ("Perf",     PanePerf),
            ("Patterns", PanePatterns),
        }, "Crawler");
    }

    private void SubTab_Checked(object sender, RoutedEventArgs e)
    {
        if (_panes is null) return;   // Checked can fire during InitializeComponent
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        _panes.Show(tag);
    }

    // Folder picker via OpenFileDialog trick — no WinForms dep needed
    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title           = "Select output folder",
            ValidateNames   = false,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName        = "Select Folder"
        };
        if (dlg.ShowDialog() == true)
            TxtOutput.Text = System.IO.Path.GetDirectoryName(dlg.FileName) ?? "";
    }
}
