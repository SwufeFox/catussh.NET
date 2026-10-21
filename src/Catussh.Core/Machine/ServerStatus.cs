namespace Catussh.Core.Machine;

/// <summary>
/// Port of <c>Foundation/MachineStatus/Sources/MachineStatus/ServerStatusInfo.swift</c>.
///
/// Swift hangs every model off <c>public extension ServerStatus</c>; this port
/// keeps that shape with a static <see cref="Markers"/> holder plus the instance
/// model below, so each C# file maps to one Swift file.
/// </summary>
public static class ServerStatusMarkers
{
    /// <summary>
    /// The merged monitor script echoes this marker on its own line ahead of each
    /// section; <see cref="ProcParsers.SplitMonitorSections"/> only opens a section
    /// on a full-line match, which kernel /proc payloads, df rows and os-release
    /// assignments can never produce. That is what the old "[*******]" token could
    /// not promise inside two samples of one file.
    /// </summary>
    public const string SectionMarkerPrefix = "<<<CATUSSH-SECTION:";

    public const string SectionMarkerSuffix = ">>>";

    /// <summary>
    /// <c>nvidia-smi</c> stays a command of its own: it is slow and missing on most
    /// machines, and its failure must not cost the other sections.
    /// </summary>
    public const string GraphicsScript = "nvidia-smi -q -x | tr -d \"[\\t\\n]\" || echo \"no nvidia gpu available\"";
}

/// <summary>
/// The monitor model: one snapshot of a remote machine.
///
/// The Swift class is an <c>ObservableObject</c> that publishes one batch per
/// refresh; here a refresh returns an immutable snapshot instead, which is the
/// same "one update, not six" contract expressed as a value. UI layers swap the
/// whole snapshot in.
/// </summary>
public sealed record ServerStatus
{
    /// <summary>Not split per property on purpose: a refresh replaces every group in one batch.</summary>
    public ProcessorInfo Processor { get; init; } = new();

    public FileSystemInfo FileSystem { get; init; } = new();

    public MemoryInfo Memory { get; init; } = new();

    public SystemInfo System { get; init; } = new();

    public NetworkInfo Network { get; init; } = new();

    /// <summary>not all server have gpu, bro.</summary>
    public GraphicsInfo? Graphics { get; init; }

    /// <summary>
    /// Hostname and os-release cannot change while a connection stays up, so they are
    /// read once and reused until the monitor reports a reconnect via
    /// <see cref="ResetSessionStatics"/>.
    /// </summary>
    public string? SessionHostname { get; init; }

    public string? SessionReleaseName { get; init; }

    /// <summary>Drops the once-per-connection static readings so the next refresh pulls them again.</summary>
    public ServerStatus ResetSessionStatics() => this with { SessionHostname = null, SessionReleaseName = null };

    /// <summary>
    /// Turns one merged monitor payload plus one GPU payload into a snapshot —
    /// the parsing half of the Swift <c>requestInfoAndWait</c>, with the two SSH
    /// channels handed in already executed.
    /// </summary>
    /// <param name="mergedOutput">Output of <see cref="MonitorScript.Build"/>.</param>
    /// <param name="graphics">Result of <see cref="ServerStatusMarkers.GraphicsScript"/> (may be "").</param>
    /// <param name="includeStatic">True on the first round of a connection (or after a reset).</param>
    public ServerStatus Apply(string mergedOutput, string graphics, bool includeStatic)
    {
        var sections = ProcParsers.SplitMonitorSections(mergedOutput);
        var isDarwin = sections.TryGetValue("platform", out var platform)
                       && ProcParsers.TrimWhitespaceAndNewlines(platform) == "Darwin";

        var hostname = SessionHostname;
        var releaseName = SessionReleaseName;

        if (includeStatic)
        {
            // Only a non-empty reading is cached; a failed probe is retried on the
            // next round instead of sticking a placeholder.
            var readHostname = sections.GetValueOrDefault("hostname", "").Replace("\n", "");
            var releasePayload = ProcParsers.TrimWhitespaceAndNewlines(sections.GetValueOrDefault("os-release", ""));
            if (readHostname.Length > 0) hostname = readHostname;
            if (releasePayload.Length > 0)
            {
                releaseName = isDarwin
                    ? ProcParsers.ParseDarwinReleaseName(releasePayload)
                    : ProcParsers.ParseOsReleaseName(releasePayload);
            }
        }

        ProcessorInfo processor;
        NetworkInfo network;
        MemoryInfo memory;
        SystemLoadInternal load;
        double uptime;

        if (isDarwin)
        {
            processor = ProcessorInfo.FromDarwinTop(sections.GetValueOrDefault("darwin-top")) ?? new ProcessorInfo();
            network = NetworkInfo.FromNetstat(sections.GetValueOrDefault("net-a"), sections.GetValueOrDefault("net-b")) ?? new NetworkInfo();
            memory = MemoryInfo.FromDarwinMemory(sections.GetValueOrDefault("mem", ""));
            load = ProcParsers.ParseDarwinTopLoad(sections.GetValueOrDefault("darwin-top", ""));
            uptime = ProcParsers.ParseDarwinUptime(sections.GetValueOrDefault("uptime", ""));
        }
        else
        {
            processor = ProcessorInfo.FromProcStat(sections.GetValueOrDefault("cpu-a"), sections.GetValueOrDefault("cpu-b")) ?? new ProcessorInfo();
            network = NetworkInfo.FromNetDev(sections.GetValueOrDefault("net-a"), sections.GetValueOrDefault("net-b")) ?? new NetworkInfo();
            memory = MemoryInfo.FromMeminfo(sections.GetValueOrDefault("mem", ""));
            load = ProcParsers.ParseLoadavg(sections.GetValueOrDefault("loadavg", ""));
            uptime = ProcParsers.ParseUptime(sections.GetValueOrDefault("uptime", ""));
        }

        var fileSystem = FileSystemInfo.FromDfPk(sections.GetValueOrDefault("df", ""));
        var system = new SystemInfo(
            release: releaseName ?? "",
            uptimeInSec: uptime,
            hostname: hostname ?? "",
            runningProcs: load.RunningProcess,
            totalProcs: load.TotalProcess,
            load1: load.Load1Avg,
            load5: load.Load5Avg,
            load15: load.Load15Avg);

        return new ServerStatus
        {
            Processor = processor,
            FileSystem = fileSystem,
            Memory = memory,
            System = system,
            Network = network,
            Graphics = GraphicsParser.FromNvidiaSmiXml(graphics),
            SessionHostname = hostname,
            SessionReleaseName = releaseName,
        };
    }
}
