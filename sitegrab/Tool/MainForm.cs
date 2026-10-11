// language: C#, file: MainForm.cs

using System.Diagnostics;

namespace SiteGrabber;

public partial class MainForm : Form
{
    private readonly Crawler  _crawler  = new();
    private readonly CmdShell _shell    = new();
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _timer = new();

    private AboutIntro? _aboutIntro;
    private CreditRoll? _credits;
    private InfoPanel?  _infoPanel;
    private TablesView? _tablesView;
    private FilesView?  _filesView;
    private Settings    _settings = Settings.Load();

    public MainForm()
    {
        InitializeComponent();
        WireEvents();
        WireIntroAndCredits();
        WireShell();
        WireQuery();
        WireSettings();
        WireInfoTablesFiles();

        Log("SiteGrabber v3.0 ready.");
        Log("Select a crawl mode, set a target, press Analyze.");
    }

    // ── crawl ────────────────────────────────────────────────────────────────
    private void WireEvents()
    {
        btnAnalyze.Click += OnAnalyze;
        btnPause.Click   += OnPause;
        btnLoad.Click    += OnLoad;
        btnSave.Click    += OnSave;

        cmbDatabase.SelectedIndexChanged += (_, _) =>
            lblMode.Text = $"Mode: {cmbDatabase.SelectedItem}";

        _crawler.OnLog   += e => BeginInvoke(() => Log(e.Message));
        _crawler.OnStats += (pages, assets, errors) =>
            BeginInvoke(() => lblStatus.Text =
                $"Status: Running  |  pages {pages}  assets {assets}  errors {errors}");
    }

