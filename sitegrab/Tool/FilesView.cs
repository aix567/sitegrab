// language: C#, file: FilesView.cs
// left tree of mirrored files + right preview pane

using System.Text;

namespace SiteGrabber;

public class FilesView
{
    private readonly TreeView    _tree;
    private readonly RichTextBox _preview;
    private readonly PictureBox  _imagePreview;

    public FilesView(TreeView tree, RichTextBox preview)
    {
        _tree    = tree;
        _preview = preview;

        _imagePreview = new PictureBox
        {
            Dock      = DockStyle.Fill,
            SizeMode  = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(18, 18, 24),
            Visible   = false,
        };

        if (_preview.Parent != null)
        {
            _preview.Parent.Controls.Add(_imagePreview);
            _imagePreview.BringToFront();
        }

        _preview.Font        = new Font("Consolas", 9f);
        _preview.BackColor   = Color.FromArgb(18, 18, 24);
        _preview.ForeColor   = Color.LightGray;
        _preview.BorderStyle = BorderStyle.None;
        _preview.ReadOnly    = true;
        _preview.WordWrap    = false;

        _tree.AfterSelect += (_, e) => ShowPreview(e.Node);
    }

    public void Load(string rootDir)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        if (!Directory.Exists(rootDir))
        {
            _tree.Nodes.Add($"(no files at {rootDir})");
            _tree.EndUpdate();
            return;
        }

        var rootNode = new TreeNode(rootDir) { Tag = rootDir };
        AddDirectory(rootDir, rootNode);
        rootNode.Expand();
        _tree.Nodes.Add(rootNode);

        _tree.EndUpdate();
    }

    private void AddDirectory(string path, TreeNode parent)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".")) continue;
                var node = new TreeNode(name) { Tag = dir };
                AddDirectory(dir, node);
                parent.Nodes.Add(node);
            }

            foreach (var file in Directory.GetFiles(path))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith(".")) continue;
                parent.Nodes.Add(new TreeNode(name) { Tag = file });
            }
        }
        catch { }
    }

    private void ShowPreview(TreeNode? node)
    {
        if (node?.Tag is not string path) return;

        if (Directory.Exists(path)) { ShowDirInfo(path); return; }
        if (!File.Exists(path)) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico")
        {
            try
            {
                using var fs = File.OpenRead(path);
                using var img = Image.FromStream(fs);
                _imagePreview.Image = new Bitmap(img);
                _imagePreview.Visible = true;
                _imagePreview.BringToFront();
                _preview.Visible = false;
                return;
            }
            catch { }
        }

        _imagePreview.Visible = false;
        _preview.Visible = true;
        _preview.BringToFront();

        try
        {
            var fi = new FileInfo(path);
            if (fi.Length > 512 * 1024)
            {
                _preview.Text = $"(file too large to preview: {fi.Length:N0} bytes)\n{path}";
                return;
            }

            if (ext is ".html" or ".htm" or ".css" or ".js" or ".json" or
                ".xml"  or ".txt" or ".md"  or ".tsv" or ".csv" or ".ndjson")
            {
                _preview.Text = File.ReadAllText(path, Encoding.UTF8);
            }
            else
            {
                _preview.Text = $"(binary file — {fi.Length:N0} bytes)\n{path}";
            }
        }
        catch (Exception ex)
        {
            _preview.Text = $"error reading file:\n{ex.Message}";
        }
    }

    private void ShowDirInfo(string dir)
    {
        _imagePreview.Visible = false;
        _preview.Visible = true;
        _preview.BringToFront();

        try
        {
            var files = Directory.GetFiles(dir);
            var dirs  = Directory.GetDirectories(dir);
            long total = files.Sum(f => new FileInfo(f).Length);

            var sb = new StringBuilder();
            sb.AppendLine($"Directory: {dir}");
            sb.AppendLine($"Files:     {files.Length}");
            sb.AppendLine($"Subdirs:   {dirs.Length}");
            sb.AppendLine($"Size:      {total:N0} bytes");
            sb.AppendLine();
            sb.AppendLine("─── Contents ─────────────────────────");
            foreach (var f in files.Take(200))
                sb.AppendLine($"  {Path.GetFileName(f)}");
            _preview.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            _preview.Text = $"error: {ex.Message}";
        }
    }
}