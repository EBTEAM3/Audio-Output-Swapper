using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AudioSwapper.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AudioSwapper.Ui;

/// <summary>
/// A borderless WPF window whose entire content is one WebView2 page.
///
/// Why the window is opaque rather than per-pixel transparent: WebView2 hosts a
/// child HWND, and a WPF window with AllowsTransparency="True" is a layered
/// window, which does not composite child HWNDs at all -- the WebView2 simply
/// does not draw. So the rounding comes from DWM instead, and the larger 22-30px
/// radii live on the panels *inside* the page, floating over the ambient
/// background that fills the window. The look never depended on the window
/// itself being see-through, so nothing is lost.
/// </summary>
internal abstract class GlassWindow : Window
{
    private readonly string _resourceName;
    private WebView2 _webView;
    private TaskCompletionSource<bool>? _ready;

    protected WebView2 WebView => _webView;

    /// <summary>Set once the page has posted its "ready" message.</summary>
    protected bool PageReady { get; private set; }

    protected GlassWindow(string resourceName)
    {
        _resourceName = resourceName;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = false;
        // Matches --bg-0 so the single frame before the page paints is the
        // right colour rather than a white flash.
        Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1C, 0x24));
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        _webView = new WebView2();
        Content = _webView;

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        NativeWindowHelpers.ApplyRoundedCorners(handle);
        ConfigureNativeWindow(handle);
    }

    /// <summary>Hook for subclasses to add their own extended window styles.</summary>
    protected abstract void ConfigureNativeWindow(IntPtr handle);

    /// <summary>Called once the page has signalled that it is ready for state.</summary>
    protected abstract void OnPageReady();

    /// <summary>Called for every message the page posts, except "ready".</summary>
    protected abstract void OnPageMessage(JsonElement message);

    /// <summary>
    /// Boots the WebView2 and loads the page. Safe to await more than once --
    /// later calls return the first initialisation's result.
    /// </summary>
    protected async Task InitialiseAsync()
    {
        if (_ready is not null)
        {
            await _ready.Task.ConfigureAwait(true);
            return;
        }

        _ready = new TaskCompletionSource<bool>();

        try
        {
            // A fixed user-data folder under %LOCALAPPDATA%; without it WebView2
            // tries to write beside the executable, which fails when the app
            // lives in Program Files or a synced OneDrive folder.
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AudioSwapper", "WebView2");
            Directory.CreateDirectory(userData);

            var options = new CoreWebView2EnvironmentOptions
            {
                // The pages are local and self-contained; no network, no
                // background sync, no first-run experience.
                AdditionalBrowserArguments = "--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection",
            };

            var environment = await CoreWebView2Environment
                .CreateAsync(browserExecutableFolder: null, userData, options)
                .ConfigureAwait(true);

            await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

            var core = _webView.CoreWebView2;
            core.WebMessageReceived += OnWebMessageReceived;

            var settings = core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsSwipeNavigationEnabled = false;

            _webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xFF, 0x17, 0x1C, 0x24);

            core.NavigateToString(LoadPage());

            await _ready.Task.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            throw;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement message;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            message = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        if (!message.TryGetProperty("type", out var typeProperty)) return;
        string? type = typeProperty.GetString();

        if (type == "ready")
        {
            PageReady = true;
            _ready?.TrySetResult(true);
            OnPageReady();
            return;
        }

        OnPageMessage(message);
    }

    /// <summary>
    /// Tears the WebView2 down completely, ending its browser processes.
    ///
    /// Suspending would be gentler, but it leaves the whole Chromium process
    /// group resident -- around half a gigabyte for a settings panel that is
    /// open for a few seconds at a time. Disposing gives that back entirely and
    /// costs a few hundred milliseconds the next time the panel opens, which is
    /// the right trade for something opened this rarely.
    /// </summary>
    protected void ReleaseWebView()
    {
        if (_ready is null) return;

        var core = _webView.CoreWebView2;
        if (core is not null) core.WebMessageReceived -= OnWebMessageReceived;

        _webView.Dispose();

        PageReady = false;
        _ready = null;

        // Recreate the control so the window still has content to host next
        // time; the disposed one cannot be reinitialised.
        _webView = new WebView2();
        Content = _webView;
    }

    /// <summary>Sends an object to the page as a JSON web message.</summary>
    protected void PostToPage(object payload)
    {
        if (!PageReady || _webView.CoreWebView2 is null) return;

        try
        {
            _webView.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(payload, JsonPayload.Options));
        }
        catch (Exception)
        {
            // The WebView can be tearing down during shutdown; a dropped state
            // update at that point is not worth surfacing.
        }
    }

    /// <summary>
    /// Reads the embedded page and splices glass.css into its placeholder. The
    /// stylesheet is kept in its own file so it stays editable and diffable
    /// rather than being pasted into both pages.
    /// </summary>
    private string LoadPage()
    {
        string html = ReadResource(_resourceName);
        string css = ReadResource("glass.css");
        return html.Replace("/*{GLASS_CSS}*/", css, StringComparison.Ordinal);
    }

    private static string ReadResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string suffix = "Web." + fileName;

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) break;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        throw new InvalidOperationException($"Embedded resource '{fileName}' is missing from the build.");
    }
}

internal static class JsonPayload
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
