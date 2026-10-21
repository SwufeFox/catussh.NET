namespace Catussh.Core.Machine;

/// <summary>Port of <c>SSI+NetworkInfo.swift</c> — 服务器网络信息（每秒字节数）。</summary>
public sealed record NetworkInfo
{
    public IReadOnlyList<NetworkInfoElement> Elements { get; init; } = Array.Empty<NetworkInfoElement>();

    public NetworkInfo() { }

    public NetworkInfo(IReadOnlyList<NetworkInfoElement> elements) => Elements = elements;

    /// <summary>
    /// Builds from two <c>/proc/net/dev</c> payloads sampled about a second apart;
    /// rates are the counter deltas, so the sampling gap is exactly the script's sleep.
    /// </summary>
    public static NetworkInfo? FromNetDev(string? netDevBefore, string? netDevAfter)
    {
        if (netDevBefore is null || netDevAfter is null) return null;

        var getPriv = Build(netDevBefore);
        var getCurr = Build(netDevAfter);
        var result = new List<NetworkInfoElement>();
        foreach (var (device, previous) in getPriv)
        {
            if (!getCurr.TryGetValue(device, out var target)) continue;
            var rxIncrease = target.Rx - previous.Rx;
            var txIncrease = target.Tx - previous.Tx;
            if (rxIncrease < 0 || txIncrease < 0) continue;
            result.Add(new NetworkInfoElement(device, rxIncrease, txIncrease));
        }
        result.Sort(static (a, b) => string.CompareOrdinal(a.Device, b.Device));
        return new NetworkInfo(result);
    }

    /// <summary>
    /// Builds from two macOS <c>netstat -ibn</c> samples. Only link-level rows are
    /// used so each interface appears once, without its IPv4 and IPv6 aliases
    /// duplicating the same counters.
    /// </summary>
    public static NetworkInfo? FromNetstat(string? netstatBefore, string? netstatAfter)
    {
        if (netstatBefore is null || netstatAfter is null) return null;
        var before = ProcParsers.ParseDarwinNetstat(netstatBefore);
        var after = ProcParsers.ParseDarwinNetstat(netstatAfter);
        var result = new List<NetworkInfoElement>();
        foreach (var (device, previous) in before)
        {
            if (!after.TryGetValue(device, out var current)) continue;
            var rxIncrease = current.Rx - previous.Rx;
            var txIncrease = current.Tx - previous.Tx;
            if (rxIncrease < 0 || txIncrease < 0) continue;
            result.Add(new NetworkInfoElement(device, rxIncrease, txIncrease));
        }
        result.Sort(static (a, b) => string.CompareOrdinal(a.Device, b.Device));
        return new NetworkInfo(result);
    }

    private static Dictionary<string, (int Rx, int Tx)> Build(string text)
    {
        var result = new Dictionary<string, (int Rx, int Tx)>();
        foreach (var item in text.Split('\n'))
        {
            if (!item.Contains(':')) continue;
            var sepName = item.Split(':');
            if (sepName.Length != 2) continue;
            var key = sepName[0].Trim();
            var split = ProcParsers.SplitWhitespaceRuns(sepName[1]);
            if (split.Count < 10) continue;

            // 0     1       2    3    4    5     6          7
            // bytes packets errs drop fifo frame compressed multicast
            // 8     9       10   11   12   13    14      15
            // bytes packets errs drop fifo colls carrier compressed
            if (!int.TryParse(split[0], out var rxBytes)) continue;
            if (!int.TryParse(split[8], out var txBytes)) continue;

            // A duplicate interface name means the counter line is ambiguous,
            // so neither sample trusts it.
            if (result.ContainsKey(key))
            {
                result.Remove(key);
                continue;
            }
            result[key] = (rxBytes, txBytes);
        }
        return result;
    }

    public sealed record NetworkInfoElement(string Device, int RxBytesPerSec, int TxBytesPerSec)
    {
        public string Id => Device;

        public override string ToString() => $"""
            interface: {Device}
            rxBytes: {RxBytesPerSec}/s txBytes: {TxBytesPerSec}/s
            """;
    }
}
