using Catussh.Core.Utils;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
using System.Text;
using System.Text.RegularExpressions;

namespace Catussh.Core.Ssh;

/// <summary>How a connection attempt ended. Mirrors <c>ConnectAndAuthenticateResult</c>
/// in <c>NSRemoteShell+catussh.swift</c>, plus the failure the Swift only expressed
/// through <c>getLastError</c>.</summary>
public enum SshConnectOutcome
{
    /// <summary>Connected and authenticated.</summary>
    Success,
    /// <summary>The TCP/SSH handshake failed — refused, timed out, host key refused.</summary>
    ConnectFailed,
    /// <summary>Connected, but every credential was rejected.</summary>
    AuthenticateFailed,
}

/// <summary>One credential to try. Password or private key, never both required.</summary>
public sealed record SshCredential
{
    public required string Username { get; init; }

    public string? Password { get; init; }

    /// <summary>OpenSSH private key text (PEM or the openssh-key-v1 block).</summary>
    public string? PrivateKey { get; init; }

    /// <summary>Passphrase for an encrypted private key.</summary>
    public string? Passphrase { get; init; }

    public string? PublicKey { get; init; }

    public static SshCredential FromPassword(string username, string password) =>
        new() { Username = username, Password = password };

    public static SshCredential FromPrivateKey(string username, string privateKey, string? passphrase = null) =>
        new() { Username = username, PrivateKey = privateKey, Passphrase = passphrase };

    /// <summary>What the UI shows while trying several — the Swift's <c>shortDescription()</c>.</summary>
    public override string ToString() =>
        Password is not null ? $"{Username} · password" : $"{Username} · key";
}

/// <summary>Result of one credential attempt, for the "tried everything" message.</summary>
public sealed record SshAttempt(SshCredential Credential, bool Succeeded, string? Error);

/// <summary>
/// A live SSH connection — the port of <c>NSRemoteShell</c>.
///
/// The Swift wrapper is a C shim over libssh2 with a run loop; SSH.NET is already a
/// managed session object, so this class keeps the parts the app actually uses:
/// connect, authenticate, run a command to completion, and open an interactive
/// shell. Port forwarding and SFTP follow the same shapes when they are needed.
///
/// Threading: SSH.NET's <see cref="SshClient"/> is not thread-safe. The app opens
/// one connection per session and serializes calls through <see cref="_gate"/>,
/// which is what the Swift event loop did implicitly.
/// </summary>
public sealed class SshConnection : IDisposable
{
    private readonly object _gate = new();
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;
    private readonly KnownHosts? _knownHosts;
    private SshClient? _client;
    private string? _banner;
    private string? _fingerprint;
    private bool _disposed;

