using System.Globalization;

namespace Catussh.Core.Machine;

/// <summary>
/// Port of <c>DarwinParsers.swift</c>: the macOS half of the monitor probe
/// (<c>top -l 2</c>, <c>sysctl</c>, <c>vm_stat</c>, <c>netstat -ibn</c>, <c>sw_vers</c>).
/// </summary>
public static class DarwinParsers
{
    private static string? LastLine(string raw, string prefix) =>
        raw.Split('\n')
            .Select(ProcParsers.TrimWhitespaceAndNewlines)
            .LastOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

    // Named ParseDarwin* (rather than plain Parse*) so the thin wrappers on
    // ProcParsers — where the Swift originals lived — can forward without a clash.

    public static ProcessPercentInfo? ParseDarwinCPU(string raw)
    {
        var line = LastLine(raw, "CPU usage:");
        if (line is null) return null;

        var percentages = new Dictionary<string, float>();
        foreach (var component in line["CPU usage:".Length..].Split(','))
        {
            var fields = ProcParsers.SplitWhitespaceRuns(component);
            if (fields.Count != 2) continue;
            if (!fields[0].EndsWith('%')) continue;
            if (!float.TryParse(fields[0].AsSpan(0, fields[0].Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            percentages[fields[1]] = value;
        }

        if (!percentages.TryGetValue("user", out var user)) return null;
        if (!percentages.TryGetValue("sys", out var system)) return null;
        if (!percentages.TryGetValue("idle", out var idle)) return null;

        return new ProcessPercentInfo(system: system, user: user, iowait: 0, nice: 0, sum: Math.Max(0, 100 - idle));
    }

    public static SystemLoadInternal ParseDarwinTopLoad(string raw)
    {
        var loadLine = LastLine(raw, "Load Avg:");
        var processesLine = LastLine(raw, "Processes:");
        if (loadLine is null || processesLine is null) return new SystemLoadInternal();

        var loads = ProcParsers.SplitWhitespaceRuns(loadLine["Load Avg:".Length..]);
        var processes = ProcParsers.SplitWhitespaceRuns(processesLine);

        // top pads the load averages with a trailing comma: "Load Avg: 1.20, 1.35, 1.44"
        if (loads.Count != 3) return new SystemLoadInternal();
        if (!float.TryParse(loads[0].Trim(','), NumberStyles.Float, CultureInfo.InvariantCulture, out var load1)) return new SystemLoadInternal();
        if (!float.TryParse(loads[1].Trim(','), NumberStyles.Float, CultureInfo.InvariantCulture, out var load5)) return new SystemLoadInternal();
        if (!float.TryParse(loads[2].Trim(','), NumberStyles.Float, CultureInfo.InvariantCulture, out var load15)) return new SystemLoadInternal();
        if (processes.Count < 5) return new SystemLoadInternal();
        if (!int.TryParse(processes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)) return new SystemLoadInternal();
        if (!int.TryParse(processes[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var running)) return new SystemLoadInternal();

        return new SystemLoadInternal
        {
            Load1Avg = load1,
            Load5Avg = load5,
            Load15Avg = load15,
            RunningProcess = running,
            TotalProcess = total,
        };
    }

    /// <summary>
    /// The Darwin uptime payload is <c>date +%s</c> followed by
    /// <c>sysctl -n kern.boottime</c> ("{ sec = 1700000000, usec = 0 } ...").
    /// </summary>
    public static double ParseDarwinUptime(string raw)
    {
        var fields = ProcParsers.SplitWhitespaceRuns(raw);
        if (fields.Count == 0) return 0;
        if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var now)) return 0;

        var secIndex = fields.IndexOf("sec");
        if (secIndex < 0 || secIndex + 2 >= fields.Count) return 0;
        if (fields[secIndex + 1] != "=") return 0;
        if (!double.TryParse(fields[secIndex + 2].Trim(','), NumberStyles.Float, CultureInfo.InvariantCulture, out var boot)) return 0;

        return Math.Max(0, now - boot);
    }

    public static string ParseDarwinReleaseName(string raw)
    {
        string? productName = null;
        string? productVersion = null;
        foreach (var line in raw.Split('\n'))
        {
            var fields = line.Split(':', 2);
            if (fields.Length != 2) continue;
            var key = fields[0].Trim();
            var value = fields[1].Trim();
            if (key == "ProductName") productName = value;
            if (key == "ProductVersion") productVersion = value;
        }
        if (productName is not null && productVersion is not null) return $"{productName} {productVersion}";
        return productName ?? "macOS";
    }

    private static double ParseDarwinKilobytes(string token)
    {
        var text = ProcParsers.TrimWhitespaceAndNewlines(token);
        if (text.Length == 0) return 0;
        var unit = text[^1];
        var scales = new Dictionary<string, double>
        {
            ["K"] = 1.0,
            ["M"] = 1024.0,
            ["G"] = 1_048_576.0,
            ["T"] = 1_073_741_824.0,
        };
        if (scales.TryGetValue(char.ToUpperInvariant(unit).ToString(), out var scale) &&
            double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value * scale;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) ? plain : 0;
    }

    public sealed record DarwinMemoryReading(
        double Total,
        double Free,
        double Cached,
        double SwapTotal,
        double SwapFree);

    /// <summary>
    /// Combines <c>sysctl hw.memsize hw.pagesize vm.swapusage</c> with
    /// <c>vm_stat</c>. Free memory counts speculative pages; cached memory is
    /// file-backed pages minus the speculative ones (they are already counted free).
    /// </summary>
    public static DarwinMemoryReading ParseDarwinMemory(string raw)
    {
        var total = 0.0;
        var pageSize = 0.0;
        var pages = new Dictionary<string, double>();
        var swapTotal = 0.0;
        var swapFree = 0.0;

        foreach (var line in raw.Split('\n'))
        {
            var fields = line.Split(':', 2);
            if (fields.Length != 2) continue;
            var key = fields[0].Trim();
            var value = fields[1].Trim();
            switch (key)
            {
                case "hw.memsize":
                    total = ParseOrZero(value) / 1024;
                    break;
                case "hw.pagesize":
                    pageSize = ParseOrZero(value);
                    break;
                case "vm.swapusage":
                    var swapFields = ProcParsers.SplitWhitespaceRuns(value);
                    var totalIndex = swapFields.IndexOf("total");
                    if (totalIndex >= 0 && totalIndex + 2 < swapFields.Count)
                        swapTotal = ParseDarwinKilobytes(swapFields[totalIndex + 2]);
                    var freeIndex = swapFields.IndexOf("free");
                    if (freeIndex >= 0 && freeIndex + 2 < swapFields.Count)
                        swapFree = ParseDarwinKilobytes(swapFields[freeIndex + 2]);
                    break;
                case "Pages free":
                case "Pages speculative":
                case "File-backed pages":
                    // vm_stat pads counts with a trailing period: "Pages free:  12345."
                    pages[key] = ParseOrZero(value.Trim('.'));
                    break;
                default:
                    continue;
            }
        }

        // Older macOS prints the page size only in vm_stat's header
        // ("Mach Virtual Memory Statistics: (page size of 16384 bytes)").
        if (pageSize == 0)
        {
            var header = raw.Split('\n').FirstOrDefault(line => line.Contains("page size of"));
            if (header is not null)
            {
                var fields = ProcParsers.SplitWhitespaceRuns(header);
                var index = fields.IndexOf("of");
                if (index >= 0 && index + 1 < fields.Count)
                    pageSize = ParseOrZero(fields[index + 1]);
            }
        }

        var freePages = pages.GetValueOrDefault("Pages free") + pages.GetValueOrDefault("Pages speculative");
        var cachedPages = Math.Max(0, pages.GetValueOrDefault("File-backed pages") - pages.GetValueOrDefault("Pages speculative"));
        var kilobytesPerPage = pageSize / 1024;

        return new DarwinMemoryReading(
            Total: total,
            Free: freePages * kilobytesPerPage,
            Cached: cachedPages * kilobytesPerPage,
            SwapTotal: swapTotal,
            SwapFree: swapFree);

        static double ParseOrZero(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    /// <summary>
    /// Parses <c>netstat -ibn</c>. Only rows whose address is a <c>&lt;Link#N&gt;</c>
    /// marker carry interface totals; the IPv4/IPv6 rows repeat the counters and are
    /// skipped so each device appears once.
    /// </summary>
    public static Dictionary<string, (int Rx, int Tx)> ParseDarwinNetstat(string raw)
    {
        var result = new Dictionary<string, (int Rx, int Tx)>();
        foreach (var line in raw.Split('\n'))
        {
            var fields = ProcParsers.SplitWhitespaceRuns(line);
            if (fields.Count < 10) continue;
            if (!fields[2].StartsWith("<Link#", StringComparison.Ordinal)) continue;
            if (!int.TryParse(fields[^5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx)) continue;
            if (!int.TryParse(fields[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tx)) continue;

            var device = fields[0];
            if (device.EndsWith('*')) device = device[..^1];
            result[device] = (rx, tx);
        }
        return result;
    }
}
