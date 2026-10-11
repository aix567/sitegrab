// language: C#, file: TablesView.cs
// live pages + assets grid — fed from crawler events

using System.Collections.Concurrent;

namespace SiteGrabber;

public class TablesView
{
    public enum View { Pages, Assets }

    private readonly DataGridView _grid;
    private readonly ConcurrentQueue<Row> _pending = new();
    private readonly System.Windows.Forms.Timer _flush = new() { Interval = 200 };

    private View _view = View.Pages;

    public record Row(string Url, string Kind, string Size);

    public TablesView(DataGridView grid)
    {
        _grid = grid;
        _grid.VirtualMode        = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Clear();
        _grid.Columns.Add("url",  "URL");
        _grid.Columns.Add("kind", "Kind");
        _grid.Columns.Add("size", "Size");
        _grid.ColumnHeadersDefaultCellStyle.Font =
            new Font("Segoe UI", 9f, FontStyle.Bold);

        _flush.Tick += (_, _) => FlushPending();
    }

    public void SetView(View v)
    {
        _view = v;
        _grid.Rows.Clear();
    }

    public void Clear()
    {
        _grid.Rows.Clear();
        while (_pending.TryDequeue(out _)) { }
    }

    public void AddPage(string url, string status, long bytes)
    {
        if (_view != View.Pages) return;
        _pending.Enqueue(new Row(url, status, HumanSize(bytes)));
    }

    public void AddAsset(string url, string kind, long bytes)
    {
        if (_view != View.Assets) return;
        _pending.Enqueue(new Row(url, kind, HumanSize(bytes)));
    }

    public void Start() => _flush.Start();
    public void Stop()  => _flush.Stop();

    private void FlushPending()
    {
        if (_pending.IsEmpty) return;

        _grid.SuspendLayout();
        int added = 0;
        while (_pending.TryDequeue(out var row))
        {
            _grid.Rows.Add(row.Url, row.Kind, row.Size);
            if (++added >= 500) break;
        }
        _grid.ResumeLayout();

        if (_grid.Rows.Count > 0)
        {
            try
            {
                _grid.FirstDisplayedScrollingRowIndex = _grid.Rows.Count - 1;
            }
            catch { }
        }
    }

    private static string HumanSize(long b)
    {
        if (b >= 1L << 30) return $"{b / (double)(1L << 30):F1} GiB";
        if (b >= 1L << 20) return $"{b / (double)(1L << 20):F1} MiB";
        if (b >= 1L << 10) return $"{b / (double)(1L << 10):F1} KiB";
        return $"{b} B";
    }
}