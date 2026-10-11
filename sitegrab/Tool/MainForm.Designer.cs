#nullable enable
// language: C#, file: MainForm.Designer.cs

namespace SiteGrabber;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    private GroupBox grpTarget       = null!;
    private Label    lblTarget       = null!;
    private TextBox  txtTarget       = null!;
    private Label    lblKeyword      = null!;
    private TextBox  txtKeyword      = null!;
    private CheckBox chkAutoDetect   = null!;
    private Label    lblDatabase     = null!;
    private ComboBox cmbDatabase     = null!;
    private Label    lblMethod       = null!;
    private ComboBox cmbMethod       = null!;
    private Label    lblType         = null!;
    private ComboBox cmbType         = null!;
    private CheckBox chkSyntax       = null!;

    private Button   btnAnalyze      = null!;
    private Button   btnPause        = null!;
    private Button   btnLoad         = null!;
    private Button   btnSave         = null!;

    private TabControl tabMain       = null!;
    private TabPage  tabAbout        = null!;
    private TabPage  tabInfo         = null!;
    private TabPage  tabTables       = null!;
    private TabPage  tabReadFiles    = null!;
    private TabPage  tabCmdShell     = null!;
    private TabPage  tabQuery        = null!;
    private TabPage  tabSettings     = null!;
    private TabPage  tabCredits      = null!;

    private BufferedPanel pnlAbout   = null!;
    private BufferedPanel pnlCredits = null!;

    private RichTextBox  txtInfo     = null!;
    private DataGridView dgvTables   = null!;
    private Button       btnTablesPages  = null!;
    private Button       btnTablesAssets = null!;
    private TreeView     tvFiles     = null!;
    private RichTextBox  txtReadFiles = null!;

    private TextBox      txtCmdShell  = null!;
    private TextBox      txtCmdInput  = null!;
    private Button       btnCmdRun    = null!;
    private Button       btnCmdKill   = null!;

    private TextBox      txtQuery     = null!;
    private TextBox      txtQueryInput = null!;
    private Button       btnQueryRun  = null!;

    private PropertyGrid pgSettings    = null!;
    private Button       btnSettingsSave = null!;

    private StatusStrip          statusStrip = null!;
    private ToolStripStatusLabel lblStatus   = null!;
    private ToolStripStatusLabel lblMode     = null!;

    private RichTextBox rtbLog = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        this.SuspendLayout();

        this.Text          = "SiteGrabber v3 - Advanced Site Mirror Tool";
        this.ClientSize    = new Size(760, 720);
        this.MinimumSize   = new Size(700, 640);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Font          = new Font("Tahoma", 8.25f);

        // ── target group box ─────────────────────────────────────────────────
        grpTarget = new GroupBox
        {
            Text     = "Target",
            Location = new Point(12, 6),
            Size     = new Size(736, 130),
        };

        lblTarget = new Label { Text = "Target", Location = new Point(12, 25), AutoSize = true };
        txtTarget = new TextBox
        {
            Location = new Point(88, 22),
            Size     = new Size(430, 20),
            Text     = "https://",
        };
        btnAnalyze = new Button
        {
            Text     = "Analyze",
            Location = new Point(530, 12),
            Size     = new Size(90, 36),
        };
        btnPause = new Button
        {
            Text     = "Pause",
            Location = new Point(630, 12),
            Size     = new Size(90, 36),
            Enabled  = false,
        };

        lblKeyword = new Label { Text = "Keyword", Location = new Point(12, 55), AutoSize = true };
        txtKeyword = new TextBox { Location = new Point(88, 52), Size = new Size(160, 20) };
        chkAutoDetect = new CheckBox
        {
            Text     = "Auto Detect",
            Location = new Point(260, 53),
            AutoSize = true,
            Checked  = true,
        };
        chkSyntax = new CheckBox
        {
            Text     = "Syntax",
            Location = new Point(360, 53),
            AutoSize = true,
            Checked  = true,
        };

        lblDatabase = new Label { Text = "Database", Location = new Point(12, 88), AutoSize = true };
        cmbDatabase = new ComboBox
        {
            Location      = new Point(88, 85),
            Size          = new Size(160, 21),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        cmbDatabase.Items.AddRange(new object[]
        {
            "Auto Detect", "HTML Pages", "Products / JSON-LD",
            "Search Backend", "Sitemap Only", "Full Mirror"
        });
        cmbDatabase.SelectedIndex = 0;

        lblMethod = new Label { Text = "Method", Location = new Point(260, 88), AutoSize = true };
        cmbMethod = new ComboBox
        {
            Location      = new Point(318, 85),
            Size          = new Size(80, 21),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        cmbMethod.Items.AddRange(new object[] { "GET", "HEAD" });
        cmbMethod.SelectedIndex = 0;

        lblType = new Label { Text = "Type", Location = new Point(410, 88), AutoSize = true };
        cmbType = new ComboBox
        {
            Location      = new Point(448, 85),
            Size          = new Size(130, 21),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        cmbType.Items.AddRange(new object[]
        {
            "Auto Detect", "Static Mirror", "Paginated Deep", "Product Catalog"
        });
        cmbType.SelectedIndex = 0;

        btnLoad = new Button { Text = "Load", Location = new Point(630, 50), Size = new Size(90, 28) };
        btnSave = new Button { Text = "Save", Location = new Point(630, 82), Size = new Size(90, 28) };

        grpTarget.Controls.AddRange(new Control[]
        {
            lblTarget, txtTarget, btnAnalyze, btnPause,
            lblKeyword, txtKeyword, chkAutoDetect, chkSyntax,
            lblDatabase, cmbDatabase, lblMethod, cmbMethod,
            lblType, cmbType, btnLoad, btnSave,
        });

        // ── tab control ──────────────────────────────────────────────────────
        tabMain = new TabControl
        {
            Location = new Point(12, 142),
            Size     = new Size(736, 380),
            Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        tabAbout     = new TabPage("About");
        tabInfo      = new TabPage("Info");
        tabTables    = new TabPage("Tables");
        tabReadFiles = new TabPage("Read Files");
        tabCmdShell  = new TabPage("Cmd Shell");
        tabQuery     = new TabPage("Query");
        tabSettings  = new TabPage("Settings");
        tabCredits   = new TabPage("Credits");

        // About tab — animated intro canvas
        pnlAbout = new BufferedPanel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 12, 18),
            TabStop   = false,
        };
        tabAbout.Controls.Add(pnlAbout);

        // Credits tab — film roll canvas
        pnlCredits = new BufferedPanel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 8, 14),
            TabStop   = false,
        };
        tabCredits.Controls.Add(pnlCredits);

        // ── info tab ─────────────────────────────────────────────────────────
        txtInfo = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            ReadOnly    = true,
            BackColor   = Color.FromArgb(18, 18, 24),
            ForeColor   = Color.LightGray,
            BorderStyle = BorderStyle.None,
            Font        = new Font("Consolas", 9.5f),
        };
        tabInfo.Controls.Add(txtInfo);

        // ── tables tab ───────────────────────────────────────────────────────
        var pnlTables = new Panel { Dock = DockStyle.Fill };
        var pnlTblBar = new Panel { Dock = DockStyle.Top, Height = 32 };
        btnTablesPages  = new Button { Text = "Pages",  Location = new Point(0,   4), Size = new Size(80, 24) };
        btnTablesAssets = new Button { Text = "Assets", Location = new Point(88,  4), Size = new Size(80, 24) };
        pnlTblBar.Controls.Add(btnTablesPages);
        pnlTblBar.Controls.Add(btnTablesAssets);
        dgvTables = new DataGridView
        {
            Dock                  = DockStyle.Fill,
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            ReadOnly              = true,
            RowHeadersVisible     = false,
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor       = Color.White,
            BorderStyle           = BorderStyle.None,
        };
        dgvTables.Columns.Add("url",  "URL");
        dgvTables.Columns.Add("kind", "Kind");
        dgvTables.Columns.Add("size", "Size");
        pnlTables.Controls.Add(dgvTables);
        pnlTables.Controls.Add(pnlTblBar);
        tabTables.Controls.Add(pnlTables);

        // ── read files tab ───────────────────────────────────────────────────
        var split = new SplitContainer
        {
            Dock             = DockStyle.Fill,
            SplitterDistance = 240,
            SplitterWidth    = 4,
        };
        tvFiles = new TreeView
        {
            Dock          = DockStyle.Fill,
            Font          = new Font("Consolas", 8.5f),
            BackColor     = Color.FromArgb(28, 28, 38),
            ForeColor     = Color.LightGray,
            BorderStyle   = BorderStyle.None,
            HideSelection = false,
        };
        split.Panel1.Controls.Add(tvFiles);

        txtReadFiles = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            ReadOnly    = true,
            BackColor   = Color.FromArgb(18, 18, 24),
            ForeColor   = Color.LightGray,
            BorderStyle = BorderStyle.None,
            Font        = new Font("Consolas", 9f),
            WordWrap    = false,
        };
        split.Panel2.Controls.Add(txtReadFiles);
        tabReadFiles.Controls.Add(split);

        // ── cmd shell tab ────────────────────────────────────────────────────
        var pnlCmd = new Panel { Dock = DockStyle.Fill };
        txtCmdInput = new TextBox
        {
            Dock = DockStyle.Top,
            Font = new Font("Consolas", 9f),
        };
        var pnlCmdBtns = new Panel { Dock = DockStyle.Top, Height = 32 };
        btnCmdRun  = new Button { Text = "Run",  Location = new Point(0,   4), Size = new Size(70, 24) };
        btnCmdKill = new Button { Text = "Kill", Location = new Point(78,  4), Size = new Size(70, 24) };
        pnlCmdBtns.Controls.Add(btnCmdRun);
        pnlCmdBtns.Controls.Add(btnCmdKill);
        txtCmdShell = new TextBox
        {
            Dock       = DockStyle.Fill,
            Multiline  = true,
            ScrollBars = ScrollBars.Both,
            Font       = new Font("Consolas", 9f),
            BackColor  = Color.Black,
            ForeColor  = Color.LightGreen,
            ReadOnly   = true,
        };
        pnlCmd.Controls.Add(txtCmdShell);
        pnlCmd.Controls.Add(pnlCmdBtns);
        pnlCmd.Controls.Add(txtCmdInput);
        tabCmdShell.Controls.Add(pnlCmd);

        // ── query tab ────────────────────────────────────────────────────────
        var pnlQuery = new Panel { Dock = DockStyle.Fill };
        txtQueryInput = new TextBox
        {
            Dock = DockStyle.Top,
            Font = new Font("Consolas", 9f),
            Text = "Product AND contains \"offer\" LIMIT 100",
        };
        var pnlQueryBtns = new Panel { Dock = DockStyle.Top, Height = 32 };
        btnQueryRun = new Button { Text = "Run Query", Location = new Point(0, 4), Size = new Size(110, 24) };
        pnlQueryBtns.Controls.Add(btnQueryRun);
        txtQuery = new TextBox
        {
            Dock       = DockStyle.Fill,
            Multiline  = true,
            ScrollBars = ScrollBars.Both,
            Font       = new Font("Consolas", 9f),
            BackColor  = Color.Black,
            ForeColor  = Color.LightCyan,
            ReadOnly   = true,
        };
        pnlQuery.Controls.Add(txtQuery);
        pnlQuery.Controls.Add(pnlQueryBtns);
        pnlQuery.Controls.Add(txtQueryInput);
        tabQuery.Controls.Add(pnlQuery);

        // ── settings tab ─────────────────────────────────────────────────────
        var pnlSettings = new Panel { Dock = DockStyle.Fill };
        btnSettingsSave = new Button
        {
            Text = "Save Settings", Dock = DockStyle.Bottom, Height = 28,
        };
        pgSettings = new PropertyGrid { Dock = DockStyle.Fill };
        pnlSettings.Controls.Add(pgSettings);
        pnlSettings.Controls.Add(btnSettingsSave);
        tabSettings.Controls.Add(pnlSettings);

        tabMain.Controls.AddRange(new Control[]
        {
            tabAbout, tabInfo, tabTables, tabReadFiles,
            tabCmdShell, tabQuery, tabSettings, tabCredits,
        });

        // ── status strip ─────────────────────────────────────────────────────
        statusStrip = new StatusStrip
        {
            Location   = new Point(0, 528),
            SizingGrip = false,
        };
        lblStatus = new ToolStripStatusLabel
        {
            Text      = "Status: Idle",
            Spring    = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        lblMode = new ToolStripStatusLabel
        {
            Text      = "Mode: Static Mirror",
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = Color.DarkBlue,
        };
        statusStrip.Items.Add(lblStatus);
        statusStrip.Items.Add(lblMode);

        // ── log pane ─────────────────────────────────────────────────────────
        rtbLog = new RichTextBox
        {
            Location    = new Point(12, 556),
            Size        = new Size(736, 148),
            Anchor      = AnchorStyles.Top | AnchorStyles.Left |
                          AnchorStyles.Right | AnchorStyles.Bottom,
            ReadOnly    = true,
            BackColor   = Color.Black,
            ForeColor   = Color.LightGreen,
            Font        = new Font("Consolas", 8.5f),
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars  = RichTextBoxScrollBars.Vertical,
        };

        this.Controls.Add(grpTarget);
        this.Controls.Add(tabMain);
        this.Controls.Add(statusStrip);
        this.Controls.Add(rtbLog);

        this.ResumeLayout(false);
        this.PerformLayout();
    }
}