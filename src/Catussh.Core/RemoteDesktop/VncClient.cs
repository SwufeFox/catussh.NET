using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Catussh.Core.RemoteDesktop;

/// <summary>RFB framebuffer snapshot in row-major RGBA byte order.</summary>
public sealed record VncFrame(int Width, int Height, byte[] Rgba, string DesktopName);

/// <summary>Small RFB 3.3/3.7/3.8 client with bounded framebuffer updates.</summary>
public sealed class VncClient : IAsyncDisposable
{
    private const int MaxDimension = 16_384;
    private const int MaxPixels = 16 * 1024 * 1024;
    private const int MaxNameBytes = 64 * 1024;
    private const int MaxClipboardBytes = 1024 * 1024;
    private const int MaxRectanglesPerUpdate = 4096;
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly string _username;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _inputGate = new();
    public event Action<Exception>? Failed;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private Task? _reader;
    private byte[] _rgba = [];
    private int _width, _height;
    private string _desktopName = "";
    private byte _buttons;
    private bool _started;
    private int _closed;

    public event Action<VncFrame>? FrameUpdated;
    public event Action<string>? ClipboardReceived;
    public event Action? Bell;
    public int Width => _width;
    public int Height => _height;
    public string DesktopName => _desktopName;

    public VncClient(string host, int port, string password = "", string username = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _host = host; _port = port; _password = password; _username = username;
    }

