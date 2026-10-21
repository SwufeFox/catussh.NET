namespace Catussh.Core.Machine;

/// <summary>Port of <c>SSI+ProcessorInfo.swift</c> — 服务器 CPU 信息（全部是百分比）。</summary>
public sealed record ProcessorInfo
{
    public ProcessPercentInfo Summary { get; init; } = new();

    /// <summary>Per-core rows, sorted cpu0, cpu1, … cpu10 (numeric, not lexical).</summary>
    public IReadOnlyList<ProcessPercentInfo> Cores { get; init; } = Array.Empty<ProcessPercentInfo>();

    public ProcessorInfo() { }

    public ProcessorInfo(ProcessPercentInfo summary, IReadOnlyDictionary<string, ProcessPercentInfo> cores)
    {
        Summary = summary;
        var keys = cores.Keys.OrderBy(key => key, CoreNameComparer.Instance);
        var build = new List<ProcessPercentInfo>(cores.Count);
        foreach (var key in keys)
        {
            build.Add(cores[key] with { Name = key });
        }
        Cores = build;
    }

    /// <summary>
    /// Builds from two <c>/proc/stat</c> payloads sampled about a second apart;
    /// null when either side is missing the aggregate cpu row.
    /// </summary>
    public static ProcessorInfo? FromProcStat(string? statBefore, string? statAfter)
    {
        if (statBefore is null || statAfter is null) return null;

        var resultPriv = ProcParsers.ParseProcStat(statBefore);
        var resultCurr = ProcParsers.ParseProcStat(statAfter);

        if (resultPriv.Summary is not { } privSum || resultCurr.Summary is not { } currSum) return null;

        var sum = ProcParsers.CalculatePercent(privSum, currSum);
        var perCore = new Dictionary<string, ProcessPercentInfo>();
        foreach (var (key, priv) in resultPriv.Cores)
        {
            if (!resultCurr.Cores.TryGetValue(key, out var curr)) continue;
            perCore[key] = ProcParsers.CalculatePercent(priv, curr);
        }
        return new ProcessorInfo(sum, perCore);
    }

    /// <summary>
    /// Builds from macOS <c>top -l 2</c>; the last sample contains the interval CPU
    /// percentages. Darwin does not expose per-core samples through top, so the
    /// core list stays empty.
    /// </summary>
    public static ProcessorInfo? FromDarwinTop(string? darwinTop)
    {
        if (darwinTop is null) return null;
        var summary = ProcParsers.ParseDarwinCPU(darwinTop);
        if (summary is null) return null;
        return new ProcessorInfo(summary, new Dictionary<string, ProcessPercentInfo>());
    }

    /// <summary>Mirrors the Swift memberwise <c>init(summary:cores:)</c>. Alias kept for symmetry with the source.</summary>
    public static ProcessorInfo Create(ProcessPercentInfo summary, IReadOnlyDictionary<string, ProcessPercentInfo> cores) =>
        new(summary, cores);

    private sealed class CoreNameComparer : IComparer<string>
    {
        public static readonly CoreNameComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            a ??= "";
            b ??= "";
            if (a.Length < 3 || b.Length < 3) return string.CompareOrdinal(a, b);
            if (int.TryParse(a.AsSpan(3), out var droppedA) && int.TryParse(b.AsSpan(3), out var droppedB))
                return droppedA.CompareTo(droppedB);
            return string.CompareOrdinal(a, b);
        }
    }
}

/// <summary>
/// Port of <c>ServerStatus.ProcessPercentInfo</c> — percentages between two samples.
/// <see cref="Name"/> doubles as the Swift <c>id</c>: the kernel names cores cpu0…cpuN.
/// </summary>
public sealed record ProcessPercentInfo
{
    public string Name { get; init; } = "";
    public float SumSystem { get; init; }
    public float SumUser { get; init; }
    public float SumIOWait { get; init; }
    public float SumNice { get; init; }
    public float SumUsed { get; init; }

    public ProcessPercentInfo() { }

    public ProcessPercentInfo(float system, float user, float iowait, float nice, float sum)
    {
        SumSystem = system;
        SumUser = user;
        SumIOWait = iowait;
        SumNice = nice;
        SumUsed = sum;
    }

    public override string ToString() =>
        $"system: {SumSystem}, user: {SumUser}, iowait: {SumIOWait}, nice: {SumNice}";
}

/// <summary>Port of <c>ServerStatus.ProcessInfoElement</c> — one raw /proc/stat row.</summary>
public sealed record ProcessInfoElement(
    float User,
    float Nice,
    float System,
    float Idle,
    float IOwait,
    float Irq,
    float SoftIrq,
    float Steal,
    float Guest)
{
    public ProcessInfoElement() : this(0, 0, 0, 0, 0, 0, 0, 0, 0) { }
}
