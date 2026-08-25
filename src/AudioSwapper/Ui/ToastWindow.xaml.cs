using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AudioSwapper.Interop;

namespace AudioSwapper.Ui;

/// <summary>
/// The bottom-right confirmation popup, drawn natively.
///
/// Kept alive between switches and simply re-shown, so a switch costs nothing
/// but a couple of animations. Being native rather than WebView2-hosted means
/// idle cost really is nothing: no browser processes, and the window itself is
/// a handful of WPF elements.
/// </summary>
public partial class ToastWindow : Window
{
    private const double DipWidth = 392;
    private const double DipMarginX = 14;

    /// <summary>
    /// The design width of the panel inside the window, i.e. excluding the
    /// margin that exists only to give the drop shadow room to fall.
    /// </summary>
    private const double ShadowMargin = 22;

    private readonly DispatcherTimer _dismissTimer;
    private readonly DispatcherTimer _failsafe;
    private double _bottomOffset = 94;
    private bool _closing;

    public ToastWindow()
    {
        InitializeComponent();

        _dismissTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _dismissTimer.Tick += (_, _) => BeginExit();

        // A backstop in case an animation completion is ever missed; the toast
        // must never be left sitting on screen.
        _failsafe = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        _failsafe.Tick += (_, _) => HideNow();

        SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;

            // A switch can happen mid-game or mid-call, so the toast must never
            // take the foreground.
            NativeWindowHelpers.MakeNonActivatingToolWindow(handle);
        };
    }

    /// <summary>Extra gap above the bottom of the work area, in device-independent pixels.</summary>
    public double BottomOffset
    {
        get => _bottomOffset;
        set => _bottomOffset = Math.Max(0, value);
    }

    /// <summary>Shows the toast, replacing whatever it was showing before.</summary>
    public void ShowFor(ToastContent content)
    {
        if (_closing) return;

        _dismissTimer.Stop();
        _failsafe.Stop();

        ApplyTheme(content.Theme, content.IsError);
        ApplyContent(content);

        if (!IsVisible) Show();
        Reposition();

        PlayEntrance();
        PlayCountdown(content.DurationMs);

        _dismissTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(800, content.DurationMs));
        _dismissTimer.Start();

        _failsafe.Interval = TimeSpan.FromMilliseconds(content.DurationMs + 5000);
        _failsafe.Start();
    }

    // ---- Content ----------------------------------------------------------

    private void ApplyContent(ToastContent content)
    {
        Kicker.Text = content.Kicker.ToUpperInvariant();
        DeviceName.Text = content.DeviceName;

        DeviceSub.Text = content.DeviceSub;
        DeviceSub.Visibility = string.IsNullOrWhiteSpace(content.DeviceSub)
            ? Visibility.Collapsed
            : Visibility.Visible;

        CommsChip.Visibility = content.Communications ? Visibility.Visible : Visibility.Collapsed;

        IconPath.Data = BuildIconGeometry(content.IconPaths);
    }

    /// <summary>
    /// Combines the icon's stroke paths into one geometry. WPF's path
    /// mini-language is a superset of the SVG syntax these are written in, so
    /// the same strings drive the tray glyph, the web menu and this.
    /// </summary>
    private static Geometry BuildIconGeometry(string[] paths)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };

        foreach (string data in paths)
        {
            try
            {
                group.Children.Add(Geometry.Parse(data));
            }
            catch (FormatException)
            {
                // One malformed path costs a stroke, not the whole toast.
            }
        }

        group.Freeze();
        return group;
    }

    // ---- Theme ------------------------------------------------------------

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(string from, string to, double angle = 135)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(from),
            (Color)ColorConverter.ConvertFromString(to),
            angle);
        brush.Freeze();
        return brush;
    }

    private void ApplyTheme(string theme, bool isError)
    {
        bool light = theme == "light";

        // Fully opaque. With no ambient layer behind it there is nothing for a
        // translucent panel to refract, and even 2% let high-contrast content
        // ghost through over the device name -- the one thing this window exists
        // to make readable. Depth comes from the shadow and the edge instead.
        Panel.Background = light ? Brush("#FFF4F7FB") : Brush("#FF1A2029");

        // glass-border is light in BOTH themes. On a solid light panel that
        // alone is invisible, so light mode also gets a faint dark hairline.
        Panel.BorderBrush = light ? Brush("#26182640") : Brush("#2BFFFFFF");
        TopHighlight.BorderBrush = light ? Brush("#FFFFFFFF") : Brush("#42FFFFFF");

        PanelShadow.Color = light
            ? (Color)ColorConverter.ConvertFromString("#233454")
            : Colors.Black;
        PanelShadow.Opacity = light ? 0.26 : 0.55;

        Kicker.Foreground = light ? Brush("#8A95A5") : Brush("#7B8593");
        DeviceName.Foreground = light ? Brush("#141A22") : Brush("#F1F4F8");
        DeviceSub.Foreground = light ? Brush("#8A95A5") : Brush("#7B8593");

        CountdownTrack.Background = light ? Brush("#1A182640") : Brush("#14FFFFFF");

        if (isError)
        {
            // Semantic danger, visibly distinct from the Azure accent, so a
            // failure never reads as a success.
            Badge.Background = Gradient("#FF5B52", "#FF8A5C");
            IconPath.Stroke = Brush("#2A0806");
            CountdownFill.Fill = Gradient("#FF5B52", "#FF8A5C", 0);
            Panel.BorderBrush = Brush("#73FF5B52");
        }
        else
        {
            Badge.Background = Gradient("#4D9BFF", "#00D4FF");
            IconPath.Stroke = Brush("#04142E");
            CountdownFill.Fill = Gradient("#2F7BFF", "#00D4FF", 0);
        }

        // the chip formula -- fill ~0.16, border ~0.22, text the solid
        // colour. Light mode darkens the text, since a 0.16 fill is much weaker
        // against white.
        CommsChip.Background = Brush("#294D9BFF");
        CommsChip.BorderBrush = Brush("#384D9BFF");
        CommsChipText.Foreground = light ? Brush("#1B5FD0") : Brush("#4D9BFF");
    }

    // ---- Placement --------------------------------------------------------

    private void Reposition()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        // Force the pending layout pass so the window has already grown to fit
        // the new text; otherwise the first frame is positioned against the
        // previous message's height and visibly jumps.
        UpdateLayout();

        // The shadow margin is part of the window but not part of the visible
        // panel, so it is subtracted here -- otherwise the panel would sit that
        // much further from the screen edge than asked for.
        ScreenPlacement.AnchorBottomRightKeepSize(
            handle,
            DipMarginX - ShadowMargin,
            _bottomOffset - ShadowMargin);
    }

    // ---- Animation --------------------------------------------------------

    private void PlayEntrance()
    {
        // entrances overshoot. BackEase(EaseOut) is WPF's equivalent of
        // the ease-bounce cubic-bezier; KeySpline cannot express it because its
        // control points are clamped to 0-1 and that curve's Y goes to 1.56.
        var overshoot = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
        var settle = new CubicEase { EasingMode = EasingMode.EaseOut };

        var duration = TimeSpan.FromMilliseconds(420);

        PanelSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(30, 0, duration) { EasingFunction = overshoot });

        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, duration) { EasingFunction = overshoot });
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, duration) { EasingFunction = overshoot });

        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = settle });
    }

    private void PlayCountdown(int durationMs)
    {
        // Linear: this is a clock, not a user-triggered transition.
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(Math.Max(800, durationMs))));
    }

    private void BeginExit()
    {
        _dismissTimer.Stop();

        // No overshoot on the way out -- a toast leaving should not draw
        // attention back to itself.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(280);

        PanelSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, 26, duration) { EasingFunction = ease });

        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) => HideNow();
        BeginAnimation(OpacityProperty, fade);
    }

    private void HideNow()
    {
        _dismissTimer.Stop();
        _failsafe.Stop();

        // Detach the animations before hiding, or the held Opacity value stops
        // a later Show() from ever becoming visible.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        if (IsVisible) Hide();

        // The toast going away is this app's natural idle point: the next thing
        // that happens is usually nothing at all, for hours. Hand back what the
        // switch and the render just allocated.
        MemoryTrim.TrimWorkingSet();
    }

    private void OnPanelClicked(object sender, MouseButtonEventArgs e) => BeginExit();

    /// <summary>Closes for real, at application shutdown.</summary>
    public void Shutdown()
    {
        _closing = true;
        _dismissTimer.Stop();
        _failsafe.Stop();
        Close();
    }
}

/// <summary>
/// Public because the XAML compiler emits ToastWindow as a public partial
/// class, and a public method cannot take an internal parameter.
/// </summary>
public sealed record ToastContent
{
    public required string DeviceName { get; init; }
    public string DeviceSub { get; init; } = "";
    public string Kicker { get; init; } = "Switched to";
    public required string[] IconPaths { get; init; }
    public required string Theme { get; init; }
    public bool Communications { get; init; }

    /// <summary>Recolours the toast to the danger palette.</summary>
    public bool IsError { get; init; }

    public int DurationMs { get; init; } = 2600;
}
