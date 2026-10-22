using Catussh.Core.Machine;
using Catussh.Core.Store;
using Catussh.Core.Utils;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.Versioning;

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

        // Data comes from ApiServer (loopback HTTP), not from request interception:
        // virtual-host-mapped requests never reach the network stack, so
        // WebResourceRequested never fires for this app (verified with a "*" +
        // Document filter: zero events, even for the document itself). The folder
        // mapping only serves the static design files.
        _api = new ApiServer(_store, _knownHosts);
        _api.Start();

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

        // Query carries both the appearance and the bridge port; the page reads
        // them from location.search (the hash form #page?theme= stays supported
        // for static screenshots without a host).
        var query = _startDark ? $"?theme=dark&api={_api.Port}" : $"?api={_api.Port}";
        _webView.CoreWebView2.Navigate($"https://catussh.local/index.html{query}#{_startPage}");

        if (_screenshotPath is not null)
        {
            _webView.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                try
                {
                    // The monitor's first fetch lands within this window; the poller
                    // keeps refreshing once a second after that.
                    await Task.Delay(1500);
                    await CaptureOnceAsync(_screenshotPath);

                    // Long pages (the monitor grid scrolls) get a second frame at
                    // the bottom so verification is not limited to above the fold.
                    if (_startPage == "monitor")
                    {
                        await _webView.CoreWebView2.ExecuteScriptAsync(
                            "document.querySelector('.rx-content').scrollTo(0, 999999)");
                        await Task.Delay(600);
                        await CaptureOnceAsync(WithSuffix(_screenshotPath, "-below"));
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
            };
        }
    }

    /// <summary>
    /// Headless-ish run used to verify the shell end to end: renders the window
    /// into a PNG and exits. Invoked with <c>--screenshot &lt;path&gt;</c>.
    /// </summary>
    private async Task CaptureOnceAsync(string path)
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

    private static string WithSuffix(string path, string suffix) =>
        Path.Combine(
            Path.GetDirectoryName(path) ?? "",
            Path.GetFileNameWithoutExtension(path) + suffix + Path.GetExtension(path));

    /// <summary>Saved servers, identities and live monitor sessions.</summary>
    private readonly CatusshStore _store = new();
    private readonly KnownHosts _knownHosts = KnownHosts.Shared;
    private ApiServer? _api;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _api?.Dispose();
        _api = null;
        base.OnFormClosed(e);
    }

    /// <summary>
    /// Runs the ported monitor script's parsers against captured remote output,
    /// exactly as <see cref="ServerStatus.Apply"/> does after an SSH round-trip.
    /// </summary>
    internal static ServerStatus DemoSnapshot()
    {
        const string merged = """
            <<<CATUSSH-SECTION:platform>>>
            Linux
            <<<CATUSSH-SECTION:cpu-a>>>
            cpu  100 0 200 1600 50 0 10 0 0 0
            cpu0 50 0 100 800 25 0 5 0 0 0
            cpu1 50 0 100 800 25 0 5 0 0 0
            <<<CATUSSH-SECTION:net-a>>>
            Inter-|   Receive                                                |  Transmit
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
                lo: 100000   1000    0    0    0     0          0         0   100000    1000    0    0    0     0       0          0
              eth0: 5000000   5000    0    0    0     0          0         0  1200000    2000    0    0    0     0       0          0
              eth1:  200000    200    0    0    0     0          0         0    80000     100    0    0    0     0       0          0
            <<<CATUSSH-SECTION:cpu-b>>>
            cpu  160 0 260 1780 60 0 16 0 0 0
            cpu0 80 0 130 890 30 0 8 0 0 0
            cpu1 80 0 130 890 30 0 8 0 0 0
            <<<CATUSSH-SECTION:net-b>>>
            Inter-|   Receive                                                |  Transmit
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
                lo: 101000   1010    0    0    0     0          0         0   101000    1010    0    0    0     0       0          0
              eth0: 7420800   5100    0    0    0     0          0         0  1400000    2100    0    0    0     0       0          0
              eth1:  262080    210    0    0    0     0          0         0   148080     110    0    0    0     0       0          0
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

        // A server with a GPU, so the Graphics card has something to draw. This is
        // the shape nvidia-smi -q -x emits; the parser reads it, not this literal.
        const string nvidiaSmi = """
            <nvidia_smi_log>
            <cuda_version>12.4</cuda_version>
            <gpu id="00000000:01:00.0">
              <product_name>NVIDIA GeForce RTX 4090</product_name>
              <uuid>GPU-8c1f2e4a-77bd-4b1e-9d2a-1f0e5c3b9a71</uuid>
              <fan_speed>38 %</fan_speed>
              <fb_memory_usage>
                <total>24564 MiB</total><used>3182 MiB</used><free>21382 MiB</free>
              </fb_memory_usage>
              <utilization>
                <gpu_util>12 %</gpu_util><memory_util>13 %</memory_util>
                <encoder_util>0 %</encoder_util><decoder_util>3 %</decoder_util>
              </utilization>
              <vbios_version>95.02.18.80.87</vbios_version>
            </gpu>
            </nvidia_smi_log>
            """;

        return new ServerStatus().Apply(merged, graphics: nvidiaSmi, includeStatic: true);
    }
}