    public SshConnection(string host, int port, TimeSpan timeout, KnownHosts? knownHosts = null)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is empty.", nameof(host));
        _host = host.Trim();
        _port = port <= 0 ? 22 : port;
        _timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(20) : timeout;
        _knownHosts = knownHosts;
    }

    public string Host => _host;

    public int Port => _port;

    public bool IsConnected
    {
        get
        {
            lock (_gate) return _client?.IsConnected == true;
        }
    }

    /// <summary>The server's SSH identification string, as <c>remoteBanner</c>.</summary>
    public string? Banner
    {
        get { lock (_gate) return _banner; }
    }

    /// <summary><c>SHA256:…</c> of the host key, as <c>remoteFingerPrint</c>.</summary>
    public string? Fingerprint
    {
        get { lock (_gate) return _fingerprint; }
    }

    /// <summary>The last failure, as <c>getLastError</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Connects and authenticates, trying each credential in turn. A username change
    /// needs a fresh connection because the remote authenticates per connection —
    /// same rule as <c>authenticateWithAutoIdentities</c>.
    /// </summary>
    public (SshConnectOutcome Outcome, IReadOnlyList<SshAttempt> Attempts) Connect(
        IReadOnlyList<SshCredential> credentials)
    {
        var attempts = new List<SshAttempt>();
        lock (_gate)
        {
            if (credentials.Count == 0)
            {
                LastError = "No identity to authenticate with.";
                return (SshConnectOutcome.AuthenticateFailed, attempts);
            }

            // Handshake once to learn the banner and host key before any credentials
            // are offered; the Swift asks its verifier at this point.
            if (!EnsureHandshake()) return (SshConnectOutcome.ConnectFailed, attempts);

            foreach (var credential in credentials)
            {
                try
                {
                    Authenticate(credential);
                }
                catch (Exception ex)
                {
                    var message = Describe(ex);
                    attempts.Add(new SshAttempt(credential, false, message));
                    LastError = message;
                    continue;
                }

                if (_client is { IsConnected: true })
                {
                    attempts.Add(new SshAttempt(credential, true, null));
                    LastError = null;
                    return (SshConnectOutcome.Success, attempts);
                }

                var failure = LastError ?? "Authentication failed.";
                attempts.Add(new SshAttempt(credential, false, failure));
            }

            LastError = BuildRefusalSummary(attempts);
            return (SshConnectOutcome.AuthenticateFailed, attempts);
        }
    }

    /// <summary>
    /// Runs one command and returns everything it printed. This is what the monitor
    /// probe uses: the merged script emits marker-delimited sections on stdout.
    /// </summary>
    public string Execute(string command, TimeSpan? timeout = null)
    {
        lock (_gate)
        {
            if (_client is not { IsConnected: true })
            {
                LastError = "Not connected.";
                return "";
            }

            var result = new StringBuilder();
            try
            {
                using var cmd = _client.CreateCommand(command);
                cmd.CommandTimeout = timeout ?? _timeout;
                // stderr carries the shell's own complaints (missing nvidia-smi, a
                // permission denial); the Swift appended both streams to one buffer.
                var output = cmd.Execute();
                result.Append(output);
                var error = cmd.Error;
                if (!string.IsNullOrEmpty(error)) result.Append(error);
                LastError = null;
            }
            catch (SshException ex)
            {
                // SshOperationTimeoutException derives from SshException, so one
                // handler covers both; Describe() picks the right sentence.
                LastError = Describe(ex);
            }
            return result.ToString();
        }
    }

    /// <summary>Runs a command and reports whether it exited zero, for probes where only success matters.</summary>
    public (bool Ok, string Output, string? Error) TryExecute(string command, TimeSpan? timeout = null)
    {
        lock (_gate)
        {
            if (_client is not { IsConnected: true }) return (false, "", "Not connected.");
            try
            {
                using var cmd = _client.CreateCommand(command);
                cmd.CommandTimeout = timeout ?? _timeout;
                var output = cmd.Execute();
                return (cmd.ExitStatus == 0, output, null);
            }
            catch (Exception ex)
            {
                return (false, "", Describe(ex));
            }
        }
    }

    /// <summary>
    /// Opens an interactive shell (a pty) — the port of <c>beginShellWithTerminalType</c>.
    /// The caller owns the stream and must dispose it.
    /// </summary>
    public ShellStream OpenShell(string terminalType, uint columns, uint rows, int bufferSize = 4096)
    {
        lock (_gate)
        {
            if (_client is not { IsConnected: true })
                throw new InvalidOperationException("Not connected.");

            var stream = _client.CreateShellStream(
                terminalType,
                columns,
                rows,
                0,
                0,
                bufferSize);
            return stream;
        }
    }

    /// <summary>
    /// A local port forward — the port of <c>createPortForwardWithLocalPort</c>.
    /// The caller disposes both the returned forward and the connection.
    /// </summary>
    public ForwardedPortLocal ForwardLocal(int localPort, string targetHost, int targetPort)
    {
        lock (_gate)
        {
            if (_client is not { IsConnected: true })
                throw new InvalidOperationException("Not connected.");

            var forward = new ForwardedPortLocal("127.0.0.1", (uint)localPort, targetHost, (uint)targetPort);
            _client.AddForwardedPort(forward);
            forward.Start();
            return forward;
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            try { _client?.Disconnect(); }
            catch (Exception) { /* Already gone. */ }
            _client?.Dispose();
            _client = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }

    // ---------------------------------------------------------------- internals

    /// <summary>
    /// Establishes the transport and captures banner + host key before authentication.
    /// Refuses the connection when known_hosts says the key changed.
    /// </summary>
    private bool EnsureHandshake()
    {
        try
        {
            _client?.Dispose();
            _client = new SshClient(BuildConnectionInfo(credentials: null));
            _client.HostKeyReceived += OnHostKeyReceived;
            _client.Connect();
            _banner = _client.ConnectionInfo?.ServerVersion;
            return true;
        }
        catch (Exception ex)
        {
            LastError = Describe(ex);
            _client?.Dispose();
            _client = null;
            return false;
        }
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        // SSH.NET asks on the connecting thread; answering false aborts the handshake,
        // which is how the Swift verifier returning NO behaved.
        var key = new KnownHosts.HostKey(e.HostKeyName ?? "ssh-unknown", e.HostKey);
        _fingerprint = key.Fingerprint;

        if (_knownHosts is not null && !_knownHosts.Accepts(key, _host, _port))
        {
            LastError = _knownHosts.RefusalMessage(_host, _port) ?? "Host key refused.";
            e.CanTrust = false;
            return;
        }
        e.CanTrust = true;
    }

    private void Authenticate(SshCredential credential)
    {
        _client?.Dispose();
        _client = new SshClient(BuildConnectionInfo(credential));
        _client.HostKeyReceived += OnHostKeyReceived;
        _client.Connect();
        _banner ??= _client.ConnectionInfo?.ServerVersion;
    }

    private ConnectionInfo BuildConnectionInfo(SshCredential? credentials)
    {
        var methods = new List<AuthenticationMethod>();
        if (credentials is null)
        {
            // Probe only: connect far enough to read the banner and host key.
            methods.Add(new NoneAuthenticationMethod("probe"));
        }
        else if (!string.IsNullOrEmpty(credentials.PrivateKey))
        {
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(credentials.PrivateKey));
            var keyFile = string.IsNullOrEmpty(credentials.Passphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, credentials.Passphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(credentials.Username, keyFile));
            if (!string.IsNullOrEmpty(credentials.Password))
                methods.Add(new PasswordAuthenticationMethod(credentials.Username, credentials.Password));
        }
        else
        {
            methods.Add(new PasswordAuthenticationMethod(credentials.Username, credentials.Password ?? ""));
        }

        return new ConnectionInfo(_host, _port, credentials?.Username ?? "probe", methods.ToArray())
        {
            Timeout = _timeout,
        };
    }

    private static string BuildRefusalSummary(IReadOnlyList<SshAttempt> attempts)
    {
        if (attempts.Count == 0) return "Authentication failed.";
        if (attempts.Count == 1) return attempts[0].Error ?? "Authentication failed.";
        return "Tried " + attempts.Count + " identities, all refused.";
    }

    /// <summary>
    /// Turns SSH.NET exceptions into sentences a person can act on. The Swift surfaced
    /// libssh2 strings; these are the same situations with clearer wording.
    /// </summary>
    public static string Describe(Exception ex) => ex switch
    {
        SshAuthenticationException => "Wrong username, password or key.",
        SshConnectionException { DisconnectReason: DisconnectReason.HostNotAllowedToConnect } =>
            "The server does not allow this host.",
        SshConnectionException { DisconnectReason: DisconnectReason.NoMoreAuthenticationMethodsAvailable } =>
            "The server rejected every authentication method offered.",
        SshConnectionException { DisconnectReason: DisconnectReason.ByApplication } =>
            "The server closed the connection.",
        SshConnectionException connection => Cleanup(connection.Message),
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused } =>
            "Connection refused — is SSH running on that port?",
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.TimedOut } =>
            "Timed out — the host did not answer.",
        System.Net.Sockets.SocketException socket => socket.Message,
        SshOperationTimeoutException => "Timed out waiting for the server.",
        SshException ssh => Cleanup(ssh.Message),
        _ => Cleanup(ex.Message),
    };

    /// <summary>SSH.NET prefixes many messages with a bracketed client tag; drop it.</summary>
    private static string Cleanup(string message)
    {
        var cleaned = Regex.Replace(message, @"^\s*\[[^\]]+\]\s*", "");
        return string.IsNullOrWhiteSpace(cleaned) ? "Unknown error." : cleaned.Trim();
    }
}