    /// <summary>Connects and negotiates RFB; returns after the first update request is sent.</summary>
    public async Task StartAsync(bool shared = true, CancellationToken cancellationToken = default)
    {
        if (_started) throw new InvalidOperationException("VNC client has already started.");
        _started = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, linked.Token).ConfigureAwait(false);
        _stream = _tcp.GetStream();
        await HandshakeAsync(shared, linked.Token).ConfigureAwait(false);
        _reader = ReadLoopAsync(_stop.Token);
    }

    /// <summary>Requests a full or incremental framebuffer update.</summary>
    public Task RequestUpdateAsync(bool incremental = true, CancellationToken cancellationToken = default) =>
        SendAsync(FramebufferRequest(incremental), cancellationToken);

    public Task PointerMoveAsync(double normalizedX, double normalizedY, CancellationToken cancellationToken = default)
    {
        byte buttons; lock (_inputGate) buttons = _buttons;
        return SendAsync(Pointer(buttons, Point(normalizedX, normalizedY)), cancellationToken);
    }

    public Task PointerButtonAsync(int button, bool down, double normalizedX, double normalizedY, CancellationToken cancellationToken = default)
    {
        if (button is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(button));
        byte bit = (byte)(1 << (button - 1));
        byte buttons;
        lock (_inputGate) { _buttons = down ? (byte)(_buttons | bit) : (byte)(_buttons & ~bit); buttons = _buttons; }
        return SendAsync(Pointer(buttons, Point(normalizedX, normalizedY)), cancellationToken);
    }

    public Task SendKeyAsync(uint keySym, bool down, CancellationToken cancellationToken = default)
    {
        byte[] message = new byte[8]; message[0] = 4; message[1] = down ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), keySym);
        return SendAsync(message, cancellationToken);
    }

    public async Task TypeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            uint sym = rune.Value <= 0xFF ? (uint)rune.Value : 0x01000000u | (uint)rune.Value;
            await SendKeyAsync(sym, true, cancellationToken).ConfigureAwait(false);
            await SendKeyAsync(sym, false, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => CloseAsync();

    public async ValueTask CloseAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _stop.Cancel();
        _tcp?.Close();
        if (_reader is not null) try { await _reader.ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        _stream?.Dispose(); _tcp?.Dispose();
    }

    private async Task HandshakeAsync(bool shared, CancellationToken ct)
    {
        var stream = _stream!;
        byte[] banner = await ReadExactAsync(stream, 12, ct).ConfigureAwait(false);
        string protocol = Encoding.ASCII.GetString(banner);
        if (!protocol.StartsWith("RFB 003.", StringComparison.Ordinal) || protocol[11] != '\n' ||
            !int.TryParse(protocol.AsSpan(8, 3), out int minor)) throw new IOException("Server is not speaking supported RFB.");
        int version = minor < 7 ? 3 : minor < 8 ? 7 : 8;
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"RFB 003.{version:000}\n"), ct).ConfigureAwait(false);
        byte security;
        if (version == 3)
        {
            uint type = BinaryPrimitives.ReadUInt32BigEndian(await ReadExactAsync(stream, 4, ct).ConfigureAwait(false));
            if (type == 0) throw new IOException("Server refused the connection: " + await ReadReasonAsync(stream, ct).ConfigureAwait(false));
            if (type > byte.MaxValue) throw new IOException("Unsupported security type.");
            security = (byte)type;
        }
        else
        {
            int count = (await ReadExactAsync(stream, 1, ct).ConfigureAwait(false))[0];
            if (count == 0) throw new IOException("Server refused the connection: " + await ReadReasonAsync(stream, ct).ConfigureAwait(false));
            byte[] offered = await ReadExactAsync(stream, count, ct).ConfigureAwait(false);
            if (offered.Contains((byte)1)) security = 1;
            else if (offered.Contains((byte)30) && _username.Length > 0)
            {
                if (_password.Length == 0) throw new IOException($"The Mac asks for the password of {_username}.");
                security = 30;
            }
            else if (offered.Contains((byte)2) && _password.Length > 0) security = 2;
            else if (offered.Contains((byte)30)) throw new IOException("The Mac asks for a user name and password.");
            else if (offered.Contains((byte)2)) throw new IOException("VNC password authentication requires a password.");
            else throw new IOException("Server offers no supported security type.");
            await stream.WriteAsync(new[] { security }, ct).ConfigureAwait(false);
        }

        if (security == 30)
        {
            if (_username.Length == 0) throw new IOException("The Mac asks for a user name and password.");
            if (_password.Length == 0) throw new IOException($"The Mac asks for the password of {_username}.");
            await ReadAppleAuthenticationAsync(stream, ct).ConfigureAwait(false);
        }
        else if (security == 2)
        {
            if (_password.Length == 0) throw new IOException("VNC password authentication requires a password.");
            byte[] challenge = await ReadExactAsync(stream, 16, ct).ConfigureAwait(false);
            byte[] key = new byte[8];
            byte[] passwordBytes = Encoding.UTF8.GetBytes(_password);
            passwordBytes.AsSpan(0, Math.Min(passwordBytes.Length, key.Length)).CopyTo(key);
            for (int i = 0; i < key.Length; i++) key[i] = ReverseBits(key[i]);
            using DES des = DES.Create(); des.Mode = CipherMode.ECB; des.Padding = PaddingMode.None; des.Key = key;
            using ICryptoTransform encryptor = des.CreateEncryptor();
            await stream.WriteAsync(encryptor.TransformFinalBlock(challenge, 0, challenge.Length), ct).ConfigureAwait(false);
        }
        uint result = BinaryPrimitives.ReadUInt32BigEndian(await ReadExactAsync(stream, 4, ct).ConfigureAwait(false));
        if (result != 0)
        {
            string reason = version == 8 ? await ReadReasonAsync(stream, ct).ConfigureAwait(false) : "Credentials rejected.";
            throw new IOException(reason);
        }
        await stream.WriteAsync(new[] { shared ? (byte)1 : (byte)0 }, ct).ConfigureAwait(false);
        byte[] init = await ReadExactAsync(stream, 24, ct).ConfigureAwait(false);
        _width = BinaryPrimitives.ReadUInt16BigEndian(init); _height = BinaryPrimitives.ReadUInt16BigEndian(init.AsSpan(2));
        CheckDimensions(_width, _height);
        if (init[4] != 32 || init[5] < 24 || init[7] == 0) throw new NotSupportedException("Unsupported server pixel format.");
        byte[] nameLengthBytes = init[20..24]; uint nameLength = BinaryPrimitives.ReadUInt32BigEndian(nameLengthBytes);
        if (nameLength > MaxNameBytes) throw new IOException("Server name exceeds the limit.");
        _desktopName = Encoding.UTF8.GetString(await ReadExactAsync(stream, (int)nameLength, ct).ConfigureAwait(false));
        _rgba = new byte[checked(_width * _height * 4)];
        byte[] format = [0,0,0,0,32,24,0,1,0,255,0,255,0,255,16,8,0,0,0,0];
        await stream.WriteAsync(format, ct).ConfigureAwait(false);
        int[] encodings = [0,1,2,5,-223];
        byte[] enc = new byte[4 + encodings.Length * 4]; enc[0] = 2; BinaryPrimitives.WriteUInt16BigEndian(enc.AsSpan(2), (ushort)encodings.Length);
        for (int i = 0; i < encodings.Length; i++) BinaryPrimitives.WriteInt32BigEndian(enc.AsSpan(4 + i * 4), encodings[i]);
        await stream.WriteAsync(enc, ct).ConfigureAwait(false);
        await stream.WriteAsync(FramebufferRequest(false), ct).ConfigureAwait(false);
        FrameUpdated?.Invoke(Snapshot());
    }


    private async Task ReadAppleAuthenticationAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
        int keyLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
        if (keyLength is < 1 or > 1024) throw new IOException("The server sent an unusable Apple authentication key length.");
        byte[] prime = await ReadExactAsync(stream, keyLength, ct).ConfigureAwait(false);
        byte[] serverPublicKey = await ReadExactAsync(stream, keyLength, ct).ConfigureAwait(false);
        byte[] response = VncAppleAuth.CreateResponse(header.AsSpan(0, 2), prime, serverPublicKey, _username, _password);
        await stream.WriteAsync(response, ct).ConfigureAwait(false);
    }
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                byte type = (await ReadExactAsync(_stream!, 1, ct).ConfigureAwait(false))[0];
                switch (type)
                {
                    case 0: await ReadFramebufferUpdateAsync(ct).ConfigureAwait(false); break;
                    case 1:
                        byte[] cmap = await ReadExactAsync(_stream!, 5, ct).ConfigureAwait(false);
                        await DiscardAsync(_stream!, checked(BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(3)) * 6), ct).ConfigureAwait(false); break;
                    case 2: Bell?.Invoke(); break;
                    case 3:
                        await DiscardAsync(_stream!, 3, ct).ConfigureAwait(false);
                        uint len = BinaryPrimitives.ReadUInt32BigEndian(await ReadExactAsync(_stream!, 4, ct).ConfigureAwait(false));
                        if (len > MaxClipboardBytes) throw new IOException("Clipboard payload exceeds the limit.");
                        ClipboardReceived?.Invoke(Encoding.Latin1.GetString(await ReadExactAsync(_stream!, (int)len, ct).ConfigureAwait(false))); break;
                    default: throw new IOException($"Unknown RFB server message {type}.");
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { _tcp?.Close(); Failed?.Invoke(ex); }
    }

    private async Task ReadFramebufferUpdateAsync(CancellationToken ct)
    {
        await DiscardAsync(_stream!, 1, ct).ConfigureAwait(false);
        int count = BinaryPrimitives.ReadUInt16BigEndian(await ReadExactAsync(_stream!, 2, ct).ConfigureAwait(false));
        if (count > MaxRectanglesPerUpdate) throw new IOException("Too many rectangles in framebuffer update.");
        for (int i = 0; i < count; i++)
        {
            byte[] h = await ReadExactAsync(_stream!, 12, ct).ConfigureAwait(false);
            int x = BinaryPrimitives.ReadUInt16BigEndian(h), y = BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(2));
            int w = BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(4)), ht = BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(6));
            int encoding = BinaryPrimitives.ReadInt32BigEndian(h.AsSpan(8));
            if (encoding == -223) { CheckDimensions(w, ht); _width = w; _height = ht; _rgba = new byte[checked(w * ht * 4)]; continue; }
            if (x > _width || y > _height || w > _width - x || ht > _height - y) throw new IOException("Rectangle exceeds framebuffer bounds.");
            switch (encoding)
            {
                case 0:
                    byte[] pixels = await ReadExactAsync(_stream!, checked(w * ht * 4), ct).ConfigureAwait(false);
                    for (int row = 0; row < ht; row++) for (int col = 0; col < w; col++) Put(x + col, y + row, pixels.AsSpan((row * w + col) * 4, 4));
                    break;
                case 1:
                    byte[] pos = await ReadExactAsync(_stream!, 4, ct).ConfigureAwait(false);
                    Copy(BinaryPrimitives.ReadUInt16BigEndian(pos), BinaryPrimitives.ReadUInt16BigEndian(pos.AsSpan(2)), x, y, w, ht); break;
                case 2: await DecodeRreAsync(x,y,w,ht,ct).ConfigureAwait(false); break;
                case 5: await DecodeHextileAsync(x,y,w,ht,ct).ConfigureAwait(false); break;
                default: throw new NotSupportedException($"Unsupported RFB encoding {encoding}.");
            }
        }
        FrameUpdated?.Invoke(Snapshot());
        await SendAsync(FramebufferRequest(true), ct).ConfigureAwait(false);
    }

    private async Task DecodeRreAsync(int x, int y, int w, int h, CancellationToken ct)
    {
        uint count = BinaryPrimitives.ReadUInt32BigEndian(await ReadExactAsync(_stream!, 4, ct).ConfigureAwait(false));
        if (count > (uint)Math.Min(1_000_000, checked(w * h))) throw new IOException("RRE subrectangle count exceeds bounds.");
        Fill(x,y,w,h,Pixel(await ReadExactAsync(_stream!,4,ct).ConfigureAwait(false)));
        for (int i = 0; i < count; i++)
        {
            uint color = Pixel(await ReadExactAsync(_stream!,4,ct).ConfigureAwait(false)); byte[] r = await ReadExactAsync(_stream!,8,ct).ConfigureAwait(false);
            int rx = BinaryPrimitives.ReadUInt16BigEndian(r), ry = BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(2));
            int rw = BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(4)), rh = BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(6));
            if (rx > w || ry > h || rw > w-rx || rh > h-ry) throw new IOException("RRE subrectangle exceeds bounds.");
            Fill(x+rx,y+ry,rw,rh,color);
        }
    }

    private async Task DecodeHextileAsync(int x, int y, int w, int h, CancellationToken ct)
    {
        uint? background = null; uint foreground = 0;
        for (int ty=0; ty<h; ty+=16) for (int tx=0; tx<w; tx+=16)
        {
            int tw=Math.Min(16,w-tx), th=Math.Min(16,h-ty); byte sub=(await ReadExactAsync(_stream!,1,ct).ConfigureAwait(false))[0];
            if ((sub & 1)!=0)
            {
                byte[] raw=await ReadExactAsync(_stream!,tw*th*4,ct).ConfigureAwait(false);
                for(int j=0;j<th;j++) for(int k=0;k<tw;k++) Put(x+tx+k,y+ty+j,raw.AsSpan((j*tw+k)*4,4));
                continue;
            }
            if ((sub & 2)!=0) background=Pixel(await ReadExactAsync(_stream!,4,ct).ConfigureAwait(false));
            if (background is null) throw new IOException("Hextile tile omits its background.");
            Fill(x+tx,y+ty,tw,th,background.Value);
            if ((sub & 4) != 0) foreground=Pixel(await ReadExactAsync(_stream!,4,ct).ConfigureAwait(false));
            if ((sub&8)==0) continue;
            int n=(await ReadExactAsync(_stream!,1,ct).ConfigureAwait(false))[0];
            for(int j=0;j<n;j++)
            {
                uint color=(sub&16)!=0?Pixel(await ReadExactAsync(_stream!,4,ct).ConfigureAwait(false)):foreground;
                byte[] b=await ReadExactAsync(_stream!,2,ct).ConfigureAwait(false);
                int sx=b[0]>>4, sy=b[0]&15, sw=(b[1]>>4)+1, sh=(b[1]&15)+1;
                if(sx+sw>tw||sy+sh>th) throw new IOException("Hextile subrectangle exceeds tile.");
                Fill(x+tx+sx,y+ty+sy,sw,sh,color);
            }
        }
    }

    private byte[] FramebufferRequest(bool incremental)
    {
        byte[] b=new byte[10]; b[0]=3; b[1]=incremental?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(6),(ushort)_width); BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(8),(ushort)_height); return b;
    }
    private (ushort X, ushort Y) Point(double nx,double ny) => ((ushort)Math.Clamp((int)(nx*_width),0,Math.Max(0,_width-1)),(ushort)Math.Clamp((int)(ny*_height),0,Math.Max(0,_height-1)));
    private static byte[] Pointer(byte mask,(ushort X,ushort Y) p) { byte[] b=new byte[6]; b[0]=5;b[1]=mask;BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2),p.X);BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(4),p.Y);return b; }
    private async Task SendAsync(byte[] bytes,CancellationToken ct) { if(Volatile.Read(ref _closed)!=0)throw new ObjectDisposedException(nameof(VncClient));var s=_stream??throw new InvalidOperationException("VNC client is not connected.");using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,_stop.Token);await _writeGate.WaitAsync(linked.Token).ConfigureAwait(false);try{await s.WriteAsync(bytes,linked.Token).ConfigureAwait(false);}finally{_writeGate.Release();} }
    private static async Task<byte[]> ReadExactAsync(Stream s,int n,CancellationToken ct) { if(n<0||n>64*1024*1024)throw new IOException("RFB length exceeds safety limit.");byte[] b=new byte[n];await s.ReadExactlyAsync(b,ct).ConfigureAwait(false);return b; }
    private static async Task DiscardAsync(Stream s,int n,CancellationToken ct) { byte[] b=await ReadExactAsync(s,n,ct).ConfigureAwait(false); }
    private async Task<string> ReadReasonAsync(Stream s,CancellationToken ct) { uint n=BinaryPrimitives.ReadUInt32BigEndian(await ReadExactAsync(s,4,ct).ConfigureAwait(false));if(n>MaxNameBytes)throw new IOException("Refusal reason exceeds limit.");return Encoding.UTF8.GetString(await ReadExactAsync(s,(int)n,ct).ConfigureAwait(false)); }
    private static byte ReverseBits(byte b) { b=(byte)(((b&0x55)<<1)|((b>>1)&0x55));b=(byte)(((b&0x33)<<2)|((b>>2)&0x33));return (byte)((b<<4)|(b>>4)); }
    private static uint Pixel(ReadOnlySpan<byte> b) => 0xff000000u | ((uint)b[2] << 16) | ((uint)b[1] << 8) | b[0];
    private void Put(int x,int y,ReadOnlySpan<byte> p) { int i=(y*_width+x)*4;_rgba[i]=p[2];_rgba[i+1]=p[1];_rgba[i+2]=p[0];_rgba[i+3]=255; }
    private void Fill(int x,int y,int w,int h,uint p) { byte r=(byte)(p>>16),g=(byte)(p>>8),b=(byte)p;for(int yy=y;yy<y+h;yy++)for(int xx=x;xx<x+w;xx++){int i=(yy*_width+xx)*4;_rgba[i]=r;_rgba[i+1]=g;_rgba[i+2]=b;_rgba[i+3]=255;} }
    private void Copy(int sx,int sy,int dx,int dy,int w,int h) { if(sx+w>_width||sy+h>_height)throw new IOException("CopyRect exceeds framebuffer bounds.");int start=dy>sy? h-1:0,end=dy>sy?-1:h,step=dy>sy?-1:1;for(int row=start;row!=end;row+=step){int from=((sy+row)*_width+sx)*4,to=((dy+row)*_width+dx)*4;Buffer.BlockCopy(_rgba,from,_rgba,to,w*4);} }
    private VncFrame Snapshot() => new(_width,_height,(byte[])_rgba.Clone(),_desktopName);
    private static void CheckDimensions(int w,int h) { if(w<=0||h<=0||w>MaxDimension||h>MaxDimension||(long)w*h>MaxPixels)throw new IOException("Framebuffer dimensions exceed safety limits."); }
}
