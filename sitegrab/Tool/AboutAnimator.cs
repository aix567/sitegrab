// language: C#, file: AboutAnimator.cs
// animated intro (About tab) + film-credit scroller (Credits tab)

using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SiteGrabber;

public class BufferedPanel : Panel
{
    public BufferedPanel() { DoubleBuffered = true; }
}

public class AboutIntro
{
    private readonly Panel                      _host;
    private readonly System.Windows.Forms.Timer _t;
    private int _frame;
    private const int TotalFrames = 110;

    public event Action? Finished;

    public AboutIntro(Panel host)
    {
        _host = host;
        _t = new System.Windows.Forms.Timer { Interval = 16 };
        _t.Tick += (_, _) =>
        {
            _frame++;
            _host.Invalidate();
            if (_frame >= TotalFrames)
            {
                _t.Stop();
                Finished?.Invoke();
            }
        };
        _host.Paint += OnPaint;
    }

    public void Play() { _frame = 0; _t.Start(); }
    public void Stop() => _t.Stop();

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var w = _host.Width;
        var h = _host.Height;
        g.Clear(Color.FromArgb(12, 12, 18));

        float t = Math.Clamp(_frame / (float)TotalFrames, 0f, 1f);

        float pTitle = Math.Clamp(t / 0.30f, 0f, 1f);
        float pSub   = Math.Clamp((t - 0.30f) / 0.20f, 0f, 1f);
        float pScan  = Math.Clamp((t - 0.50f) / 0.18f, 0f, 1f);
        float pInfo  = Math.Clamp((t - 0.68f) / 0.28f, 0f, 1f);
        float pBar   = Math.Clamp(t, 0f, 1f);

        float easeInfo = 1f - (float)Math.Pow(1f - pInfo, 3);

        using var titleFont = new Font("Segoe UI", 44f, FontStyle.Bold, GraphicsUnit.Pixel);
        const string titleText = "S I T E G R A B B E R";
        var titleSize = g.MeasureString(titleText, titleFont);
        var titleX    = (w - titleSize.Width) / 2f;
        var titleY    = h * 0.16f;

        int titleAlpha = (int)(pTitle * 255);
        using (var brush = new SolidBrush(Color.FromArgb(titleAlpha, 240, 240, 250)))
            g.DrawString(titleText, titleFont, brush, titleX, titleY);

        const string fullSub = "Full-Site Mirror & Product Harvest Engine";
        int subChars = (int)(fullSub.Length * pSub);
        var subShown = fullSub.Substring(0, subChars);

        using var subFont = new Font("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        var subSize = g.MeasureString(fullSub, subFont);
        var subX    = (w - subSize.Width) / 2f;
        var subY    = titleY + titleSize.Height + 6;

        if (subChars > 0)
        {
            using var brush = new SolidBrush(Color.FromArgb(170, 185, 205));
            g.DrawString(subShown, subFont, brush, subX, subY);
        }

        if (pSub < 1f && ((_frame / 8) % 2) == 0)
        {
            var caretX = subX + g.MeasureString(subShown, subFont).Width + 1;
            using var caretBrush = new SolidBrush(Color.FromArgb(170, 185, 205));
            g.FillRectangle(caretBrush, caretX, subY + 2, 2, subSize.Height - 4);
        }

        if (pScan > 0f && pScan < 1f)
        {
            var scanY = (int)(h * pScan);
            for (int i = 6; i >= 1; i--)
            {
                using var glow = new Pen(
                    Color.FromArgb(Math.Max(0, 55 - i * 8), 99, 102, 241), 1f);
                g.DrawLine(glow, 0, scanY - i, w, scanY - i);
            }
            using var pen = new Pen(Color.FromArgb(160, 120, 124, 255), 1.5f);
            g.DrawLine(pen, 0, scanY, w, scanY);
        }

        if (pInfo > 0f)
        {
            var offset = (int)((1f - easeInfo) * 70f);
            var yBase  = h * 0.55f + offset;

            using var keyFont = new Font("Consolas", 13f, FontStyle.Bold,    GraphicsUnit.Pixel);
            using var valFont = new Font("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);

            int infoAlpha = (int)(easeInfo * 230);
            using var keyBrush = new SolidBrush(Color.FromArgb(infoAlpha, 140, 160, 190));
            using var valBrush = new SolidBrush(Color.FromArgb(infoAlpha, 205, 215, 230));

            var lines = new (string key, string val)[]
            {
                ("Author:",  "p0!z0ner"),
                ("Contact:", "poizion@atomicmail.io"),
                ("Source:",  "https://github.com/aix567"),
                ("Support:", "https://buymeacoffee.com/ori0n"),
                ("License:", "Proprietary — single-user"),
            };

            int i = 0;
            foreach (var (k, v) in lines)
            {
                var y = yBase + i * 22;
                g.DrawString(k, keyFont, keyBrush, w * 0.28f, y);
                g.DrawString(v, valFont, valBrush, w * 0.42f, y);
                i++;
            }
        }

        using var bar = new SolidBrush(Color.FromArgb(80, 99, 102, 241));
        g.FillRectangle(bar, 0, h - 3, (int)(w * pBar), 3);
    }
}

public class CreditRoll
{
    private readonly Panel                      _host;
    private readonly System.Windows.Forms.Timer _t;
    private float _offsetY;
    private readonly List<CreditLine> _lines = new();
    private int  _contentHeight;
    private bool _paused;
    private int  _tailFrames;