    private async void OnAnalyze(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtTarget.Text)) { Log("no target set"); return; }

        string mode = cmbDatabase.SelectedItem?.ToString() ?? "Auto Detect";
        var opts = _settings.ToCrawlOptions(txtTarget.Text.Trim());

        _cts = new CancellationTokenSource();
        btnAnalyze.Enabled = false;
        btnPause.Enabled   = true;
        _timer.Restart();

        _infoPanel?.Start(opts.StartUrl, opts.OutputDir, opts.MaxDepth, opts.Threads, mode);
        _tablesView?.Clear();
        _tablesView?.Start();

        lblStatus.Text = "Status: Starting crawler...";
        Log($"--- crawl start: {opts.StartUrl}");
        Log($"    depth={opts.MaxDepth}  threads={opts.Threads}  out={opts.OutputDir}");

        try
        {
            await _crawler.RunAsync(opts, _cts.Token);
            Log($"--- crawl complete in {_timer.Elapsed.TotalSeconds:F1}s");
            lblStatus.Text = "Status: Idle";
        }
        catch (OperationCanceledException)
        {
            Log("--- crawl cancelled");
            lblStatus.Text = "Status: Cancelled";
        }
        catch (Exception ex)
        {
            Log($"--- fatal: {ex.Message}");
            lblStatus.Text = "Status: Error";
        }
        finally
        {
            _timer.Stop();
            _infoPanel?.Stop();
            _tablesView?.Stop();
            btnAnalyze.Enabled = true;
            btnPause.Enabled   = false;
        }
    }

    private void OnPause(object? sender, EventArgs e) => _cts?.Cancel();

    private void OnLoad(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "State / NDJSON / log files|*.tsv;*.json;*.ndjson;*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            txtReadFiles.Text   = File.ReadAllText(dlg.FileName);
            tabMain.SelectedTab = tabReadFiles;
            Log($"loaded: {dlg.FileName}");
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Filter   = "Text (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = "sitegrabber-log.txt",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            File.WriteAllText(dlg.FileName, rtbLog.Text);
            Log($"saved log: {dlg.FileName}");
        }
    }

    // ── intro + credits ──────────────────────────────────────────────────────
    private void WireIntroAndCredits()
    {
        _aboutIntro = new AboutIntro(pnlAbout);
        pnlAbout.VisibleChanged += (_, _) =>
        {
            if (pnlAbout.Visible) _aboutIntro.Play();
            else                  _aboutIntro.Stop();
        };

        _credits = new CreditRoll(pnlCredits);
        _credits.SetLines(new[]
        {
            new CreditLine("S I T E G R A B B E R", "Segoe UI", 28f,
                           FontStyle.Bold, Color.FromArgb(240, 240, 250),
                           Height: 44, Spacing: 40),
            new CreditLine("v3.0", "Consolas", 13f,
                           FontStyle.Regular, Color.FromArgb(120, 130, 150),
                           Height: 22, Spacing: 60),
            new CreditLine("Full-Site Mirror & Product Harvest Engine", "Consolas", 13f,
                           FontStyle.Regular, Color.FromArgb(160, 170, 190),
                           Height: 22, Spacing: 80),
            new CreditLine("Author", "Consolas", 11f,
                           FontStyle.Regular, Color.FromArgb(100, 110, 130),
                           Height: 20, Spacing: 4),
            new CreditLine("p0!z0ner", "Segoe UI", 16f,
                           FontStyle.Bold, Color.FromArgb(220, 225, 240),
                           Height: 26, Spacing: 60),
            new CreditLine("Contact", "Consolas", 11f,
                           FontStyle.Regular, Color.FromArgb(100, 110, 130),
                           Height: 20, Spacing: 4),
            new CreditLine("poizion@atomicmail.io", "Consolas", 14f,
                           FontStyle.Regular, Color.FromArgb(200, 210, 230),
                           Height: 22, Spacing: 60),
            new CreditLine("Engine", "Consolas", 11f,
                           FontStyle.Regular, Color.FromArgb(100, 110, 130),
                           Height: 20, Spacing: 4),
            new CreditLine("C# 12  ·  .NET 8  ·  WinForms  ·  HtmlAgilityPack",
                           "Consolas", 13f, FontStyle.Regular,
                           Color.FromArgb(200, 210, 230), Height: 22, Spacing: 60),
            new CreditLine("Other Projects", "Consolas", 11f,
                           FontStyle.Regular, Color.FromArgb(100, 110, 130),
                           Height: 20, Spacing: 4),
            new CreditLine("sitegrab-cpp     C++17 WinHTTP mirror engine", "Consolas", 13f,
                           FontStyle.Regular, Color.FromArgb(200, 210, 230),
                           Height: 22, Spacing: 4),
            new CreditLine("sitegrab-py      Python engine + headless Chromium", "Consolas", 13f,
                           FontStyle.Regular, Color.FromArgb(200, 210, 230),
                           Height: 22, Spacing: 4),
            new CreditLine("sitegrab-gui     C# WinForms shell + JSON-LD", "Consolas", 13f,
                           FontStyle.Regular, Color.FromArgb(200, 210, 230),
                           Height: 22, Spacing: 60),
            new CreditLine("Thanks", "Consolas", 11f,
                           FontStyle.Regular, Color.FromArgb(100, 110, 130),
                           Height: 20, Spacing: 4),
            new CreditLine("to everyone who crawls with a purpose quote I can kiLL any life", "Consolas", 13f,
                           FontStyle.Regular, Color.FromArgb(200, 210, 230),
                           Height: 22, Spacing: 80),
            new CreditLine("© 2026", "Consolas", 12f,
                           FontStyle.Regular, Color.FromArgb(140, 150, 170),
                           Height: 20, Spacing: 60),
            new CreditLine(" ", "Consolas", 1f,
                           FontStyle.Regular, Color.FromArgb(0, 0, 0),
                           Height: 200, Spacing: 0),
        });

        pnlCredits.VisibleChanged += (_, _) =>
        {
            if (pnlCredits.Visible) _credits.Play();
            else                    _credits.Stop();
        };
    }

    // ── info + tables + files ────────────────────────────────────────────────
    private void WireInfoTablesFiles()
    {
        _infoPanel  = new InfoPanel(txtInfo);
        _tablesView = new TablesView(dgvTables);
        _filesView  = new FilesView(tvFiles, txtReadFiles);

        btnTablesPages.Click  += (_, _) => _tablesView.SetView(TablesView.View.Pages);
        btnTablesAssets.Click += (_, _) => _tablesView.SetView(TablesView.View.Assets);

        _crawler.OnLog += e =>
        {
            BeginInvoke(() =>
            {
                if (e.Level == LogLevel.Success && e.Message.StartsWith("[page] "))
                    _tablesView.AddPage(e.Message[7..], "200", 0);
                else if (e.Level == LogLevel.Success && e.Message.StartsWith("[asset] "))
                    _tablesView.AddAsset(e.Message[8..], "asset", 0);
            });
        };

        _crawler.OnStats += (pages, assets, errors) =>
            BeginInvoke(() => _infoPanel.Update(pages, assets, errors));

        tabReadFiles.Enter += (_, _) =>
        {
            try
            {
                var host = new Uri(txtTarget.Text.Trim()).Host;
                var root = Path.Combine(_settings.OutputDir, host);
                _filesView.Load(root);
            }
            catch { }
        };
    }

    // ── cmd shell ────────────────────────────────────────────────────────────
    private void WireShell()
    {
        _shell.OnOutput += line => BeginInvoke(() => AppendShell(line));
        _shell.OnError  += line => BeginInvoke(() => AppendShell($"[err] {line}"));

        btnCmdRun.Click += (_, _) =>
        {
            var cmd = txtCmdInput.Text.Trim();
            if (cmd.Length == 0) return;
            AppendShell($"> {cmd}");
            _shell.Run(cmd);
        };
        btnCmdKill.Click += (_, _) => _shell.Kill();

        txtCmdInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                btnCmdRun.PerformClick();
            }
        };
    }

    private void AppendShell(string line)
    {
        txtCmdShell.AppendText(line + "\n");
        txtCmdShell.SelectionStart = txtCmdShell.TextLength;
        txtCmdShell.ScrollToCaret();
    }

    // ── query ────────────────────────────────────────────────────────────────
    private void WireQuery()
    {
        btnQueryRun.Click += (_, _) => RunQuery();
        txtQueryInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                RunQuery();
            }
        };
    }

    private void RunQuery()
    {
        string host;
        try { host = new Uri(txtTarget.Text.Trim()).Host; }
        catch { txtQuery.Text = "target URL is not a valid absolute URL"; return; }

        var ndjson = Path.Combine(_settings.OutputDir, host, "_products.ndjson");
        if (!File.Exists(ndjson))
        {
            txtQuery.Text = $"no product manifest at:\n  {ndjson}\n\nrun a crawl first.";
            return;
        }

        var q = txtQueryInput.Text;
        txtQuery.Text = $"query: {q}\nfile:  {ndjson}\n{new string('-', 60)}\n";

        try
        {
            var rows = ProductQuery.Run(ndjson, q);
            txtQuery.AppendText($"{rows.Count} rows\n{new string('-', 60)}\n");
            foreach (var r in rows)
                txtQuery.AppendText($"[{r.Kind}] {r.Url}\n  {r.Snippet}\n\n");
        }
        catch (Exception ex)
        {
            txtQuery.AppendText($"error: {ex.Message}\n");
        }
    }

    // ── settings ─────────────────────────────────────────────────────────────
    private void WireSettings()
    {
        pgSettings.SelectedObject = _settings;
        btnSettingsSave.Click += (_, _) =>
        {
            _settings.Save();
            Log($"settings saved → {AppContext.BaseDirectory}sitegrabber.settings.json");
        };
    }

    private void Log(string line)
    {
        rtbLog.SelectionStart  = rtbLog.TextLength;
        rtbLog.SelectionLength = 0;
        rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        rtbLog.ScrollToCaret();
    }
}