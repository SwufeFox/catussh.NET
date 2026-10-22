namespace Catussh.Core.Machine;

/// <summary>
/// The session-level readings the Monitor page needs on top of
/// <see cref="ServerStatus"/> — port of the <c>public extension ServerStatus</c>
/// block in <c>MonitorSession.swift</c>.
/// </summary>
public static class MonitorExtensions
{
    /// <summary>Sum of the per-interface receive rates.</summary>
    public static int TotalReceivePerSecond(this ServerStatus status) =>
        status.Network.Elements.Sum(static e => e.RxBytesPerSec);

    public static int TotalTransmitPerSecond(this ServerStatus status) =>
        status.Network.Elements.Sum(static e => e.TxBytesPerSec);

    /// <summary>Memory readings are stored in kB on every platform.</summary>
    public static double MemoryUsedKB(this ServerStatus status)
    {
        var m = status.Memory;
        return Math.Max(0, m.MemTotal - m.MemFree - m.MemCached - m.MemBuffers);
    }

    public static double MemoryCacheKB(this ServerStatus status) =>
        Math.Max(0, status.Memory.MemCached + status.Memory.MemBuffers);

    public static double MemoryUsedPercent(this ServerStatus status)
    {
        if (status.Memory.MemTotal <= 0) return 0;
        return status.MemoryUsedKB() / status.Memory.MemTotal * 100;
    }

    /// <summary>The monitor shows the dashboard once either of these is known.</summary>
    public static bool HasData(this ServerStatus status) =>
        !string.IsNullOrEmpty(status.System.Hostname) || status.Memory.MemTotal > 0;
}

/// <summary>
/// Port of <c>MonitorHistory</c>: what the sparklines draw. One ring of 60
/// samples per series, oldest dropped first.
/// </summary>
public sealed class MonitorHistory
{
    public const int Capacity = 60;

    private readonly List<double> _cpu = [];
    private readonly List<double> _memory = [];
    private readonly List<double> _receive = [];
    private readonly List<double> _transmit = [];

    public IReadOnlyList<double> Cpu => _cpu;
    public IReadOnlyList<double> Memory => _memory;
    public IReadOnlyList<double> Receive => _receive;
    public IReadOnlyList<double> Transmit => _transmit;

    public void Record(ServerStatus status)
    {
        Push(status.Processor.Summary.SumUsed, _cpu);
        Push(status.MemoryUsedPercent(), _memory);
        Push(status.TotalReceivePerSecond(), _receive);
        Push(status.TotalTransmitPerSecond(), _transmit);
    }

    private static void Push(double value, List<double> list)
    {
        list.Add(double.IsFinite(value) ? value : 0);
        if (list.Count > Capacity) list.RemoveRange(0, list.Count - Capacity);
    }
}
