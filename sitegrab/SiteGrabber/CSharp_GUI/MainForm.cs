// language: C#, file: MainForm.cs
// WinForms GUI — dark theme, real-time log, full crawl controls

using System.Diagnostics;

namespace SiteGrabber;

public partial class MainForm : Form
{
    private readonly Crawler _crawler = new();
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _timer = new();

    // ── colors ────────────────────────────────────────────────────────────────
    private static readonly Color C_BG        = Color.FromArgb(18,  18,  24);
    private static readonly Color C_SURFACE   = Color.FromArgb(28,  28,  38);
    private static readonly Color C_BORDER    = Color.FromArgb(50,  50,  70);
    private static readonly Color C_ACCENT    = Color.FromArgb(99,  102, 241);
    private static readonly Color C_GREEN     = Color.FromArgb(52,  211, 153);
    private static readonly Color C_RED       = Color.FromArgb(239, 68,  68);
    private static readonly Color C_YELLOW    = Color.FromArgb(251, 191, 36);
    private static readonly Color C_TEXT      = Color.FromArgb(226, 232, 240);
    private static readonly Color C_MUTED     = Color.FromArgb(148, 163, 184);

    // ── controls ──────────────────────────────────────────────────────────────
    private TextBox  txtUrl      = null!;
    private TextBox  txtOutput   = null!;
    private Button   btnOutput   = null!;
    private NumericUpDown numDepth   = null!;
    private NumericUpDown numThreads = null!;
    private NumericUpDown numDelay   = null!;
    private CheckBox chkLinks    = null!;
    private CheckBox chkSitemap  = null!;
    private CheckBox chkImages   = null!;
    private CheckBox chkCss      = null!;
    private CheckBox chkJs       = null!;
    private CheckBox chkFonts    = null!;
    private CheckBox chkVideo    = null!;
    private CheckBox chkDocs     = null!;
    private Button   btnStart    = null!;
    private Button   btnStop     = null!;
    private Button   btnOpen     = null!;
    private ProgressBar pbMain   = null!;
    private RichTextBox rtbLog   = null!;
    private Label lblPages       = null!;
    private Label lblAssets      = null!;
    private Label lblErrors      = null!;
    private Label lblElapsed     = null!;
    private System.Windows.Forms.Timer tmrClock = null!;

    public MainForm()
    {
        InitializeComponent();
        WireEvents();
    }

