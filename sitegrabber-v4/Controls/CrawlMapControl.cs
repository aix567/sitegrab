// language: C#, file: Controls/CrawlMapControl.cs
// Force-directed graph canvas — nodes pulse, edges draw, DispatcherTimer drives sim

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SiteGrabberV3.Models;

namespace SiteGrabberV3.Controls;

public class CrawlMapControl : Canvas
{
    // ── Dependency property ───────────────────────────────────────────────────
    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(nameof(Nodes),
            typeof(ObservableCollection<MapNode>),
            typeof(CrawlMapControl),
            new PropertyMetadata(null, OnNodesChanged));

    public ObservableCollection<MapNode>? Nodes
    {
        get => (ObservableCollection<MapNode>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    // ── Private state ─────────────────────────────────────────────────────────
    private DispatcherTimer? _timer;
    private readonly Dictionary<string, (Ellipse Circle, TextBlock Label)> _visual = new();
    private readonly List<(string From, string To, Line Edge)> _edges = new();

    // Force-sim constants
    private const double Repulsion  = 12_000;
    private const double Attraction = 0.03;
    private const double Damping    = 0.85;
    private const double MinDist    = 1.0;

    // Node appearance
    private static readonly Dictionary<NodeType, Color> _nodeColors = new()
    {
        [NodeType.Root]      = Color.FromRgb(0x00, 0xD4, 0xAA), // accent teal
        [NodeType.Page]      = Color.FromRgb(0x3B, 0x82, 0xF6), // blue
        [NodeType.Directory] = Color.FromRgb(0x22, 0xC5, 0x5E), // green
        [NodeType.File]      = Color.FromRgb(0xA8, 0x55, 0xF7), // purple
        [NodeType.Subdomain] = Color.FromRgb(0xF9, 0x73, 0x16), // orange
    };

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public CrawlMapControl()
    {
        Loaded   += (_, _) => StartTimer();
        Unloaded += (_, _) => StopTimer();
        ClipToBounds = true;
    }

    private static void OnNodesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (CrawlMapControl)d;
        if (e.OldValue is ObservableCollection<MapNode> old)
            old.CollectionChanged -= ctrl.Nodes_CollectionChanged;
        if (e.NewValue is ObservableCollection<MapNode> @new)
            @new.CollectionChanged += ctrl.Nodes_CollectionChanged;
        ctrl.RebuildGraph();
    }

    private void Nodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.InvokeAsync(RebuildGraph);

    // ── Timer ─────────────────────────────────────────────────────────────────
    private void StartTimer()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void StopTimer() { _timer?.Stop(); _timer = null; }

    // ── Graph build ───────────────────────────────────────────────────────────
    private void RebuildGraph()
    {
        Children.Clear();
        _visual.Clear();
        _edges.Clear();

        if (Nodes is null || Nodes.Count == 0) return;

        var w = ActualWidth  > 0 ? ActualWidth  : 500;
        var h = ActualHeight > 0 ? ActualHeight : 400;
        var cx = w / 2; var cy = h / 2;
        var rng = new Random(42);

        // Scatter nodes in random positions initially (centered)
        foreach (var node in Nodes)
        {
            if (node.X == 0 && node.Y == 0)
            {
                node.X = cx + (rng.NextDouble() - 0.5) * 200;
                node.Y = cy + (rng.NextDouble() - 0.5) * 200;
            }
        }

        // Draw edges first so they're underneath
        var nodeMap = Nodes.ToDictionary(n => n.Id);
        foreach (var node in Nodes)
        {
            foreach (var edgeId in node.Edges)
            {
                if (!nodeMap.ContainsKey(edgeId)) continue;
                var line = new Line
                {
                    Stroke          = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xD4, 0xAA)),
                    StrokeThickness = 1,
                    X1 = node.X, Y1 = node.Y,
                    X2 = nodeMap[edgeId].X, Y2 = nodeMap[edgeId].Y
                };
                Children.Add(line);
                _edges.Add((node.Id, edgeId, line));
            }
        }

