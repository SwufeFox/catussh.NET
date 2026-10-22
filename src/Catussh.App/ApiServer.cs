using Catussh.Core.Machine;
using Catussh.Core.Ssh;
using Catussh.Core.Store;
using Catussh.Core.Utils;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

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
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ApiServer(CatusshStore store, KnownHosts knownHosts)
    {
        _store = store;
        _knownHosts = knownHosts;
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
        Console.Error.WriteLine($"[catussh] api on http://127.0.0.1:{Port}/");
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
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[catussh] api error: {ex.Message}");
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

            case ("/monitor", "GET"):
                TryWrite(context, 200, MonitorSnapshot());
                return;
        }

        // /machines/{id}, DELETE /machines/{id}, /monitor/{id}, POST /monitor/{id}
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0] == "machines" && method == "DELETE")
        {
            var removed = _store.RemoveMachine(parts[1]);
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

        TryWrite(context, 404, new { error = $"No route for {method} {path}." });
    }

    // ------------------------------------------------------------- stores

    private object MachineList() => new
    {
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
        lastConnection = m.LastConnection.ToString("O"),
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
                Comment = body.TryGetProperty("comment", out var c) ? (c.GetString() ?? "") : "",
            };
        }

        _store.Upsert(machine);
        TryWrite(context, 201, ToMachineJson(machine));
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
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();
        try { _cts?.Cancel(); } catch (Exception) { }
        try { _listener.Stop(); } catch (Exception) { }
        _listener.Close();
        _cts?.Dispose();
    }
}
