using Catussh.Core.Machine;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Catussh.App;

/// <summary>
/// The catussh window.
///
/// The original is a SwiftUI window; here the chrome is a WebView2 showing the
/// ported design system (src/Catussh.Design) while the host process is .NET. The
/// bridge below is the only coupling: the page asks for a machine snapshot and
/// the .NET side answers with the ported <see cref="ServerStatus"/> model, so
/// the monitor page renders real parser output rather than demo data.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MainWindow : Form
{
    private readonly WebView2 _webView = new();
    private readonly bool _startDark;
    private readonly string _startPage;
    private readonly string? _screenshotPath;

    public MainWindow(bool startDark, string startPage, string? screenshotPath = null)
    {
        _startDark = startDark;
        _startPage = startPage;
        _screenshotPath = screenshotPath;

        Text = "catussh";
        MinimumSize = new Size(1024, 700);
        // 1440 wide keeps every server-table column visible; the CSS hides
        // trailing columns below the widths ServerColumns.swift uses.
        Size = new Size(1440, 880);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = startDark ? Color.FromArgb(0x1B, 0x1C, 0x1F) : Color.FromArgb(0xEE, 0xF0, 0xF5);

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        Load += OnLoad;
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        // A persistent WebView2 profile caches tokens.css/components.css on disk;
        // the design is edited far more often than it is shipped, so each launch
        // starts from an empty profile directory instead of stale stylesheets.
        var userData = Path.Combine(Path.GetTempPath(), "catussh-webview2");
        if (Directory.Exists(userData))
        {
            try { Directory.Delete(userData, recursive: true); }
            catch (IOException) { /* a previous instance still holds it; reuse */ }
        }
        var environment = await CoreWebView2Environment.CreateAsync(null, userData);
        await _webView.EnsureCoreWebView2Async(environment);

        var settings = _webView.CoreWebView2.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreDevToolsEnabled = true;
        settings.IsZoomControlEnabled = false;

        // The design files sit next to the executable under Design/.
        var designRoot = Path.Combine(AppContext.BaseDirectory, "Design");
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "catussh.local", designRoot, CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.AddWebResourceRequestedFilter(
            "https://catussh.local/monitor-snapshot*", CoreWebView2WebResourceContext.All);
        _webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

        // Navigation must not reuse a cached copy of the design files: iterating on
        // the CSS and relaunching would otherwise show the previous stylesheet.
        // WebView2 caches subresources (tokens.css, components.css) on disk, and a
        // query string on the document URL does not bust them.
        if (_webView.CoreWebView2.Profile is { } profile)
        {
            profile.PreferredColorScheme = _startDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
        }
        _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

        // Serve the design from a scratch profile and drop what is cached, so a
        // rebuilt stylesheet is what the window actually shows.
        await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
            CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage);

        var theme = _startDark ? "?theme=dark" : "";
        _webView.CoreWebView2.Navigate($"https://catussh.local/index.html{theme}#{_startPage}");

        if (_screenshotPath is not null)
        {
            _webView.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                // Let the first layout and the CSS arrival animation settle.
                await Task.Delay(600);
                await CaptureAndCloseAsync(_screenshotPath);
            };
        }
    }

    /// <summary>
    /// Headless-ish run used to verify the shell end to end: renders the window
    /// into a PNG and exits. Invoked with <c>--screenshot &lt;path&gt;</c>.
    /// </summary>
    private async Task CaptureAndCloseAsync(string path)
    {
        try
        {
            await using var stream = File.Create(path);
            await _webView.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png, stream);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                // CapturePreviewAsync can hand back an empty frame before first paint;
                // fall back to a form capture so the run still proves a window rendered.
                using var bitmap = new Bitmap(_webView.Width, _webView.Height);
                _webView.DrawToBitmap(bitmap, new Rectangle(0, 0, _webView.Width, _webView.Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[catussh] screenshot failed: {ex.Message}");
        }
        finally
        {
            Close();
        }
    }

    /// <summary>
    /// Minimal bridge: <c>GET https://catussh.local/monitor-snapshot</c> answers with a
    /// ServerStatus snapshot produced by the ported parsers, so the UI can show
    /// monitor data without a live SSH channel.
    /// </summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var json = JsonSerializer.Serialize(DemoSnapshot(), new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        e.Response = _webView.CoreWebView2!.Environment.CreateWebResourceResponse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)),
            200,
            "OK",
            "Content-Type: application/json");
    }

    /// <summary>
    /// Runs the ported monitor script's parsers against captured remote output,
    /// exactly as <see cref="ServerStatus.Apply"/> does after an SSH round-trip.
    /// </summary>
    private static ServerStatus DemoSnapshot()
    {
        const string merged = """
            <<<CATUSSH-SECTION:platform>>>
            Linux
            <<<CATUSSH-SECTION:cpu-a>>>
            cpu  100 0 200 1600 50 0 10 0 0 0
            cpu0 50 0 100 800 25 0 5 0 0 0
            cpu1 50 0 100 800 25 0 5 0 0 0
            <<<CATUSSH-SECTION:cpu-b>>>
            cpu  160 0 260 1780 60 0 16 0 0 0
            cpu0 80 0 130 890 30 0 8 0 0 0
            cpu1 80 0 130 890 30 0 8 0 0 0
            <<<CATUSSH-SECTION:mem>>>
            MemTotal:       16384000 kB
            MemFree:         1024000 kB
            Buffers:          512000 kB
            Cached:          4096000 kB
            SwapTotal:       2097152 kB
            SwapFree:        1048576 kB
            <<<CATUSSH-SECTION:df>>>
            Filesystem                        1024-blocks     Used Available Capacity Mounted on
            /dev/mapper/ubuntu--vg-ubuntu--lv   51281224 15994688  32923552      33% /
            /dev/sda2                            1959920   251792   1606088      14% /boot
            <<<CATUSSH-SECTION:uptime>>>
            773229.50 1531748.20
            <<<CATUSSH-SECTION:loadavg>>>
            0.27 0.34 0.39 2/916 23817
            <<<CATUSSH-SECTION:hostname>>>
            web-01
            <<<CATUSSH-SECTION:os-release>>>
            NAME="Ubuntu"
            PRETTY_NAME="Ubuntu 24.04 LTS"
            """;

        return new ServerStatus().Apply(merged, graphics: "", includeStatic: true);
    }
}