    private const float SpeedPxPerFrame = 0.7f;
    private const int   Fps             = 60;
    private const int   TailPauseFrames = 120;
    private const float FadeBandPx      = 80f;

    public CreditRoll(Panel host)
    {
        _host = host;
        _t = new System.Windows.Forms.Timer { Interval = 1000 / Fps };
        _t.Tick += (_, _) => Step();
        _host.Paint  += OnPaint;
        _host.Resize += (_, _) => _host.Invalidate();
    }

    public void SetLines(IEnumerable<CreditLine> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
        Measure();
        _offsetY    = _host.Height;
        _tailFrames = 0;
        _host.Invalidate();
    }

    public void Play()
    {
        _offsetY    = _host.Height;
        _tailFrames = 0;
        _paused     = false;
        _t.Start();
    }

    public void Pause() => _paused = !_paused;
    public void Stop()  { _t.Stop(); _host.Invalidate(); }

    private void Measure()
    {
        int y = 0;
        foreach (var l in _lines) y += l.Height + l.Spacing;
        _contentHeight = y;
    }

    private void Step()
    {
        if (_paused) return;
        _offsetY -= SpeedPxPerFrame;
        _host.Invalidate();

        if (_offsetY + _contentHeight < 0)
        {
            _tailFrames++;
            if (_tailFrames > TailPauseFrames)
            {
                _offsetY    = _host.Height;
                _tailFrames = 0;
            }
        }
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(8, 8, 14));

        var w = _host.Width;
        var h = _host.Height;

        float y = _offsetY;
        foreach (var line in _lines)
        {
            if (y + line.Height > -20 && y < h + 20)
            {
                float alphaTop    = Math.Clamp(y / FadeBandPx, 0f, 1f);
                float alphaBottom = Math.Clamp((h - y - line.Height) / FadeBandPx, 0f, 1f);
                float alpha       = Math.Min(alphaTop, alphaBottom);

                using var font = new Font(line.FontFamily, line.Size,
                                          line.Style, GraphicsUnit.Pixel);
                var size = g.MeasureString(line.Text, font);
                var x    = (w - size.Width) / 2f;

                var baseColor = line.Color ?? System.Drawing.Color.FromArgb(180, 190, 210);
                int a = (int)(alpha * 255);

                using var brush = new SolidBrush(
                    System.Drawing.Color.FromArgb(a, baseColor.R, baseColor.G, baseColor.B));
                g.DrawString(line.Text, font, brush, x, y);
            }
            y += line.Height + line.Spacing;
        }

        using (var topFade = new LinearGradientBrush(
            new Rectangle(0, 0, w, (int)FadeBandPx),
            Color.FromArgb(220, 8, 8, 14),
            Color.FromArgb(0, 8, 8, 14),
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(topFade, 0, 0, w, FadeBandPx);
        }

        using (var botFade = new LinearGradientBrush(
            new Rectangle(0, (int)(h - FadeBandPx), w, (int)FadeBandPx),
            Color.FromArgb(0, 8, 8, 14),
            Color.FromArgb(220, 8, 8, 14),
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(botFade, 0, h - FadeBandPx, w, FadeBandPx);
        }
    }
}

public record CreditLine(
    string    Text,
    string    FontFamily = "Consolas",
    float     Size       = 13f,
    FontStyle Style      = FontStyle.Regular,
    Color?    Color      = null,
    int       Height     = 22,
    int       Spacing    = 8
);