// language: C#, file: SplashWindow.xaml.cs
// Intro splash — animated logo in, holds briefly, then hands off to MainWindow.
// Never blocks startup for more than ~1.8s even if something stalls.

using System.Windows;
using System.Windows.Media.Animation;

namespace SiteGrabberV3;

public partial class SplashWindow : Window
{
    private readonly string[] _statusSteps =
    [
        "Loading…", "Initializing engine…", "Warming up crawler…", "Ready",
    ];

    public SplashWindow()
    {
        InitializeComponent();
        Opacity = 0;
        Loaded += (_, _) => RunIntro();
    }

    // Single shared easing curve across every animation in this window — mixing
    // easings (bounce here, linear there) is what reads as "not smooth". One
    // gentle cubic ease-out, used everywhere, keeps the whole sequence coherent.
    private static CubicEase Ease() => new() { EasingMode = EasingMode.EaseOut };

    private async void RunIntro()
    {
        // Window fade-in
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));

        // Glow pulse — continuous, slow breathing effect
        var pulse = new DoubleAnimation
        {
            From = 1.0, To = 1.1, Duration = TimeSpan.FromSeconds(1.8),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        GlowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
        GlowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);

        // Logo: fade + scale up + un-rotate, all on the same curve/duration so
        // they land in sync instead of drifting apart mid-motion
        var logoDur = TimeSpan.FromMilliseconds(750);
        Logo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, logoDur) { EasingFunction = Ease() });
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.75, 1.0, logoDur) { EasingFunction = Ease() });
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.75, 1.0, logoDur) { EasingFunction = Ease() });
        LogoRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(-12, 0, logoDur) { EasingFunction = Ease() });

        await Task.Delay(320);

        // Title + subtitle slide up and fade in
        var titleDur = TimeSpan.FromMilliseconds(450);
        TitleText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, titleDur) { EasingFunction = Ease() });
        TitleOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, titleDur) { EasingFunction = Ease() });

        await Task.Delay(150);
        SubText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, titleDur) { EasingFunction = Ease() });

        // Progress bar fill, synced with status text steps
        ProgressBar.BeginAnimation(WidthProperty,
            new DoubleAnimation(0, 160, TimeSpan.FromMilliseconds(1200)) { EasingFunction = Ease() });

        foreach (var step in _statusSteps)
        {
            StatusText.Text = step;
            await Task.Delay(300);
        }

        await Task.Delay(180);
        Launch();
    }

    private void Launch()
    {
        var main = new MainWindow();
        Application.Current.MainWindow = main;
        main.Opacity = 0;
        main.Show();
        main.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = Ease() });

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = Ease() };
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
