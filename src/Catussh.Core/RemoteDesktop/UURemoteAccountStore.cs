using Catussh.Core.Store;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Catussh.Core.RemoteDesktop;

public sealed record UUDeviceRegistrationProfile(
    string Name,
    string OS,
    string BaseBoard,
    string Cpu,
    IReadOnlyList<string> Video,
    string Mac,
    ulong MemoryMiB,
    string Screen)
{
    public static UUDeviceRegistrationProfile FromCurrentMachine(string screen)
    {
        var network = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(item => item.OperationalStatus == OperationalStatus.Up &&
                                    item.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel));
        var mac = network?.GetPhysicalAddress().GetAddressBytes();
        var macText = mac is { Length: > 0 } ? string.Join('-', mac.Select(static value => value.ToString("X2"))) : "";
        var memory = Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024L));
        var cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString();
        return new UUDeviceRegistrationProfile(
            $"catussh · {Environment.MachineName}",
            RuntimeInformation.OSDescription,
            Environment.MachineName,
            cpu,
            [],
            macText,
            checked((ulong)memory),
            screen);
    }
}

/// <summary>Persisted account state and its device folder synchronization.</summary>
public sealed class UURemoteAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly object _gate = new();
    private readonly CatusshStore _store;
    private readonly UURemoteApiClient _client;
    private readonly UUDeviceRegistrationProfile _profile;
    private UURemoteCredentials _credentials;
    private string _nickname;
    private List<UUDeviceInfo> _devices = [];
    private UUQrLoginSession? _qrSession;
    private int _qrStatus = 1;
    private string _qrState = "idle";
    private string _qrHint = "";
    private string? _loginError;
    private string? _refreshError;

    public UURemoteAccountStore(CatusshStore store, UUDeviceRegistrationProfile profile, UURemoteApiClient? client = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        var persisted = store.ReadUURemoteAccount();
        _nickname = persisted.Nickname;
        _credentials = string.IsNullOrWhiteSpace(persisted.CredentialsJson)
            ? new UURemoteCredentials()
            : JsonSerializer.Deserialize<UURemoteCredentials>(persisted.CredentialsJson, JsonOptions) ?? new UURemoteCredentials();
        _client = client ?? new UURemoteApiClient(_credentials);
        _client.UpdateCredentials(_credentials);
    }

    public UURemoteApiClient ApiClient => _client;

    public UURemoteCredentials Credentials
    {
        get { lock (_gate) return _credentials; }
    }

    public string UserID { get { lock (_gate) return _credentials.UserID; } }
    public string Nickname { get { lock (_gate) return _nickname; } }
    public string OwnDeviceID { get { lock (_gate) return _credentials.DeviceID; } }
    public bool IsLoggedIn { get { lock (_gate) return _credentials.IsComplete; } }
    public IReadOnlyList<UUDeviceInfo> Devices { get { lock (_gate) return _devices.ToArray(); } }
    public string QrState { get { lock (_gate) return _qrState; } }
    public string QrHint { get { lock (_gate) return _qrHint; } }
    public string? LoginError { get { lock (_gate) return _loginError; } }
    public string? RefreshError { get { lock (_gate) return _refreshError; } }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLocalDeviceIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (!IsLoggedIn) return;
        try { await RefreshDevicesAsync(cancellationToken).ConfigureAwait(false); }
        catch { }
    }

    public async Task<UUQrLoginSession> StartQrLoginAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            SetQr("generating", "Generating QR code…", null);
            await EnsureRegisteredDeviceAsync(cancellationToken).ConfigureAwait(false);
            var session = await _client.GenerateQrLoginAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _qrSession = session;
                _qrStatus = 1;
                _qrState = "waiting";
                _qrHint = "Scan with the UU Remote app";
                _loginError = null;
            }
            return session;
        }
        catch (Exception ex)
        {
            SetQr("failed", ex.Message, ex.Message);
            throw;
        }
    }

    public async Task<int> PollQrLoginAsync(CancellationToken cancellationToken = default)
    {
        UUQrLoginSession session;
        int lastStatus;
        lock (_gate)
        {
            session = _qrSession ?? throw new InvalidOperationException("There is no active UU Remote QR login.");
            lastStatus = _qrStatus;
        }

        var status = await _client.PollQrLoginAsync(session.Token, session.JumpUrl, lastStatus, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (status != 3) _qrStatus = status;
            if (status == 0)
            {
                _qrSession = null;
                _qrState = "expired";
                _qrHint = "QR code expired";
            }
            else if (status == 2)
            {
                _qrState = "scanned";
                _qrHint = "Scanned — confirm on the device";
            }
            else if (status == 4)
            {
                _qrState = "confirmed";
                _qrHint = "Logging in…";
            }
            else if (status == 1)
            {
                _qrState = "waiting";
                _qrHint = "Scan with the UU Remote app";
            }
        }

        if (status == 4)
        {
            var login = await _client.CompleteQrLoginAsync(session.Token, session.JumpUrl, cancellationToken).ConfigureAwait(false);
            await ApplyLoginAsync(login).ConfigureAwait(false);
            lock (_gate)
            {
                _qrSession = null;
                _qrState = "confirmed";
                _qrHint = $"Logged in as {(_nickname.Length > 0 ? _nickname : _credentials.UserID)}";
            }
        }
        return status;
    }

    public async Task SendSmsCodeAsync(string countryCode, string mobile, CancellationToken cancellationToken = default)
    {
        await EnsureRegisteredDeviceAsync(cancellationToken).ConfigureAwait(false);
        await _client.SendMobileCodeAsync(countryCode, mobile, cancellationToken).ConfigureAwait(false);
    }

    public async Task LoginBySmsAsync(string countryCode, string mobile, string code, CancellationToken cancellationToken = default)
    {
        await EnsureRegisteredDeviceAsync(cancellationToken).ConfigureAwait(false);
        var login = await _client.CompleteMobileLoginAsync(countryCode, mobile, code, cancellationToken).ConfigureAwait(false);
        await ApplyLoginAsync(login).ConfigureAwait(false);
    }

    public async Task ImportTokenAsync(
        string token,
        string userID = "",
        string deviceID = "",
        string clientID = "",
        string systemID = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("UU Remote token is empty.", nameof(token));
        lock (_gate)
        {
            _credentials = _credentials with
            {
                Token = token.Trim(),
                UserID = string.IsNullOrWhiteSpace(userID) ? UURequestSigner.UserIDFrom(token.Trim()) ?? "" : userID.Trim(),
                Csrf = UURequestSigner.CsrfFrom(token.Trim()) ?? "",
                DeviceID = string.IsNullOrWhiteSpace(deviceID) ? _credentials.DeviceID : deviceID.Trim(),
                ClientID = string.IsNullOrWhiteSpace(clientID) ? _credentials.ClientID : clientID.Trim(),
                SystemID = string.IsNullOrWhiteSpace(systemID) ? _credentials.SystemID : systemID.Trim(),
            };
            _client.UpdateCredentials(_credentials);
            PersistLocked();
        }
        await EnsureLocalDeviceIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (IsLoggedIn) await RefreshDevicesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn) throw new InvalidOperationException("Link a UU Remote account first.");
        try
        {
            var devices = await _client.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _devices = devices.ToList();
                _refreshError = null;
            }
            var folder = _store.EnsureUURemoteFolder(UserID);
            UURemoteFolderSync.Apply(devices, folder.Name, OwnDeviceID, _store);
        }
        catch (Exception ex)
        {
            lock (_gate) _refreshError = ex.Message;
            throw;
        }
    }

    public async Task LogoutAsync()
    {
        lock (_gate)
        {
            _qrSession = null;
            _qrState = "idle";
            _qrHint = "";
            _devices.Clear();
            _loginError = null;
            _refreshError = null;
            _credentials = new UURemoteCredentials
            {
                ClientID = _credentials.ClientID,
                SystemID = _credentials.SystemID,
                DeviceID = _credentials.DeviceID,
            };
            _nickname = "";
            _client.UpdateCredentials(_credentials);
            PersistLocked();
        }
        await Task.CompletedTask;
    }

    public object Snapshot() => new
    {
        isLoggedIn = IsLoggedIn,
        userID = UserID,
        nickname = Nickname,
        ownDeviceID = OwnDeviceID,
        devices = Devices.Select(device => new
        {
            deviceID = device.DeviceID,
            alias = device.Alias,
            status = device.Status,
            online = device.IsOnline,
            platform = device.Platform,
            platformName = UUDeviceInfo.PlatformName(device.Platform),
            desktop = device.IsDesktop,
            controllable = device.Controllable,
            wallpaperUrl = device.WallpaperUrl,
        }),
        desktopDeviceCount = Devices.Count(device => device.IsDesktop && device.DeviceID != OwnDeviceID),
        qrState = QrState,
        qrHint = QrHint,
        loginError = LoginError,
        refreshError = RefreshError,
    };

    private async Task ApplyLoginAsync(UUUserLogin login)
    {
        lock (_gate)
        {
            _credentials = _credentials with
            {
                Token = login.Token,
                UserID = string.IsNullOrWhiteSpace(login.UserID) ? UURequestSigner.UserIDFrom(login.Token) ?? "" : login.UserID,
                Csrf = UURequestSigner.CsrfFrom(login.Token) ?? "",
            };
            _nickname = login.Nickname;
            _client.UpdateCredentials(_credentials);
            PersistLocked();
        }
        await EnsureLocalDeviceIdentityAsync(CancellationToken.None).ConfigureAwait(false);
        if (IsLoggedIn) await RefreshDevicesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task EnsureRegisteredDeviceAsync(CancellationToken cancellationToken)
    {
        await EnsureLocalDeviceIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(Credentials.DeviceID)) return;
        await RegisterLocalDeviceAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureLocalDeviceIdentityAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_credentials.ClientID.Length == 0 || _credentials.SystemID.Length == 0)
            {
                _credentials = _credentials with
                {
                    ClientID = _credentials.ClientID.Length == 0 ? Guid.NewGuid().ToString() : _credentials.ClientID,
                    SystemID = _credentials.SystemID.Length == 0 ? Guid.NewGuid().ToString() : _credentials.SystemID,
                };
                _client.UpdateCredentials(_credentials);
                PersistLocked();
            }
            if (string.IsNullOrEmpty(_credentials.Token) || !string.IsNullOrEmpty(_credentials.DeviceID)) return;
        }

        try
        {
            await RegisterLocalDeviceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_credentials.DeviceID.Length != 0) return;
                var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"uu|{_credentials.ClientID}|{_credentials.SystemID}"));
                _credentials = _credentials with { DeviceID = Convert.ToHexStringLower(digest.AsSpan(0, 8)) };
                _client.UpdateCredentials(_credentials);
                PersistLocked();
            }
        }
    }

    private async Task RegisterLocalDeviceAsync(CancellationToken cancellationToken)
    {
        var credentials = Credentials;
        if (credentials.DeviceID.Length != 0) return;
        var deviceID = await _client.RegisterWindowsDeviceAsync(
            _profile.Name,
            credentials.ClientID,
            credentials.SystemID,
            Guid.NewGuid().ToString(),
            _profile.OS,
            _profile.BaseBoard,
            _profile.Cpu,
            _profile.Video,
            _profile.Mac,
            _profile.MemoryMiB,
            _profile.Screen,
            cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _credentials = _credentials with { DeviceID = deviceID };
            _client.UpdateCredentials(_credentials);
            PersistLocked();
        }
    }

    private void SetQr(string state, string hint, string? error)
    {
        lock (_gate)
        {
            _qrState = state;
            _qrHint = hint;
            _loginError = error;
        }
    }

    private void PersistLocked()
    {
        _store.SaveUURemoteAccount(JsonSerializer.Serialize(_credentials, JsonOptions), _nickname);
    }
}
