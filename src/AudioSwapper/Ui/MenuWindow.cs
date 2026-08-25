using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioSwapper.Interop;

namespace AudioSwapper.Ui;

/// <summary>
/// The settings and device-picker panel, opened from the tray.
///
/// Behaves like a shell flyout rather than an app window: anchored above the
/// tray, out of Alt+Tab, and dismissed as soon as it loses focus or Escape is
/// pressed. Like the toast, it is created once and reused so reopening is
/// instant.
/// </summary>
internal sealed class MenuWindow : GlassWindow
{
    private const double DipWidth = 440;
    private const double DipHeight = 620;
    private const double DipMargin = 12;

    /// <summary>
    /// How long the panel stays warm after closing. Long enough that closing
    /// and reopening feels instant, short enough that leaving it alone gives
    /// the memory back.
    /// </summary>
    private static readonly TimeSpan IdleTeardown = TimeSpan.FromSeconds(30);

    private readonly Action<JsonElement> _onMessage;
    private readonly Func<object> _buildState;
    private readonly DispatcherTimer _teardownTimer;
    private bool _booted;

    public MenuWindow(Func<object> buildState, Action<JsonElement> onMessage) : base("menu.html")
    {
        _buildState = buildState;
        _onMessage = onMessage;

        Width = DipWidth;
        Height = DipHeight;
        Topmost = true;
        ShowActivated = true;

        _teardownTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = IdleTeardown,
        };
        _teardownTimer.Tick += (_, _) =>
        {
            _teardownTimer.Stop();
            if (IsVisible) return;

            ReleaseWebView();
            _booted = false;
            MemoryTrim.TrimWorkingSet();
        };

        Deactivated += (_, _) => HideMenu();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HideMenu();
    }

    protected override void ConfigureNativeWindow(IntPtr handle)
    {
        // Out of Alt+Tab, but still focusable -- the hotkey field needs to
        // receive key events.
        NativeWindowHelpers.MakeToolWindow(handle);
    }

    protected override void OnPageReady() => PushState();

    protected override void OnPageMessage(JsonElement message)
    {
        if (message.TryGetProperty("type", out var type) && type.GetString() == "close")
        {
            HideMenu();
            return;
        }

        _onMessage(message);
    }

    /// <summary>Re-sends the whole state to the page. Cheap; the page re-renders wholesale.</summary>
    public void PushState()
    {
        if (!PageReady) return;
        PostToPage(_buildState());
    }

    public async Task ToggleAsync()
    {
        if (IsVisible)
        {
            HideMenu();
            return;
        }

        _teardownTimer.Stop();
        Show();

        if (!_booted)
        {
            // Same reasoning as the toast: WebView2 will not attach until the
            // window is shown, so boot it off-screen rather than showing an
            // empty panel for the few hundred milliseconds it takes.
            ScreenPlacement.MoveOffScreen(
                new WindowInteropHelper(this).Handle, DipWidth, DipHeight);

            await InitialiseAsync().ConfigureAwait(true);
            _booted = true;
        }

        PushState();
        Reposition();
        Activate();
    }

    private void Reposition()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        ScreenPlacement.AnchorBottomRight(handle, DipWidth, DipHeight, DipMargin, topmost: true);
    }

    private void HideMenu()
    {
        if (IsVisible) Hide();

        // Start the clock on giving the browser processes back.
        _teardownTimer.Stop();
        _teardownTimer.Start();
    }

    public void Shutdown()
    {
        _teardownTimer.Stop();
        Close();
    }
}
