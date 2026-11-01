using Renci.SshNet;

namespace Catussh.Core.Ssh;

/// <summary>Owns an interactive shell channel opened over a caller-owned SSH connection.</summary>
public sealed class SshTerminalSession : IDisposable, IAsyncDisposable
{
    public const int DefaultBufferSize = 4096;
    public const int MaximumReadSize = 1024 * 1024;

    private readonly ShellStream _shell;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _disposed;

    private SshTerminalSession(ShellStream shell) => _shell = shell;

    /// <summary>
    /// Authenticates the supplied connection and opens its interactive shell. SSH.NET's
    /// connection and shell setup are synchronous, so they run on a worker thread.
    /// The caller retains ownership of <paramref name="connection"/>.
    /// </summary>
    public static Task<SshTerminalSession> OpenAsync(
        SshConnection connection,
        SshCredential credential,
        string terminalType = "xterm-256color",
        uint columns = 80,
        uint rows = 24,
        int bufferSize = DefaultBufferSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(terminalType))
            throw new ArgumentException("Terminal type is empty.", nameof(terminalType));
        if (columns == 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows == 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (outcome, attempts) = connection.Connect([credential]);
            if (outcome != SshConnectOutcome.Success)
                throw new InvalidOperationException(connection.LastError ?? attempts.FirstOrDefault()?.Error ?? "SSH authentication failed.");

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new SshTerminalSession(connection.OpenShell(terminalType, columns, rows, bufferSize));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(SshConnection.Describe(ex), ex);
            }
        }, cancellationToken);
    }

    /// <summary>Reads at most <paramref name="maximumBytes"/> of pending shell output.</summary>
    public async Task<byte[]> ReadAsync(int maximumBytes = 16 * 1024, CancellationToken cancellationToken = default)
    {
        if (maximumBytes is <= 0 or > MaximumReadSize)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), $"Read size must be between 1 and {MaximumReadSize} bytes.");
        ThrowIfDisposed();
        await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var buffer = new byte[maximumBytes];
            var count = await _shell.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) return [];
            if (count == buffer.Length) return buffer;
            Array.Resize(ref buffer, count);
            return buffer;
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(SshTerminalSession));
        }
        catch (Exception ex) when (IsDisposed && ex is not OperationCanceledException)
        {
            throw new ObjectDisposedException(nameof(SshTerminalSession), ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(SshConnection.Describe(ex), ex);
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>Writes bytes to the remote shell's standard input.</summary>
    public async Task WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _shell.WriteAsync(input, cancellationToken).ConfigureAwait(false);
            await _shell.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(SshTerminalSession));
        }
        catch (Exception ex) when (IsDisposed && ex is not OperationCanceledException)
        {
            throw new ObjectDisposedException(nameof(SshTerminalSession), ex.Message);
        }
        catch (Exception ex) when (!IsDisposed && ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(SshConnection.Describe(ex), ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Writes text to the remote shell using UTF-8.</summary>
    public Task WriteAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return WriteAsync(System.Text.Encoding.UTF8.GetBytes(input), cancellationToken);
    }

    /// <summary>Changes the remote pseudo-terminal dimensions.</summary>
    public Task ResizeAsync(uint columns, uint rows, CancellationToken cancellationToken = default)
    {
        if (columns == 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows == 0) throw new ArgumentOutOfRangeException(nameof(rows));
        ThrowIfDisposed();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            try
            {
                _shell.ResizeTerminal(checked((int)columns), checked((int)rows), 0, 0);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (IsDisposed) throw new ObjectDisposedException(nameof(SshTerminalSession), ex.Message);
                throw new InvalidOperationException(SshConnection.Describe(ex), ex);
            }
        }, cancellationToken);
    }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _shell.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SshTerminalSession));
    }
}
