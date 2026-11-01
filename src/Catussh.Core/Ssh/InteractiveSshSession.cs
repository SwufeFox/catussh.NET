using System.Text;
using Catussh.Core.Store;
using Catussh.Core.Utils;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Catussh.Core.Ssh;

public sealed record SftpEntry(string Name, string FullName, bool IsDirectory, long Length, DateTime LastWriteTimeUtc);

/// <summary>One authenticated interactive SSH shell with SFTP access over the same saved transport.</summary>
public sealed class InteractiveSshSession : IDisposable
{
    private readonly object _gate = new();
    private readonly SshConnection _connection;
    private readonly ShellStream _shell;
    private SftpClient? _sftp;
    private bool _disposed;

    public InteractiveSshSession(
        RDMachine machine,
        IReadOnlyList<SshCredential> credentials,
        KnownHosts? knownHosts = null,
        uint columns = 100,
        uint rows = 30,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(credentials);
        if (machine.TransportMode == RDMachine.Transport.UURemote)
            throw new NotSupportedException("UU Remote terminals require an active WebRTC session.");

        _connection = new SshConnection(machine, TimeSpan.FromSeconds(20), knownHosts);
        var (outcome, attempts) = _connection.Connect(credentials, cancellationToken);
        if (outcome != SshConnectOutcome.Success)
        {
            var detail = _connection.LastError ?? attempts.LastOrDefault()?.Error ?? "SSH authentication failed.";
            _connection.Dispose();
            throw new InvalidOperationException(detail);
        }

        try
        {
            _shell = _connection.OpenShell("vt100", columns, rows);
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    public bool IsConnected => !_disposed && _connection.IsConnected;
    public string? Banner => _connection.Banner;

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            ThrowIfDisposed();
            _shell.Write(Encoding.UTF8.GetBytes(text));
            _shell.Flush();
        }
    }

    public byte[] ReadAvailable(int maximumBytes = 64 * 1024)
    {
        if (maximumBytes is < 1 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_shell.DataAvailable) return [];
            var buffer = new byte[maximumBytes];
            var count = _shell.Read(buffer, 0, buffer.Length);
            return count == buffer.Length ? buffer : buffer.AsSpan(0, count).ToArray();
        }
    }

    public void Resize(uint columns, uint rows)
    {
        if (columns is < 1 or > 1000 || rows is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(columns), "Terminal dimensions must be between 1 and 1000.");
        lock (_gate)
        {
            ThrowIfDisposed();
            _shell.ChangeWindowSize(columns, rows, 0, 0);
        }
    }

    public IReadOnlyList<SftpEntry> ListDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            ThrowIfDisposed();
            return GetSftpClient().ListDirectory(path)
                .Where(static file => file.Name is not ("." or ".."))
                .OrderByDescending(static file => file.IsDirectory)
                .ThenBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static file => new SftpEntry(file.Name, file.FullName, file.IsDirectory, file.Length, file.LastWriteTimeUtc))
                .ToArray();
        }
    }

    public void CreateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            ThrowIfDisposed();
            GetSftpClient().CreateDirectory(path);
        }
    }

    public void DownloadFile(string path, Stream destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(destination);
        lock (_gate)
        {
            ThrowIfDisposed();
            GetSftpClient().DownloadFile(path, destination);
        }
    }

    public void UploadFile(string path, Stream source, bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            ThrowIfDisposed();
            GetSftpClient().UploadFile(source, path, canOverride: overwrite);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _sftp?.Dispose();
            _shell.Dispose();
            _connection.Dispose();
        }
    }

    private SftpClient GetSftpClient()
    {
        if (_sftp is { IsConnected: true } connected) return connected;
        _sftp?.Dispose();
        var client = _connection.OpenSftp();
        _sftp = client;
        return client;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