        // Draw nodes
        foreach (var node in Nodes)
        {
            var color  = _nodeColors.TryGetValue(node.Type, out var c) ? c : Colors.Gray;
            var radius = node.Type == NodeType.Root ? 14.0 : 8.0;

            // Glow ring for root
            if (node.Type == NodeType.Root)
            {
                var glow = new Ellipse
                {
                    Width  = radius * 2 + 12,
                    Height = radius * 2 + 12,
                    Fill   = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B)),
                };
                SetLeft(glow, node.X - radius - 6);
                SetTop(glow, node.Y - radius - 6);
                Children.Add(glow);
                AnimatePulse(glow);
            }

            var circle = new Ellipse
            {
                Width  = radius * 2,
                Height = radius * 2,
                Fill   = new SolidColorBrush(color),
                ToolTip = node.Label,
            };
            SetLeft(circle, node.X - radius);
            SetTop(circle, node.Y - radius);
            Children.Add(circle);

            var label = new TextBlock
            {
                Text       = node.Label.Length > 18 ? node.Label[..15] + "…" : node.Label,
                FontSize   = node.Type == NodeType.Root ? 10 : 9,
                FontWeight = node.Type == NodeType.Root ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA3, 0xB9, 0xD2)),
            };
            SetLeft(label, node.X + radius + 3);
            SetTop(label, node.Y - 7);
            Children.Add(label);

            _visual[node.Id] = (circle, label);
        }
    }

    // ── Force simulation tick ─────────────────────────────────────────────────
    private void Tick()
    {
        if (Nodes is null || Nodes.Count == 0) return;

        var w = ActualWidth  > 10 ? ActualWidth  : 500;
        var h = ActualHeight > 10 ? ActualHeight : 400;
        var nodes = Nodes.ToList();

        // Repulsion (all pairs)
        for (int i = 0; i < nodes.Count; i++)
        {
            double fx = 0, fy = 0;
            for (int j = 0; j < nodes.Count; j++)
            {
                if (i == j) continue;
                var dx = nodes[i].X - nodes[j].X;
                var dy = nodes[i].Y - nodes[j].Y;
                var d2 = dx * dx + dy * dy;
                if (d2 < MinDist) d2 = MinDist;
                var f  = Repulsion / d2;
                fx += f * dx / Math.Sqrt(d2);
                fy += f * dy / Math.Sqrt(d2);
            }
            nodes[i].Vx = (nodes[i].Vx + fx) * Damping;
            nodes[i].Vy = (nodes[i].Vy + fy) * Damping;
        }

        // Attraction (edges)
        var nodeMap = nodes.ToDictionary(n => n.Id);
        foreach (var n in nodes)
        {
            foreach (var eId in n.Edges)
            {
                if (!nodeMap.TryGetValue(eId, out var t)) continue;
                var dx = t.X - n.X;
                var dy = t.Y - n.Y;
                n.Vx += dx * Attraction;
                n.Vy += dy * Attraction;
                t.Vx -= dx * Attraction;
                t.Vy -= dy * Attraction;
            }
        }

        // Gravity toward center
        var cx = w / 2; var cy = h / 2;
        foreach (var n in nodes)
        {
            n.Vx += (cx - n.X) * 0.002;
            n.Vy += (cy - n.Y) * 0.002;
        }

        // Integrate + boundary
        foreach (var n in nodes)
        {
            n.X = Math.Clamp(n.X + n.Vx, 20, w - 60);
            n.Y = Math.Clamp(n.Y + n.Vy, 20, h - 20);
        }

        // Reposition visual elements
        foreach (var n in nodes)
        {
            if (!_visual.TryGetValue(n.Id, out var vis)) continue;
            var r = n.Type == NodeType.Root ? 14.0 : 8.0;
            SetLeft(vis.Circle, n.X - r);
            SetTop(vis.Circle, n.Y - r);
            SetLeft(vis.Label, n.X + r + 3);
            SetTop(vis.Label, n.Y - 7);
        }

        // Reposition edges
        foreach (var (fromId, toId, line) in _edges)
        {
            if (!nodeMap.TryGetValue(fromId, out var f) ||
                !nodeMap.TryGetValue(toId,   out var t)) continue;
            line.X1 = f.X; line.Y1 = f.Y;
            line.X2 = t.X; line.Y2 = t.Y;
        }
    }

    // ── Root node pulse animation ──────────────────────────────────────────────
    private static void AnimatePulse(Ellipse el)
    {
        var anim = new DoubleAnimation
        {
            From           = 0.15,
            To             = 0.50,
            Duration       = TimeSpan.FromSeconds(1.5),
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var brush = (SolidColorBrush)el.Fill;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);

        var opAnim = new DoubleAnimation
        {
            From           = 0.15,
            To             = 0.50,
            Duration       = TimeSpan.FromSeconds(1.5),
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        el.BeginAnimation(OpacityProperty, opAnim);

        var scaleAnim = new DoubleAnimation
        {
            From           = 1.0,
            To             = 1.25,
            Duration       = TimeSpan.FromSeconds(1.5),
            AutoReverse    = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var st = new ScaleTransform(1, 1,
            el.Width / 2, el.Height / 2);
        el.RenderTransform = st;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim.Clone());
    }
}
