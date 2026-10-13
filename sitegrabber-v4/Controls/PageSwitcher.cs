// language: C#, file: Controls/PageSwitcher.cs
// Animated switching between sibling panes that share one grid cell.
//   • outgoing pane fades + drifts away (120 ms)
//   • incoming pane fades + slides in from the direction of travel (300 ms)
// Direction follows the order the pages were registered in, so moving "down"
// the sidebar slides content up, moving "up" slides it down.

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SiteGrabberV3.Controls;

public sealed class PageSwitcher
{
    private const double SlideDistance = 18;

    private static readonly Duration InDuration  = new(TimeSpan.FromMilliseconds(300));
    private static readonly Duration OutDuration = new(TimeSpan.FromMilliseconds(120));

    private readonly List<(string Key, FrameworkElement Page)> _pages = new();
    private FrameworkElement? _current;

    public PageSwitcher(IEnumerable<(string Key, FrameworkElement Page)> pages, string initialKey)
    {
        foreach (var p in pages)
        {
            // Own, unfrozen transform per page so it can be animated.
            p.Page.RenderTransform = new TranslateTransform();
            p.Page.Visibility = p.Key == initialKey ? Visibility.Visible : Visibility.Collapsed;
            _pages.Add(p);
            if (p.Key == initialKey) _current = p.Page;
        }
    }

    public void Show(string key)
    {
        var newIdx = _pages.FindIndex(p => p.Key == key);
        if (newIdx < 0) return;

        var next = _pages[newIdx].Page;
        if (ReferenceEquals(next, _current)) return;

        var oldIdx = _current is null
            ? -1
            : _pages.FindIndex(p => ReferenceEquals(p.Page, _current));
        var dir  = newIdx >= oldIdx ? 1 : -1;
        var prev = _current;

        _current = next;
        if (prev is not null) FadeOut(prev, dir);
        SlideIn(next, dir);
    }

    // ── Outgoing ──────────────────────────────────────────────────────────────
    private void FadeOut(FrameworkElement page, int dir)
    {
        page.IsHitTestVisible = false;

        var fade = new DoubleAnimation
        {
            To           = 0,
            Duration     = OutDuration,
            FillBehavior = FillBehavior.HoldEnd,
        };
        fade.Completed += (_, _) =>
        {
            // The user may have switched back to this page while it was fading.
            if (ReferenceEquals(page, _current)) return;

            page.BeginAnimation(UIElement.OpacityProperty, null);
            if (page.RenderTransform is TranslateTransform t)
                t.BeginAnimation(TranslateTransform.YProperty, null);

            page.Opacity          = 1;
            page.IsHitTestVisible = true;
            page.Visibility       = Visibility.Collapsed;
        };
        page.BeginAnimation(UIElement.OpacityProperty, fade);

        if (page.RenderTransform is TranslateTransform tr)
        {
            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                To           = -dir * SlideDistance * 0.5,
                Duration     = OutDuration,
                FillBehavior = FillBehavior.HoldEnd,
            });
        }
    }

    // ── Incoming ──────────────────────────────────────────────────────────────
    private static void SlideIn(FrameworkElement page, int dir)
    {
        var wasHidden = page.Visibility != Visibility.Visible;

        // If the page is mid-fade-out, continue from where it currently is.
        var fromOpacity = wasHidden ? 0 : page.Opacity;
        var tr          = page.RenderTransform as TranslateTransform;
        var fromY       = wasHidden || tr is null ? dir * SlideDistance : tr.Y;

        page.BeginAnimation(UIElement.OpacityProperty, null);
        tr?.BeginAnimation(TranslateTransform.YProperty, null);
        page.Opacity          = 1;
        page.IsHitTestVisible = true;
        page.Visibility       = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From           = fromOpacity,
            To             = 1,
            BeginTime      = TimeSpan.FromMilliseconds(40),
            Duration       = InDuration,
            EasingFunction = ease,
        });

        tr?.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From           = fromY,
            To             = 0,
            BeginTime      = TimeSpan.FromMilliseconds(40),
            Duration       = InDuration,
            EasingFunction = ease,
        });
    }

    // ── One-shot "content changed" nudge (e.g. after a filter tab) ───────────
    public static void Pop(FrameworkElement el, double fromOpacity = 0.25, double rise = 8)
    {
        if (el.RenderTransform is not TranslateTransform tr)
        {
            tr = new TranslateTransform();
            el.RenderTransform = tr;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur  = new Duration(TimeSpan.FromMilliseconds(260));

        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(fromOpacity, 1, dur) { EasingFunction = ease });
        tr.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(rise, 0, dur) { EasingFunction = ease });
    }
}
