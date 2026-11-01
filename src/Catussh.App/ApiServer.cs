using Catussh.Core.Machine;
using Catussh.Core.RemoteDesktop;
using Catussh.Core.Ssh;
using Catussh.Core.Store;
using Catussh.Core.Utils;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using QRCoder;
using System.Windows.Forms;

namespace Catussh.App;

/// <summary>
/// The HTTP surface the UI talks to, on 127.0.0.1.
///
/// The first Monitor page only needed one endpoint serving a fixture. Now the page
/// is a real client: it lists saved servers, adds and removes them, starts a monitor
/// session and polls its live readings. Keeping this on loopback means the UI can be
/// driven (and tested) with curl, without WebView2 in the way.
/// </summary>
internal sealed class ApiServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CatusshStore _store;
    private readonly KnownHosts _knownHosts;
    private readonly ConcurrentDictionary<string, MonitorSession> _sessions = new();
    private readonly ConcurrentDictionary<string, VncSession> _vncSessions = new();
    private readonly ConcurrentDictionary<string, InteractiveSshSession> _terminalSessions = new();
    private readonly ConcurrentDictionary<string, UURemoteBrowserSession> _uuDesktopSessions = new();
    private readonly VncDiscoveryService _vncDiscovery = new();
    private readonly UURemoteAccountStore _uuAccount;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ApiServer(CatusshStore store, KnownHosts knownHosts)
    {
        _store = store;
        _knownHosts = knownHosts;
        var bounds = Screen.PrimaryScreen?.Bounds;
        var screen = bounds is { Width: > 0, Height: > 0 } size ? $"{size.Width}x{size.Height}" : "";
        _uuAccount = new UURemoteAccountStore(store, UUDeviceRegistrationProfile.FromCurrentMachine(screen));
    }

    public int Port { get; private set; }

    public void Start()
    {
        Port = FindFreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => ServeAsync(token), token);
        _ = _uuAccount.InitializeAsync();
        _ = StartVncDiscoveryAsync();
        Console.Error.WriteLine($"[catussh] api on http://127.0.0.1:{Port}/");
    }

    private async Task StartVncDiscoveryAsync()
    {
        try { await _vncDiscovery.StartAsync(_cts?.Token ?? CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { Console.Error.WriteLine($"[catussh] VNC discovery unavailable: {ex.GetType().Name}"); }
    }

    private async Task ServeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }

            _ = Task.Run(() =>
            {
                try { Handle(context); }
                catch (UUApiException ex)
                {
                    var status = ex.TokenExpired ? 401 : ex.HttpStatus == 422 ? 400 : 502;
                    TryWrite(context, status, new { error = ex.Message, code = ex.Code, tokenExpired = ex.TokenExpired });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[catussh] api error: {ex.GetType().Name}");
                    TryWrite(context, 500, new { error = ex.Message });
                }
            }, token);
        }
    }

    // ------------------------------------------------------------- routes

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var path = (request.Url?.AbsolutePath ?? "/").TrimEnd('/');
        if (path.Length == 0) path = "/";
        var method = request.HttpMethod.ToUpperInvariant();

        // Loopback only, but the page is on https://catussh.local, so cross-origin
        // has to be allowed explicitly. Nothing here is reachable off this machine.
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
        context.Response.Headers.Add("Access-Control-Allow-Headers", "content-type");
        context.Response.Headers.Add("Access-Control-Expose-Headers", "Content-Disposition");
        context.Response.Headers.Add("Cache-Control", "no-store");

        if (method == "OPTIONS")
        {
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }

        switch (path, method)
        {
            case ("/health", "GET"):
                TryWrite(context, 200, new { ok = true, store = _store.Path_ });
                return;

            case ("/folders", "GET"):
                TryWrite(context, 200, _store.Folders);
                return;

            case ("/folders", "POST"):
                AddFolder(context);
                return;

            case ("/machines", "GET"):
                TryWrite(context, 200, MachineList());
                return;

            case ("/machines", "POST"):
                AddMachine(context);
                return;

            case ("/identities", "GET"):
                TryWrite(context, 200, _store.Identities.Select(i => new
                {
                    id = i.Id,
                    username = i.Username,
                    keyType = i.KeyType,
                    comment = i.Comment,
                    group = i.Group,
                    automatic = i.AuthenticAutomatically,
                    hasSecret = !string.IsNullOrEmpty(i.Password) || !string.IsNullOrEmpty(i.PrivateKey),
                }));
                return;

            case ("/identities", "POST"):
                AddIdentity(context);
                return;


            case ("/uu/account", "GET"):
                TryWrite(context, 200, _uuAccount.Snapshot());
                return;

            case ("/uu/account/qr", "POST"):
                StartUUQrLogin(context);
                return;

            case ("/uu/account/qr/status", "POST"):
                PollUUQrLogin(context);
                return;

            case ("/uu/account/sms/code", "POST"):
                SendUUSmsCode(context);
                return;

            case ("/uu/account/sms/login", "POST"):
                LoginUUBySms(context);
                return;

            case ("/uu/account/token", "POST"):
                ImportUUToken(context);
                return;

            case ("/uu/account/devices/refresh", "POST"):
                RefreshUUDevices(context);
                return;

            case ("/uu/account/logout", "POST"):
                LogoutUUAccount(context);
                return;
            case ("/vnc/discovery", "GET"):
                TryWrite(context, 200, VncDiscoverySnapshot());
                return;

            case ("/vnc/discovery/refresh", "POST"):
                _vncDiscovery.RefreshAsync().GetAwaiter().GetResult();
                TryWrite(context, 200, VncDiscoverySnapshot());
                return;
            case ("/monitor", "GET"):
                TryWrite(context, 200, MonitorSnapshot());
                return;
        }

        // /machines/{id}, DELETE /machines/{id}, /monitor/{id}, POST /monitor/{id}
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (HandleTransportRoute(context, parts, method)) return;

        if (parts.Length == 2 && parts[0] == "folders" && (method == "POST" || method == "DELETE"))
        {
            var folderName = Uri.UnescapeDataString(parts[1]);
            if (method == "DELETE")
            {
                var removed = _store.RemoveFolder(folderName);
                TryWrite(context, removed ? 200 : 404, new { removed });
                return;
            }

            var body = ReadJson(context);
            var newName = body.TryGetProperty("newName", out var value) ? value.GetString() : null;
            if (!_store.RenameFolder(folderName, newName ?? ""))
            {
                TryWrite(context, 409, new { error = "The folder name is empty, missing, or already in use." });
                return;
            }
            TryWrite(context, 200, new { name = newName!.Trim() });
            return;
        }

        if (parts.Length == 3 && parts[0] == "machines" && parts[2] == "folder" && method == "POST")
        {
            var body = ReadJson(context);
            var folder = body.TryGetProperty("name", out var value) ? value.GetString() : "";
            var moved = _store.MoveMachineToFolder(Uri.UnescapeDataString(parts[1]), folder ?? "");
            if (!moved)
                TryWrite(context, 400, new { moved = false, error = "No such server, or folder does not exist." });
            else
                TryWrite(context, 200, new { moved = true });
            return;
        }

        if (parts.Length == 2 && parts[0] == "machines" && method == "DELETE")
        {
            var id = Uri.UnescapeDataString(parts[1]);
            if (_terminalSessions.TryRemove(id, out var terminal)) terminal.Dispose();
            if (_sessions.TryRemove(id, out var monitor)) monitor.Dispose();
            if (_vncSessions.TryRemove(id, out var desktop)) desktop.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (_uuDesktopSessions.TryRemove(id, out var uuDesktop)) uuDesktop.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var removed = _store.RemoveMachine(id);
            TryWrite(context, removed ? 200 : 404, new { removed });
            return;
        }

        if (parts.Length == 2 && parts[0] == "identities" && method == "DELETE")
        {
            var removed = _store.RemoveIdentity(parts[1]);
            TryWrite(context, removed ? 200 : 404, new { removed });
            return;
        }

        if (parts.Length == 2 && parts[0] == "monitor")
        {
            var machine = _store.FindMachine(parts[1]);
            if (machine is null)
            {
                TryWrite(context, 404, new { error = "No such server." });
                return;
            }

            if (method == "POST")
            {
                StartSession(machine);
                TryWrite(context, 200, new { started = machine.Id });
                return;
            }

            TryWrite(context, 200, SessionSnapshot(machine));
            return;
        }

        if (parts.Length == 2 && parts[0] == "desktop")
        {
            var machineId = Uri.UnescapeDataString(parts[1]);
            var machine = _store.FindMachine(machineId);
            if (machine is null)
            {
                TryWrite(context, 404, new { error = "No such server." });
                return;
            }

            if (method == "POST")
            {
                try
                {
                    var body = ReadJson(context);
                    var purpose = body.TryGetProperty("purpose", out var value) ? value.GetString() ?? "desktop" : "desktop";
                    if (purpose is not ("desktop" or "terminal"))
                    {
                        TryWrite(context, 400, new { error = "Desktop purpose must be desktop or terminal." });
                        return;
                    }
                    if (machine.RemoteDesktopProtocol == RDMachine.RemoteDesktopProtocolType.Vnc)
                    {
                        if (purpose != "desktop") { TryWrite(context, 409, new { error = "VNC sessions do not support remote terminal access." }); return; }
                        StartVncSession(machine);
                        TryWrite(context, 200, new { started = machine.Id, purpose });
                        return;
                    }
                    if (machine.RemoteDesktopProtocol == RDMachine.RemoteDesktopProtocolType.UuRemote)
                    {
                        var session = StartUURemoteSession(machine, purpose == "terminal");
                        TryWrite(context, 200, session.Snapshot(0));
                        return;
                    }
                    TryWrite(context, 409, new { error = "This server has no remote desktop configured." });
                }
                catch (Exception ex) { TryWrite(context, 502, new { error = ex.Message }); }
                return;
            }

            if (method == "DELETE")
            {
                if (_vncSessions.TryRemove(machineId, out var vnc)) vnc.DisposeAsync().AsTask().GetAwaiter().GetResult();
                if (_uuDesktopSessions.TryRemove(machineId, out var uu)) uu.DisposeAsync().AsTask().GetAwaiter().GetResult();
                TryWrite(context, 200, new { stopped = machine.Id });
                return;
            }

            if (method == "GET")
            {
                long.TryParse(context.Request.QueryString["after"], out var afterVersion);
                if (_uuDesktopSessions.TryGetValue(machineId, out var uu)) TryWrite(context, 200, uu.Snapshot(afterVersion));
                else TryWrite(context, 200, VncSnapshot(machineId, afterVersion));
                return;
            }
        }

        if (parts.Length == 3 && parts[0] == "desktop" && method == "POST")
        {
            var machineId = Uri.UnescapeDataString(parts[1]);
            if (parts[2] == "signal")
            {
                if (!_uuDesktopSessions.TryGetValue(machineId, out var uu))
                {
                    TryWrite(context, 409, new { error = "The UU Remote session is not open." });
                    return;
                }
                try
                {
                    uu.SendSOACAsync(ReadJson(context)).GetAwaiter().GetResult();
                    TryWrite(context, 200, new { sent = true });
                }
                catch (Exception ex) { TryWrite(context, 502, new { error = ex.Message }); }
                return;
            }

            if (!_vncSessions.TryGetValue(machineId, out var session))
            {
                TryWrite(context, 409, new { error = "The VNC desktop is not open." });
                return;
            }

            var body = ReadJson(context);
            switch (parts[2])
            {
                case "pointer":
                {
                    double x = body.TryGetProperty("x", out var xValue) ? xValue.GetDouble() : 0;
                    double y = body.TryGetProperty("y", out var yValue) ? yValue.GetDouble() : 0;
                    if (body.TryGetProperty("button", out var buttonValue) && buttonValue.TryGetInt32(out int button))
                    {
                        bool down = body.TryGetProperty("down", out var downValue) && downValue.ValueKind == JsonValueKind.True;
                        session.PointerButtonAsync(button, down, x, y).GetAwaiter().GetResult();
                    }
                    else session.PointerMoveAsync(x, y).GetAwaiter().GetResult();
                    TryWrite(context, 200, new { sent = true });
                    return;
                }
                case "key":
                {
                    uint keySym = body.TryGetProperty("keySym", out var keyValue) ? keyValue.GetUInt32() : 0;
                    bool down = body.TryGetProperty("down", out var downValue) && downValue.ValueKind == JsonValueKind.True;
                    session.SendKeyAsync(keySym, down).GetAwaiter().GetResult();
                    TryWrite(context, 200, new { sent = true });
                    return;
                }
            }
        }



        TryWrite(context, 404, new { error = $"No route for {method} {path}." });
    }

    private bool HandleTransportRoute(HttpListenerContext context, string[] parts, string method)
    {
        if (parts.Length == 2 && parts[0] == "terminal")
        {
            var id = Uri.UnescapeDataString(parts[1]);
            var machine = _store.FindMachine(id);
            if (machine is null) { TryWrite(context, 404, new { error = "No such server." }); return true; }

            if (method == "POST")
            {
                try
                {
                    if (machine.TransportMode == RDMachine.Transport.UURemote)
                    {
                        var uu = StartUURemoteSession(machine, terminalOnly: true);
                        TryWrite(context, 200, uu.Snapshot(0));
                    }
                    else
                    {
                        var session = GetOrStartTerminalSession(machine);
                        TryWrite(context, 200, new { started = id, banner = session.Banner, transport = "ssh" });
                    }
                }
                catch (Exception ex) { TryWrite(context, 502, new { error = ex.Message }); }
                return true;
            }
            if (method == "DELETE")
            {
                if (_terminalSessions.TryRemove(id, out var session)) session.Dispose();
                if (_uuDesktopSessions.TryGetValue(id, out var uu) && uu.TerminalOnly && _uuDesktopSessions.TryRemove(id, out uu)) uu.DisposeAsync().AsTask().GetAwaiter().GetResult();
                TryWrite(context, 200, new { stopped = id });
                return true;
            }
            if (method == "GET")
            {
                if (machine.TransportMode == RDMachine.Transport.UURemote && _uuDesktopSessions.TryGetValue(id, out var uu) && uu.TerminalOnly)
                {
                    TryWrite(context, 200, new { phase = uu.Phase, output = "", error = uu.Failure });
                    return true;
                }
                if (!_terminalSessions.TryGetValue(id, out var session))
                {
                    TryWrite(context, 409, new { error = "The SSH terminal is not open." });
                    return true;
                }
                var bytes = session.ReadAvailable();
                TryWrite(context, 200, new { phase = session.IsConnected ? "connected" : "closed", output = Convert.ToBase64String(bytes) });
                return true;
            }
        }

        if (parts.Length == 3 && parts[0] == "terminal" && method == "POST")
        {
            var id = Uri.UnescapeDataString(parts[1]);
            if (!_terminalSessions.TryGetValue(id, out var session))
            {
                TryWrite(context, 409, new { error = "The SSH terminal is not open." });
                return true;
            }
            var body = ReadJson(context);
            if (parts[2] == "input")
            {
                var data = body.TryGetProperty("data", out var value) ? value.GetString() : null;
                if (data is null) { TryWrite(context, 400, new { error = "Terminal input is missing." }); return true; }
                session.Write(data);
                TryWrite(context, 200, new { sent = true });
                return true;
            }
            if (parts[2] == "resize")
            {
                var columns = body.TryGetProperty("columns", out var c) ? c.GetUInt32() : 100;
                var rows = body.TryGetProperty("rows", out var r) ? r.GetUInt32() : 30;
                session.Resize(columns, rows);
                TryWrite(context, 200, new { resized = true });
                return true;
            }
        }

        if (parts.Length >= 2 && parts.Length <= 3 && parts[0] == "files")
        {
            var id = Uri.UnescapeDataString(parts[1]);
            var machine = _store.FindMachine(id);
            if (machine is null) { TryWrite(context, 404, new { error = "No such server." }); return true; }
            try
            {
                var session = GetOrStartTerminalSession(machine);
                if (parts.Length == 2 && method == "GET")
                {
                    var path = context.Request.QueryString["path"] ?? machine.FileTransferLoginPath;
                    TryWrite(context, 200, new { path, entries = session.ListDirectory(path) });
                    return true;
                }
                if (parts.Length == 3 && parts[2] == "directory" && method == "POST")
                {
                    var body = ReadJson(context);
                    var path = body.TryGetProperty("path", out var value) ? value.GetString() : null;
                    if (string.IsNullOrWhiteSpace(path)) { TryWrite(context, 400, new { error = "A folder path is required." }); return true; }
                    session.CreateDirectory(path);
                    TryWrite(context, 200, new { created = path });
                    return true;
                }
                if (parts.Length == 3 && parts[2] == "upload" && method == "POST")
                {
                    var path = context.Request.QueryString["path"];
                    if (string.IsNullOrWhiteSpace(path)) { TryWrite(context, 400, new { error = "An upload path is required." }); return true; }
                    bool overwrite = context.Request.QueryString["overwrite"] != "false";
                    session.UploadFile(path, context.Request.InputStream, overwrite);
                    TryWrite(context, 200, new { uploaded = path });
                    return true;
                }
                if (parts.Length == 3 && parts[2] == "download" && method == "GET")
                {
                    var path = context.Request.QueryString["path"];
                    if (string.IsNullOrWhiteSpace(path)) { TryWrite(context, 400, new { error = "A download path is required." }); return true; }
                    var name = path.Replace('\\', '/').Split('/').LastOrDefault() ?? "download";
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.Headers.Add("Content-Disposition", $"attachment; filename*=UTF-8''{Uri.EscapeDataString(name)}");
                    context.Response.SendChunked = true;
                    session.DownloadFile(path, context.Response.OutputStream);
                    context.Response.OutputStream.Close();
                    return true;
                }
            }
            catch (Exception ex) { TryWrite(context, 502, new { error = ex.Message }); return true; }
        }

        return false;
    }

    private InteractiveSshSession GetOrStartTerminalSession(RDMachine machine)
    {
        lock (_terminalSessions)
        {
            if (_terminalSessions.TryGetValue(machine.Id, out var existing) && existing.IsConnected) return existing;
            if (_terminalSessions.TryRemove(machine.Id, out existing)) existing.Dispose();
            var session = new InteractiveSshSession(machine, BuildCredentials(machine), _knownHosts);
            _terminalSessions[machine.Id] = session;
            _store.MarkConnected(machine.Id, session.Banner);
            return session;
        }
    }

    private object VncDiscoverySnapshot() => new
    {
        isSearching = _vncDiscovery.IsSearching,
        failure = _vncDiscovery.Failure,
        hosts = _vncDiscovery.Hosts.Select(host => new
        {
            id = host.Id,
            name = host.Name,
            displayName = host.DisplayName,
            hostName = host.HostName,
            port = host.Port,
            machineId = _store.Machines.FirstOrDefault(machine =>
                machine.RemoteDesktopProtocol == RDMachine.RemoteDesktopProtocolType.Vnc &&
                string.Equals(machine.RemoteAddress.TrimEnd('.'), host.HostName, StringComparison.OrdinalIgnoreCase) &&
                machine.VncPort == host.Port.ToString())?.Id,
        }),
    };

    // ------------------------------------------------------------- stores

    private object MachineList() => new
    {
        folders = _store.Folders,
        folderInfo = _store.FolderInfos.Select(folder => new
        {
            id = folder.Id,
            name = folder.Name,
            sortIndex = folder.SortIndex,
            linked = folder.IsLinked,
            source = folder.Source,
        }),
        uuRemote = _uuAccount.Snapshot(),
        machines = _store.GroupedMachines().Select(group => new
        {
            group = group.Key,
            items = group.Select(ToMachineJson),
        }),
        recent = _store.RecentMachines(6).Select(ToMachineJson),
        loadFailed = _store.LoadFailed,
        loadError = _store.LoadError,
    };

    private static object ToMachineJson(RDMachine m) => new
    {
        id = m.Id,
        name = m.DisplayName,
        address = m.RemoteAddress,
        port = m.Port,
        group = string.IsNullOrWhiteSpace(m.Group) ? "Ungrouped" : m.Group,
        comment = m.Comment,
        identity = m.AssociatedIdentity is null ? "Automatic" : "Saved",
        identityId = m.AssociatedIdentity,
        transport = m.TransportMode switch
        {
            RDMachine.Transport.CloudflareAccess => "cloudflare-access",
            RDMachine.Transport.UURemote => "uu-remote",
            _ => "direct",
        },
        hasCloudflareServiceToken = !string.IsNullOrEmpty(m.CloudflareServiceTokenId),
        remoteDesktopProtocol = m.RemoteDesktopProtocol switch
        {
            RDMachine.RemoteDesktopProtocolType.Vnc => "vnc",
            RDMachine.RemoteDesktopProtocolType.UuRemote => "uu-remote",
            _ => "none",
        },
        uuDeviceID = m.UUDeviceID,
        uuDeviceAlias = m.UUDeviceAlias,
        uuDeviceStatus = m.UUDeviceStatus,
        uuWallpaperUrl = m.UUWallpaperUrl,
        uuRemoteManaged = m.UURemoteManaged,
        vncPort = m.VncPort,
        vncUsername = m.VncUsername,
        hasVncPassword = !string.IsNullOrEmpty(m.VncPassword),
        vncOverSSH = m.VncOverSSH,
        lastConnection = m.LastConnection.ToString("O"),
        fileTransferLoginPath = m.FileTransferLoginPath,
        lastBanner = m.LastBanner,
    };

    private void AddMachine(HttpListenerContext context)
    {
        var body = ReadJson(context);
        var raw = body.TryGetProperty("quickConnect", out var q) ? q.GetString() : null;

        RDMachine machine;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            // Quick Connect: "user@host:port" from the home page's one field.
            var parsed = RDMachine.FromQuickConnect(raw);
            if (parsed is null)
            {
                TryWrite(context, 400, new { error = "Type user@host:port." });
                return;
            }
            machine = parsed;
        }
        else
        {
            var address = body.TryGetProperty("address", out var a) ? a.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(address))
            {
                TryWrite(context, 400, new { error = "An address is required." });
                return;
            }
            machine = new RDMachine
            {
                Name = body.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString())
                    ? n.GetString()!
                    : address,
                RemoteAddress = address,
                RemotePort = body.TryGetProperty("port", out var p) ? (p.GetString() ?? "22") : "22",
                Group = body.TryGetProperty("group", out var g) ? (g.GetString() ?? "") : "",
                FileTransferLoginPath = body.TryGetProperty("fileTransferLoginPath", out var loginPath)
                    ? loginPath.GetString() ?? "/"
                    : "/",
                Comment = body.TryGetProperty("comment", out var c) ? (c.GetString() ?? "") : "",
                TransportMode = body.TryGetProperty("transport", out var transport) &&
                    transport.GetString() == "cloudflare-access"
                    ? RDMachine.Transport.CloudflareAccess
                    : RDMachine.Transport.Direct,
                CloudflareServiceTokenId = body.TryGetProperty("cloudflareServiceTokenId", out var tokenId)
                    ? tokenId.GetString() ?? ""
                    : "",
                CloudflareServiceTokenSecret = body.TryGetProperty("cloudflareServiceTokenSecret", out var tokenSecret)
                    ? tokenSecret.GetString() ?? ""
                    : "",
                RemoteDesktopProtocol = body.TryGetProperty("remoteDesktopProtocol", out var protocol) &&
                    protocol.GetString() == "vnc"
                    ? RDMachine.RemoteDesktopProtocolType.Vnc
                    : RDMachine.RemoteDesktopProtocolType.None,
                VncPort = body.TryGetProperty("vncPort", out var vncPort) ? vncPort.GetString() ?? "5900" : "5900",
                VncUsername = body.TryGetProperty("vncUsername", out var vncUsername) ? vncUsername.GetString() ?? "" : "",
                VncPassword = body.TryGetProperty("vncPassword", out var vncPassword) ? vncPassword.GetString() ?? "" : "",
                VncOverSSH = body.TryGetProperty("vncOverSSH", out var vncOverSSH) && vncOverSSH.ValueKind == JsonValueKind.True,
                VncShared = !body.TryGetProperty("vncShared", out var vncShared) || vncShared.ValueKind != JsonValueKind.False,
            };
        }

        _store.Upsert(machine);
        TryWrite(context, 201, ToMachineJson(machine));
    }

    private void AddFolder(HttpListenerContext context)
    {
        var body = ReadJson(context);
        var name = body.TryGetProperty("name", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            TryWrite(context, 400, new { error = "A folder name is required." });
            return;
        }

        if (!_store.CreateFolder(name))
        {
            TryWrite(context, 409, new { error = "A folder with that name already exists." });
            return;
        }

        TryWrite(context, 201, new { name = name.Trim() });
    }

    private void StartUUQrLogin(HttpListenerContext context)
    {
        var session = _uuAccount.StartQrLoginAsync().GetAwaiter().GetResult();
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(session.JumpUrl, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        TryWrite(context, 200, new
        {
            jumpUrl = session.JumpUrl,
            qrImage = $"data:image/png;base64,{Convert.ToBase64String(png)}",
            account = _uuAccount.Snapshot(),
        });
    }

    private void PollUUQrLogin(HttpListenerContext context)
    {
        var status = _uuAccount.PollQrLoginAsync().GetAwaiter().GetResult();
        TryWrite(context, 200, new { status, account = _uuAccount.Snapshot() });
    }

    private void SendUUSmsCode(HttpListenerContext context)
    {
        var body = ReadJson(context);
        var countryCode = body.TryGetProperty("countryCode", out var countryValue) ? countryValue.GetString()?.Trim() : null;
        var mobile = body.TryGetProperty("mobile", out var mobileValue) ? mobileValue.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(mobile))
        {
            TryWrite(context, 400, new { error = "Country code and mobile number are required." });
            return;
        }

        _uuAccount.SendSmsCodeAsync(countryCode, mobile).GetAwaiter().GetResult();
        TryWrite(context, 200, new { sent = true });
    }

    private void LoginUUBySms(HttpListenerContext context)
    {
        var body = ReadJson(context);
        var countryCode = body.TryGetProperty("countryCode", out var countryValue) ? countryValue.GetString()?.Trim() : null;
        var mobile = body.TryGetProperty("mobile", out var mobileValue) ? mobileValue.GetString()?.Trim() : null;
        var code = body.TryGetProperty("code", out var codeValue) ? codeValue.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(mobile) || string.IsNullOrWhiteSpace(code))
        {
            TryWrite(context, 400, new { error = "Country code, mobile number, and verification code are required." });
            return;
        }

        _uuAccount.LoginBySmsAsync(countryCode, mobile, code).GetAwaiter().GetResult();
        TryWrite(context, 200, _uuAccount.Snapshot());
    }

    private void ImportUUToken(HttpListenerContext context)
    {
        var body = ReadJson(context);
        var token = body.TryGetProperty("token", out var tokenValue) ? tokenValue.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(token))
        {
            TryWrite(context, 400, new { error = "A UU Remote token is required." });
            return;
        }

        var userID = body.TryGetProperty("userID", out var userValue) ? userValue.GetString() ?? "" : "";
        var deviceID = body.TryGetProperty("deviceID", out var deviceValue) ? deviceValue.GetString() ?? "" : "";
        var clientID = body.TryGetProperty("clientID", out var clientValue) ? clientValue.GetString() ?? "" : "";
        var systemID = body.TryGetProperty("systemID", out var systemValue) ? systemValue.GetString() ?? "" : "";
        _uuAccount.ImportTokenAsync(token, userID, deviceID, clientID, systemID).GetAwaiter().GetResult();
        TryWrite(context, 200, _uuAccount.Snapshot());
    }

    private void RefreshUUDevices(HttpListenerContext context)
    {
        _uuAccount.RefreshDevicesAsync().GetAwaiter().GetResult();
        TryWrite(context, 200, _uuAccount.Snapshot());
    }

    private void LogoutUUAccount(HttpListenerContext context)
    {
        foreach (var session in _uuDesktopSessions.Values) session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _uuDesktopSessions.Clear();
        _uuAccount.LogoutAsync().GetAwaiter().GetResult();
        TryWrite(context, 200, _uuAccount.Snapshot());
    }

    private void AddIdentity(HttpListenerContext context)
    {
        var body = ReadJson(context);
        var username = body.TryGetProperty("username", out var u) ? u.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(username))
        {
            TryWrite(context, 400, new { error = "A username is required." });
            return;
        }

        var identity = Identity.Create(
            username,
            password: body.TryGetProperty("password", out var p) ? p.GetString() : null,
            privateKey: body.TryGetProperty("privateKey", out var k) ? k.GetString() : null,
            passphrase: body.TryGetProperty("passphrase", out var ph) ? ph.GetString() : null,
            comment: body.TryGetProperty("comment", out var c) ? (c.GetString() ?? "") : "",
            authenticAutomatically: body.TryGetProperty("automatic", out var auto) && auto.ValueKind == JsonValueKind.True);

        _store.Upsert(identity);
        TryWrite(context, 201, new { id = identity.Id, username = identity.Username, keyType = identity.KeyType });
    }

    // ------------------------------------------------------------- monitor

    /// <summary>The demo server the monitor shows before any real one is added.</summary>
    private object MonitorSnapshot()
    {
        var history = new MonitorHistory();
        ServerStatus status = MonitorBridge.SnapshotAt(0);
        for (var i = 0; i < 24; i++)
        {
            status = MonitorBridge.SnapshotAt(i);
            history.Record(status);
        }
        return MonitorPayload.Shape(status, history, MonitorBridge.DemoAddress);
    }

    private void StartSession(RDMachine machine)
    {
        // One session per server: pressing Monitor again reuses the live one rather
        // than opening a second SSH connection to the same host.
        if (_sessions.TryGetValue(machine.Id, out var existing))
        {
            if (existing.Phase is MonitorPhase.Connected or MonitorPhase.Connecting) return;
            _sessions.TryRemove(machine.Id, out _);
            existing.Dispose();
        }

        var credentials = BuildCredentials(machine);
        var session = new MonitorSession(machine, credentials, _knownHosts);
        _sessions[machine.Id] = session;
        session.Start();
    }

    /// <summary>
    /// Picks credentials the way the Swift does: the named identity when the machine
    /// has one, otherwise every automatic identity in turn.
    /// </summary>
    private IReadOnlyList<SshCredential> BuildCredentials(RDMachine machine)
    {
        if (_store.IdentityFor(machine) is { } named)
            return [named.ToCredential()];

        return _store.AutoIdentities().Select(i => i.ToCredential()).ToList();
    }

    private object SessionSnapshot(RDMachine machine)
    {
        if (!_sessions.TryGetValue(machine.Id, out var session))
        {
            return new
            {
                phase = "idle",
                error = (string?)null,
                attempts = Array.Empty<object>(),
                payload = (object?)null,
            };
        }

        var status = session.Status;
        return new
        {
            phase = session.Phase.ToString().ToLowerInvariant(),
            error = session.Error,
            attempts = session.Attempts.Select(a => new
            {
                credential = a.Credential.ToString(),
                succeeded = a.Succeeded,
                error = a.Error,
            }),
            payload = session.HasData
                ? MonitorPayload.Shape(status, session.History, machine.RemoteAddress)
                : null,
        };
    }

    private void StartVncSession(RDMachine machine)
    {
        lock (_vncSessions)
        {
            if (_vncSessions.TryGetValue(machine.Id, out var existing))
            {
                if (existing.Phase is "connecting" or "connected") return;
                _vncSessions.TryRemove(machine.Id, out _);
                existing.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            _vncSessions[machine.Id] = VncSession.Start(
                machine,
                BuildCredentials(machine),
                _store.IdentityFor(machine),
                _knownHosts);
        }
    }
    private UURemoteBrowserSession StartUURemoteSession(RDMachine machine, bool terminalOnly)
    {
        if (!machine.IsUURemoteDevice)
            throw new InvalidOperationException("This server is not linked to a UU Remote device.");
        if (!_uuAccount.IsLoggedIn)
            throw new InvalidOperationException("Link a UU Remote account first.");

        string localDeviceID = _uuAccount.OwnDeviceID;
        if (localDeviceID.Length == 0)
            throw new InvalidOperationException("The local UU Remote device identity is not ready.");

        var device = _uuAccount.Devices.FirstOrDefault(item => item.DeviceID == machine.UUDeviceID);
        if (device is null)
        {
            _uuAccount.RefreshDevicesAsync().GetAwaiter().GetResult();
            device = _uuAccount.Devices.FirstOrDefault(item => item.DeviceID == machine.UUDeviceID);
        }
        if (device is null) throw new InvalidOperationException("The UU Remote device is no longer in this account.");
        if (!device.IsOnline) throw new InvalidOperationException("The UU Remote device is offline.");

        lock (_uuDesktopSessions)
        {
            if (_uuDesktopSessions.TryGetValue(machine.Id, out var existing))
            {
                if (existing.TerminalOnly == terminalOnly && existing.Phase is not ("failed" or "closed")) return existing;
                _uuDesktopSessions.TryRemove(machine.Id, out _);
                existing.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            var room = _uuAccount.ApiClient.JoinRoomByDeviceAsync(machine.UUDeviceID, forceJoin: true).GetAwaiter().GetResult();
            var session = UURemoteBrowserSession.ConnectAsync(room, machine.UUDeviceID, localDeviceID, terminalOnly, targetPlatform: device.Platform).GetAwaiter().GetResult();
            _uuDesktopSessions[machine.Id] = session;
            return session;
        }
    }


    private object VncSnapshot(string machineId, long afterVersion)
    {
        if (!_vncSessions.TryGetValue(machineId, out var session))
            return new { phase = "idle", error = (string?)null, frameVersion = 0L, frame = (object?)null };

        var frameVersion = session.FrameVersion;
        var frame = frameVersion > afterVersion ? session.Frame : null;
        return new
        {
            phase = session.Phase,
            error = session.Error,
            frameVersion,
            frame = frame is null
                ? null
                : new { width = frame.Width, height = frame.Height, desktopName = frame.DesktopName, rgba = frame.Rgba },
        };
    }

    // ------------------------------------------------------------- plumbing

    private static JsonElement ReadJson(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(text)) return JsonDocument.Parse("{}").RootElement;
        return JsonDocument.Parse(text).RootElement;
    }

    private static void TryWrite(HttpListenerContext context, int status, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[catussh] api write failed: {ex.Message}");
        }
        finally
        {
            try { context.Response.OutputStream.Close(); }
            catch (Exception) { /* client gone */ }
        }
    }

    private static int FindFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var session in _vncSessions.Values)
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _vncSessions.Clear();
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();
        foreach (var session in _terminalSessions.Values) session.Dispose();
        _terminalSessions.Clear();
        foreach (var session in _uuDesktopSessions.Values) session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _uuDesktopSessions.Clear();
        _vncDiscovery.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { _cts?.Cancel(); } catch (Exception) { }
        try { _listener.Stop(); } catch (Exception) { }
        _listener.Close();
        _cts?.Dispose();
    }
}
