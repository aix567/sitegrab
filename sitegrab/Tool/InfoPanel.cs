// language: C#, file: InfoPanel.cs
// live session status — updates as crawl runs

using System.Diagnostics;
using System.Text;

namespace SiteGrabber;

public class InfoPanel
{
    private readonly RichTextBox _box;
    private readonly Stopwatch   _timer = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 250 };

    private string _target  = "";
    private string _output  = "";
    private int    _depth;
    private int    _threads;
    private string _mode    = "";
    private int    _pages, _assets, _errors;

    public InfoPanel(RichTextBox box)
    {
        _box = box;
        _box.Font        = new Font("Consolas", 9.5f);
        _box.BackColor   = Color.FromArgb(18, 18, 24);
        _box.ForeColor   = Color.LightGray;
        _box.BorderStyle = BorderStyle.None;
        _box.ReadOnly    = true;
        _tick.Tick += (_, _) => { if (_timer.IsRunning) Render(); };
    }

    public void Start(string target, string output, int depth, int threads, string mode)
    {
        _target  = target;
        _output  = output;
        _depth   = depth;
        _threads = threads;
        _mode    = mode;
        _pages = _assets = _errors = 0;
        _timer.Restart();
        _tick.Start();
        Render();
    }

    public void Update(int pages, int assets, int errors)
    {
        _pages  = pages;
        _assets = assets;
        _errors = errors;
    }

    public void Stop()
    {
        _timer.Stop();
        _tick.Stop();
        Render(final: true);
    }

    private void Render(bool final = false)
    {
        var sb  = new StringBuilder();
        var now = DateTime.Now;

        sb.AppendLine("─── Session ─────────────────────────────────");
        sb.AppendLine($"  Started      {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Elapsed      {_timer.Elapsed.TotalSeconds:F1}s");
        sb.AppendLine($"  State        {(final ? "finished" : "running")}");
        sb.AppendLine();
        sb.AppendLine("─── Target ──────────────────────────────────");
        sb.AppendLine($"  URL          {_target}");
        sb.AppendLine($"  Output       {_output}");
        sb.AppendLine($"  Mode         {_mode}");
        sb.AppendLine($"  Depth        {_depth}");
        sb.AppendLine($"  Threads      {_threads}");
        sb.AppendLine();
        sb.AppendLine("─── Live Counts ─────────────────────────────");
        sb.AppendLine($"  Pages        {_pages}");
        sb.AppendLine($"  Assets       {_assets}");
        sb.AppendLine($"  Errors       {_errors}");
        sb.AppendLine();

        if (final)
        {
            sb.AppendLine("─── Result ──────────────────────────────────");
            sb.AppendLine($"  Finished     {now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  Total        {_pages + _assets} items in {_timer.Elapsed.TotalSeconds:F1}s");
        }

        _box.Text = sb.ToString();
    }
}