    // ── layout ────────────────────────────────────────────────────────────────
    private void InitializeComponent()
    {
        SuspendLayout();

        Text            = "SiteGrabber v2.0";
        Size            = new Size(900, 740);
        MinimumSize     = new Size(720, 600);
        BackColor       = C_BG;
        ForeColor       = C_TEXT;
        Font            = new Font("Segoe UI", 9.5f);
        StartPosition   = FormStartPosition.CenterScreen;

        // ── URL row ───────────────────────────────────────────────────────────
        var lblUrl = MakeLabel("Target URL:", new Point(16, 18));
        txtUrl  = MakeTextBox("https://example.com", new Point(110, 14), 620);

        var lblOut = MakeLabel("Output dir:", new Point(16, 52));
        txtOutput  = MakeTextBox(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "grabbed"),
            new Point(110, 48), 560);
        btnOutput  = MakeButton("…", new Point(678, 47), 60, height: 28);
        btnOutput.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK) txtOutput.Text = dlg.SelectedPath;
        };

        // ── options panel ─────────────────────────────────────────────────────
        var pnlOpts = MakePanel(new Point(10, 84), new Size(860, 90));
        var optY    = 8;

        var lblDepth   = MakePanelLabel(pnlOpts, "Depth:",   new Point(10, optY + 3));
        numDepth       = MakeSpinner(pnlOpts, 1, 20, 4, new Point(58, optY));
        var lblThreads = MakePanelLabel(pnlOpts, "Threads:", new Point(130, optY + 3));
        numThreads     = MakeSpinner(pnlOpts, 1, 64, 16, new Point(190, optY));
        var lblDelay   = MakePanelLabel(pnlOpts, "Delay ms:", new Point(260, optY + 3));
        numDelay       = MakeSpinner(pnlOpts, 0, 5000, 0, new Point(328, optY));

        chkLinks   = MakePanelCheck(pnlOpts, "Follow Links",   new Point(410, optY), true);
        chkSitemap = MakePanelCheck(pnlOpts, "Parse Sitemap",  new Point(530, optY), true);

        optY = 44;
        chkImages = MakePanelCheck(pnlOpts, "Images",      new Point(10,  optY), true);
        chkCss    = MakePanelCheck(pnlOpts, "CSS",         new Point(90,  optY), true);
        chkJs     = MakePanelCheck(pnlOpts, "JavaScript",  new Point(150, optY), true);
        chkFonts  = MakePanelCheck(pnlOpts, "Fonts",       new Point(255, optY), true);
        chkVideo  = MakePanelCheck(pnlOpts, "Video/Audio", new Point(320, optY), true);
        chkDocs   = MakePanelCheck(pnlOpts, "Documents",   new Point(425, optY), true);

        // ── button row ────────────────────────────────────────────────────────
        btnStart = MakeButton("▶  START", new Point(16, 186), 140, accent: true);
        btnStop  = MakeButton("■  STOP",  new Point(166, 186), 100);
        btnOpen  = MakeButton("📂  Open Output", new Point(276, 186), 140);
        btnStop.Enabled = false;
        btnOpen.Enabled = false;

        pbMain = new ProgressBar
        {
            Location = new Point(430, 190),
            Size     = new Size(440, 22),
            Style    = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 0,
            BackColor = C_SURFACE,
        };

        // ── stat bar ──────────────────────────────────────────────────────────
        lblPages   = MakeStatLabel("Pages: 0",   new Point(16,  222));
        lblAssets  = MakeStatLabel("Assets: 0",  new Point(130, 222));
        lblErrors  = MakeStatLabel("Errors: 0",  new Point(250, 222));
        lblElapsed = MakeStatLabel("Elapsed: 0s",new Point(370, 222));

        // ── log box ───────────────────────────────────────────────────────────
        var lblLogHeader = MakeLabel("Live log:", new Point(16, 252));
        rtbLog = new RichTextBox
        {
            Location  = new Point(10, 270),
            Size      = new Size(860, 400),
            BackColor = C_SURFACE,
            ForeColor = C_TEXT,
            Font      = new Font("Consolas", 9f),
            ReadOnly  = true,
            ScrollBars= RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor    = AnchorStyles.Top | AnchorStyles.Bottom |
                        AnchorStyles.Left | AnchorStyles.Right,
        };

        // ── clock timer ───────────────────────────────────────────────────────
        tmrClock = new System.Windows.Forms.Timer { Interval = 500 };
        tmrClock.Tick += (_, _) =>
        {
            if (_timer.IsRunning)
                lblElapsed.Text = $"Elapsed: {(int)_timer.Elapsed.TotalSeconds}s";
        };

        // ── add to form ───────────────────────────────────────────────────────
        Controls.AddRange([
            lblUrl, txtUrl, lblOut, txtOutput, btnOutput,
            pnlOpts,
            btnStart, btnStop, btnOpen, pbMain,
            lblPages, lblAssets, lblErrors, lblElapsed,
            lblLogHeader, rtbLog,
        ]);

        ResumeLayout(true);
    }

    // ── event wiring ──────────────────────────────────────────────────────────
    private void WireEvents()
    {
        btnStart.Click += OnStart;
        btnStop.Click  += (_, _) => _cts?.Cancel();
        btnOpen.Click  += (_, _) =>
        {
            if (Directory.Exists(txtOutput.Text))
                Process.Start("explorer.exe", txtOutput.Text);
        };

        _crawler.OnLog   += entry => BeginInvoke(() => AppendLog(entry));
        _crawler.OnStats += (pages, assets, errors) => BeginInvoke(() =>
        {
            lblPages.Text  = $"Pages: {pages}";
            lblAssets.Text = $"Assets: {assets}";
            var c = errors > 0 ? C_RED : C_MUTED;
            lblErrors.Text  = $"Errors: {errors}";
            lblErrors.ForeColor = c;
        });
    }

    // ── start / stop ──────────────────────────────────────────────────────────
    private async void OnStart(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtUrl.Text)) return;

        rtbLog.Clear();
        lblPages.Text  = "Pages: 0";
        lblAssets.Text = "Assets: 0";
        lblErrors.Text = "Errors: 0";

        _cts = new CancellationTokenSource();
        btnStart.Enabled = false;
        btnStop.Enabled  = true;
        btnOpen.Enabled  = false;
        pbMain.MarqueeAnimationSpeed = 30;
        _timer.Restart();
        tmrClock.Start();

        var opts = new CrawlOptions(
            StartUrl     : txtUrl.Text.Trim(),
            OutputDir    : txtOutput.Text.Trim(),
            MaxDepth     : (int)numDepth.Value,
            Threads      : (int)numThreads.Value,
            DelayMs      : (int)numDelay.Value,
            FollowLinks  : chkLinks.Checked,
            ParseSitemap : chkSitemap.Checked,
            SaveImages   : chkImages.Checked,
            SaveCSS      : chkCss.Checked,
            SaveJS       : chkJs.Checked,
            SaveFonts    : chkFonts.Checked,
            SaveVideos   : chkVideo.Checked,
            SaveDocs     : chkDocs.Checked
        );

        try
        {
            await _crawler.RunAsync(opts, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog(LogEntry.Warn("crawl cancelled by user"));
        }
        catch (Exception ex)
        {
            AppendLog(LogEntry.Bad($"fatal: {ex.Message}"));
        }
        finally
        {
            _timer.Stop();
            tmrClock.Stop();
            btnStart.Enabled = true;
            btnStop.Enabled  = false;
            btnOpen.Enabled  = true;
            pbMain.MarqueeAnimationSpeed = 0;
        }
    }

    // ── log renderer ─────────────────────────────────────────────────────────
    private void AppendLog(LogEntry e)
    {
        var color = e.Level switch
        {
            LogLevel.Success => C_GREEN,
            LogLevel.Error   => C_RED,
            LogLevel.Warning => C_YELLOW,
            _                => C_MUTED,
        };
        var prefix = e.Level switch
        {
            LogLevel.Success => "✓ ",
            LogLevel.Error   => "✗ ",
            LogLevel.Warning => "⚠ ",
            _                => "  ",
        };

        rtbLog.SuspendLayout();
        rtbLog.SelectionStart  = rtbLog.TextLength;
        rtbLog.SelectionLength = 0;
        rtbLog.SelectionColor  = color;
        rtbLog.AppendText(prefix + e.Message + "\n");
        rtbLog.ScrollToCaret();
        rtbLog.ResumeLayout();
    }

    // ── factory helpers ───────────────────────────────────────────────────────
    private Label MakeLabel(string text, Point loc)
    {
        var l = new Label { Text = text, Location = loc, AutoSize = true,
                            ForeColor = C_MUTED };
        Controls.Add(l);
        return l;
    }

    private Label MakeStatLabel(string text, Point loc)
    {
        var l = new Label { Text = text, Location = loc, AutoSize = true,
                            ForeColor = C_MUTED, Font = new Font("Segoe UI", 9f) };
        return l;
    }

    private TextBox MakeTextBox(string ph, Point loc, int width)
    {
        var t = new TextBox
        {
            Text      = ph,
            Location  = loc,
            Size      = new Size(width, 28),
            BackColor = C_SURFACE,
            ForeColor = C_TEXT,
            BorderStyle = BorderStyle.FixedSingle,
        };
        return t;
    }

    private Button MakeButton(string text, Point loc, int width,
                               bool accent = false, int height = 30)
    {
        var b = new Button
        {
            Text      = text,
            Location  = loc,
            Size      = new Size(width, height),
            FlatStyle = FlatStyle.Flat,
            BackColor = accent ? C_ACCENT : C_SURFACE,
            ForeColor = C_TEXT,
            Cursor    = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = accent ? C_ACCENT : C_BORDER;
        b.FlatAppearance.MouseOverBackColor =
            accent ? Color.FromArgb(79, 82, 200) : Color.FromArgb(40, 40, 55);
        return b;
    }

    private Panel MakePanel(Point loc, Size size)
    {
        var p = new Panel
        {
            Location  = loc,
            Size      = size,
            BackColor = C_SURFACE,
            BorderStyle = BorderStyle.FixedSingle,
        };
        return p;
    }

    private static Label MakePanelLabel(Panel p, string text, Point loc)
    {
        var l = new Label
        {
            Text      = text,
            Location  = loc,
            AutoSize  = true,
            ForeColor = C_MUTED,
            BackColor = Color.Transparent,
        };
        p.Controls.Add(l);
        return l;
    }

    private static NumericUpDown MakeSpinner(Panel p, int min, int max, int val, Point loc)
    {
        var n = new NumericUpDown
        {
            Minimum   = min,
            Maximum   = max,
            Value     = val,
            Location  = loc,
            Size      = new Size(62, 24),
            BackColor = Color.FromArgb(38, 38, 52),
            ForeColor = C_TEXT,
        };
        p.Controls.Add(n);
        return n;
    }

    private static CheckBox MakePanelCheck(Panel p, string text, Point loc, bool chk)
    {
        var c = new CheckBox
        {
            Text      = text,
            Location  = loc,
            AutoSize  = true,
            Checked   = chk,
            ForeColor = C_TEXT,
            BackColor = Color.Transparent,
        };
        p.Controls.Add(c);
        return c;
    }
}
