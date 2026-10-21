namespace Catussh.Core.Machine;

/// <summary>Port of <c>SSI+MemoryInfo.swift</c> — 服务器 RAM 信息（单位 kB）。</summary>
public sealed record MemoryInfo
{
    public double MemTotal { get; init; }
    public double MemFree { get; init; }
    public double MemBuffers { get; init; }
    public double MemCached { get; init; }
    public double SwapTotal { get; init; }
    public double SwapFree { get; init; }
    public double PhyUsed { get; init; }
    public double SwapUsed { get; init; }

    public MemoryInfo() { }

    public MemoryInfo(double total, double free, double buffers, double cached, double swapTotal, double swapFree)
    {
        MemTotal = total;
        MemFree = free;
        MemBuffers = buffers;
        MemCached = cached;
        SwapTotal = swapTotal;
        SwapFree = swapFree;

        // Fractions, not percentages: the views multiply. Both guards matter —
        // the parsed dictionaries return 0 for a missing key.
        PhyUsed = total != 0 ? (total - free - MemCached - MemBuffers) / total : 0;

        // Swap usage is relative to swapTotal, not physical memory, and machines
        // without swap must not divide by zero.
        SwapUsed = swapTotal != 0 ? (swapTotal - swapFree) / swapTotal : 0;
    }

    public static MemoryInfo FromMeminfo(string meminfo)
    {
        var info = ProcParsers.ParseMeminfo(meminfo);
        return new MemoryInfo(
            total: info.GetValueOrDefault("MEMTOTAL"),
            free: info.GetValueOrDefault("MEMFREE"),
            buffers: info.GetValueOrDefault("BUFFERS"),
            cached: info.GetValueOrDefault("CACHED"),
            swapTotal: info.GetValueOrDefault("SWAPTOTAL"),
            swapFree: info.GetValueOrDefault("SWAPFREE"));
    }

    public static MemoryInfo FromDarwinMemory(string darwinMemory)
    {
        var info = ProcParsers.ParseDarwinMemory(darwinMemory);
        return new MemoryInfo(
            total: info.Total,
            free: info.Free,
            buffers: 0,
            cached: info.Cached,
            swapTotal: info.SwapTotal,
            swapFree: info.SwapFree);
    }

    public override string ToString() =>
        $"""
        total: {(double.IsFinite(MemTotal) && MemTotal is >= long.MinValue and <= long.MaxValue ? (long)MemTotal : 0)} kB
        free: {(double.IsFinite(MemFree) && MemFree is >= long.MinValue and <= long.MaxValue ? (long)MemFree : 0)} kB
        buffers: {(double.IsFinite(MemBuffers) && MemBuffers is >= long.MinValue and <= long.MaxValue ? (long)MemBuffers : 0)} kB
        cached: {(double.IsFinite(MemCached) && MemCached is >= long.MinValue and <= long.MaxValue ? (long)MemCached : 0)} kB
        swap: {(double.IsFinite(SwapFree) && SwapFree is >= long.MinValue and <= long.MaxValue ? (long)SwapFree : 0)}/{(double.IsFinite(SwapTotal) && SwapTotal is >= long.MinValue and <= long.MaxValue ? (long)SwapTotal : 0)} kB
        """;
}
