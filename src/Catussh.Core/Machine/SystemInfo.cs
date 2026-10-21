using System.Globalization;

namespace Catussh.Core.Machine;

/// <summary>Port of <c>SSI+SystemInfo.swift</c> — 服务器系统信息。</summary>
public sealed record SystemInfo
{
    public string ReleaseName { get; init; } = "";
    public double UptimeSec { get; init; }
    public string Hostname { get; init; } = "";
    public int RunningProcs { get; init; }
    public int TotalProcs { get; init; }
    public float Load1 { get; init; }
    public float Load5 { get; init; }
    public float Load15 { get; init; }

    public SystemInfo() { }

    public SystemInfo(
        string release,
        double uptimeInSec,
        string hostname,
        int runningProcs,
        int totalProcs,
        float load1,
        float load5,
        float load15)
    {
        ReleaseName = release;
        UptimeSec = uptimeInSec;
        Hostname = hostname;
        RunningProcs = runningProcs;
        TotalProcs = totalProcs;
        Load1 = load1;
        Load5 = load5;
        Load15 = load15;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"""
        release: {ReleaseName}
        uptime: {UptimeSec} second
        hostname: {Hostname}
        running process: {RunningProcs} total: {TotalProcs}
        load1: {Load1} load5: {Load5} load15: {Load15}
        """);
}

/// <summary>Port of <c>ServerStatus.SystemLoadInternal</c> — intermediate loadavg reading.</summary>
public sealed record SystemLoadInternal
{
    public int RunningProcess { get; init; }
    public int TotalProcess { get; init; }
    public float Load1Avg { get; init; }
    public float Load5Avg { get; init; }
    public float Load15Avg { get; init; }
}
