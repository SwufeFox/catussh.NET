using Catussh.Core.Machine;
using System.Text.Json;

namespace Catussh.App;

/// <summary>
/// The JSON the Monitor page consumes.
///
/// C# records would serialize with <c>init</c>-only properties fine, but the page
/// wants a flat, stable shape with the session-level totals already computed, so
/// the projection lives here instead of being scattered through the host. Keeping
/// it in one place also lets <c>--dump-snapshot</c> write the exact same document
/// that the WebView2 bridge serves.
/// </summary>
internal static class MonitorPayload
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <param name="address">Demo target for the Facts row; null renders as "—".</param>
    public static string Build(ServerStatus status, MonitorHistory history, string? address = null) =>
        JsonSerializer.Serialize(new
        {
            status = new
            {
                address = address ?? "",
                processor = new
                {
                    summary = new
                    {
                        name = status.Processor.Summary.Name,
                        sumSystem = status.Processor.Summary.SumSystem,
                        sumUser = status.Processor.Summary.SumUser,
                        sumIOWait = status.Processor.Summary.SumIOWait,
                        sumNice = status.Processor.Summary.SumNice,
                        sumUsed = status.Processor.Summary.SumUsed,
                    },
                    cores = status.Processor.Cores.Select(c => new
                    {
                        name = c.Name,
                        sumSystem = c.SumSystem,
                        sumUser = c.SumUser,
                        sumIOWait = c.SumIOWait,
                        sumNice = c.SumNice,
                        sumUsed = c.SumUsed,
                    }),
                },
                memory = new
                {
                    memTotal = status.Memory.MemTotal,
                    memFree = status.Memory.MemFree,
                    memBuffers = status.Memory.MemBuffers,
                    memCached = status.Memory.MemCached,
                    swapTotal = status.Memory.SwapTotal,
                    swapFree = status.Memory.SwapFree,
                },
                fileSystem = new
                {
                    elements = status.FileSystem.Elements.Select(d => new
                    {
                        mountPoint = d.MountPoint,
                        size = d.Size,
                        used = d.Used,
                        free = d.Free,
                        percent = d.Percent,
                    }),
                },
                system = new
                {
                    releaseName = status.System.ReleaseName,
                    uptimeSec = status.System.UptimeSec,
                    hostname = status.System.Hostname,
                    runningProcs = status.System.RunningProcs,
                    totalProcs = status.System.TotalProcs,
                    load1 = status.System.Load1,
                    load5 = status.System.Load5,
                    load15 = status.System.Load15,
                },
                network = new
                {
                    elements = status.Network.Elements.Select(n => new
                    {
                        device = n.Device,
                        rxBytesPerSec = n.RxBytesPerSec,
                        txBytesPerSec = n.TxBytesPerSec,
                    }),
                },
                graphics = status.Graphics is { } g
                    ? new
                    {
                        version = g.Version,
                        units = g.Units.Select(u => new
                        {
                            uuid = u.Uuid,
                            name = u.Name,
                            memory = new { total = u.Memory.Total, used = u.Memory.Used, free = u.Memory.Free },
                            vbios_version = u.VbiosVersion,
                            fan_speed = u.FanSpeed,
                            utilization = new
                            {
                                gpu_util = u.Utilization.GpuUtil,
                                memory_util = u.Utilization.MemoryUtil,
                                encoder_util = u.Utilization.EncoderUtil,
                                decoder_util = u.Utilization.DecoderUtil,
                            },
                        }),
                    }
                    : null,
            },
            totals = new
            {
                receivePerSecond = status.TotalReceivePerSecond(),
                transmitPerSecond = status.TotalTransmitPerSecond(),
                memoryUsedKB = status.MemoryUsedKB(),
                memoryCacheKB = status.MemoryCacheKB(),
                memoryUsedPercent = status.MemoryUsedPercent(),
            },
            history = new
            {
                receive = history.Receive,
                transmit = history.Transmit,
                cpu = history.Cpu,
                memory = history.Memory,
            },
        }, Options);
}
