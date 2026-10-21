using System.Globalization;

namespace Catussh.Core.Machine;

/// <summary>
/// Port of <c>ProcParsers.swift</c>: pure parsers for Linux /proc and os-release
/// payloads, extracted from the remote initializers so they can be tested offline
/// with fixtures.
/// </summary>
public static class ProcParsers
{
    /// <summary>
    /// Splits on runs of whitespace: /proc files and tool outputs pad columns with
    /// variable-width spaces. Swift's <c>String.split(whereSeparator: \.isWhitespace)</c>
    /// also drops empty pieces, which this reproduces.
    /// </summary>
    public static List<string> SplitWhitespaceRuns(string line)
    {
        var result = new List<string>();
        var start = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                if (start >= 0)
                {
                    result.Add(line.Substring(start, i - start));
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }
        if (start >= 0) result.Add(line.Substring(start));
        return result;
    }

    // MARK: - /proc/meminfo

    /// <summary>Parses <c>/proc/meminfo</c> content into a keyed dictionary (values in kB).</summary>
    public static Dictionary<string, double> ParseMeminfo(string raw)
    {
        var info = new Dictionary<string, double>();
        foreach (var line in raw.Split('\n'))
        {
            if (line.Length == 0) continue;
            var cut = SplitWhitespaceRuns(line.Replace(":", ""));
            if (cut.Count != 3) continue;
            if (!cut[2].ToUpperInvariant().Equals("KB", StringComparison.Ordinal)) continue;
            info[cut[0].ToUpperInvariant()] = ParseDouble(cut[1]);
        }
        return info;
    }

    // MARK: - /proc/stat

    /// <summary>
    /// Parses one <c>/proc/stat</c> sample into the aggregate "cpu" row and
    /// per-core rows.
    /// </summary>
    public static (ProcessInfoElement? Summary, Dictionary<string, ProcessInfoElement> Cores) ParseProcStat(string raw)
    {
        var result = new Dictionary<string, ProcessInfoElement>();
        ProcessInfoElement? summary = null;

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = TrimWhitespaceAndNewlines(rawLine);
            if (!line.StartsWith("cpu", StringComparison.Ordinal)) continue;
            var lineElement = SplitWhitespaceRuns(line);

            // The kernel prints ten counters; a line with more or fewer is not one
            // this parser understands (newer kernels append fields).
            if (lineElement.Count != 11) continue;

            var key = lineElement[0];
            // Column order is the kernel's: user nice system idle iowait irq
            // softirq steal guest (the 11th field, guest_nice, is not modelled).
            var element = new ProcessInfoElement(
                ParseFloat(lineElement[1]),
                ParseFloat(lineElement[2]),
                ParseFloat(lineElement[3]),
                ParseFloat(lineElement[4]),
                ParseFloat(lineElement[5]),
                ParseFloat(lineElement[6]),
                ParseFloat(lineElement[7]),
                ParseFloat(lineElement[8]),
                ParseFloat(lineElement[9]));

            if (key == "cpu") summary = element;
            else result[key] = element;
        }
        return (summary, result);
    }

    /// <summary>
    /// Computes usage percentages between two /proc/stat samples. Returns zeros
    /// when counters did not advance (idle machine), rather than dividing by zero
    /// and feeding NaN into the UI.
    /// </summary>
    public static ProcessPercentInfo CalculatePercent(ProcessInfoElement priv, ProcessInfoElement curr)
    {
        var preAll = priv.User + priv.Nice + priv.System + priv.Idle + priv.IOwait + priv.Irq + priv.SoftIrq + priv.Steal + priv.Guest;
        var nowAll = curr.User + curr.Nice + curr.System + curr.Idle + curr.IOwait + curr.Irq + curr.SoftIrq + curr.Steal + curr.Guest;

        var total = nowAll - preAll;
        if (!(total > 0)) return new ProcessPercentInfo();

        var privUsedTotal = priv.User + priv.Nice + priv.System + priv.IOwait;
        var currUsedTotal = curr.User + curr.Nice + curr.System + curr.IOwait;

        return new ProcessPercentInfo(
            system: (curr.System - priv.System) / total * 100,
            user: (curr.User - priv.User) / total * 100,
            iowait: (curr.IOwait - priv.IOwait) / total * 100,
            nice: (curr.Nice - priv.Nice) / total * 100,
            sum: (currUsedTotal - privUsedTotal) / total * 100);
    }

    // MARK: - merged monitor script

    /// <summary>
    /// Splits the merged monitor script's output into named sections. The script
    /// echoes a marker line ahead of each command's payload and only a full-line
    /// match opens a section; kernel /proc payloads, df rows and os-release
    /// assignments can never consist of just the marker, so sections cannot collide
    /// with remote content the way a bare token like "[*******]" could sit inside
    /// one payload.
    /// </summary>
    public static Dictionary<string, string> SplitMonitorSections(string raw)
    {
        var sections = new Dictionary<string, string>();
        string? current = null;
        foreach (var line in raw.Split('\n'))
        {
            // whitespacesAndNewlines also eats a stray \r: the channel is not a pty
            // so markers end in \n, but a payload that slipped a CR in must not make
            // a marker unrecognizable.
            var trimmed = TrimWhitespaceAndNewlines(line);
            if (trimmed.StartsWith(ServerStatusMarkers.SectionMarkerPrefix, StringComparison.Ordinal) &&
                trimmed.EndsWith(ServerStatusMarkers.SectionMarkerSuffix, StringComparison.Ordinal))
            {
                var name = trimmed[
                    ServerStatusMarkers.SectionMarkerPrefix.Length..
                    ^ServerStatusMarkers.SectionMarkerSuffix.Length];
                current = name;
                sections[name] = "";
                continue;
            }

            if (current is not null)
            {
                sections[current] = sections.GetValueOrDefault(current, "") + line + "\n";
            }
        }
        return sections;
    }

    // MARK: - /proc/uptime

    /// <summary>Parses the first field of <c>/proc/uptime</c> ("773229.50 1531748.20").</summary>
    public static double ParseUptime(string intake)
    {
        var fields = SplitWhitespaceRuns(intake);
        return fields.Count > 0 ? ParseDouble(fields[0]) : 0;
    }

    // MARK: - /etc/os-release

    public static string StripSurroundingQuotes(string str)
    {
        if (str.Length <= 2) return str;
        var doubleQuoted = str.StartsWith('"') && str.EndsWith('"');
        var singleQuoted = str.StartsWith('\'') && str.EndsWith('\'');
        if (!doubleQuoted && !singleQuoted) return str;
        return str[1..^1];
    }

    /// <summary>
    /// Extracts a display name from /etc/os-release content, preferring PRETTY_NAME
    /// over NAME, unquoting the value.
    /// </summary>
    public static string ParseOsReleaseName(string intake)
    {
        string? pretty = null;
        string? name = null;
        foreach (var item in intake.Split('\n'))
        {
            if (item.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
            {
                pretty = item["PRETTY_NAME=".Length..];
                break;
            }
            if (item.StartsWith("NAME=", StringComparison.Ordinal))
            {
                name = item["NAME=".Length..];
            }
        }
        if (pretty is not null) return StripSurroundingQuotes(pretty);
        if (name is not null) return StripSurroundingQuotes(name);
        return "Generic Linux";
    }

    // MARK: - /proc/loadavg

    /// <summary>
    /// Parses <c>/proc/loadavg</c> ("0.27 0.34 0.39 2/916 23817") into load averages
    /// and process counters. Any malformed field degrades the whole reading to
    /// zeros so a card never renders half a number.
    /// </summary>
    public static SystemLoadInternal ParseLoadavg(string intake)
    {
        var cut = SplitWhitespaceRuns(intake);
        if (cut.Count != 5) return new SystemLoadInternal();

        var load1 = ParseFloatOrInfinity(cut[0]);
        if (load1 is null || float.IsInfinity(load1.Value)) return new SystemLoadInternal();
        var load5 = ParseFloatOrInfinity(cut[1]);
        if (load5 is null || float.IsInfinity(load5.Value)) return new SystemLoadInternal();
        var load15 = ParseFloatOrInfinity(cut[2]);
        if (load15 is null || float.IsInfinity(load15.Value)) return new SystemLoadInternal();

        var process = cut[3].Split('/');
        if (process.Length != 2) return new SystemLoadInternal();
        if (!int.TryParse(process[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var running)) return new SystemLoadInternal();
        if (!int.TryParse(process[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)) return new SystemLoadInternal();

        return new SystemLoadInternal
        {
            Load1Avg = load1.Value,
            Load5Avg = load5.Value,
            Load15Avg = load15.Value,
            RunningProcess = running,
            TotalProcess = total,
        };
    }

    // MARK: - df -Pk

    /// <summary>
    /// Formats a 1024-block count the way <c>df -h</c> would ("935M", "98G", "1.8T"),
    /// so the model keeps serving display-ready strings.
    /// </summary>
    public static string FormatKilobytes(int kilobytes)
    {
        string[] units = ["K", "M", "G", "T", "P"];
        var value = (double)kilobytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        if (index == 0 || value >= 10)
        {
            return ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + units[index];
        }
        return value.ToString("0.0", CultureInfo.InvariantCulture) + units[index];
    }

    /// <summary>
    /// Parses <c>df -Pk</c> output (POSIX: one line per filesystem, sizes in
    /// 1024-blocks) into display-ready elements. Unlike <c>df -h</c>, -P never wraps
    /// a long device name onto its own line, and the mount point is the trailing
    /// columns, so spaces in it survive.
    /// </summary>
    public static List<FileSystemInfo.FileSystemInfoElement> ParseDfPk(string raw)
    {
        var result = new List<FileSystemInfo.FileSystemInfoElement>();
        var lines = raw.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var cut = SplitWhitespaceRuns(line);
            if (cut.Count < 6) continue;
            if (!int.TryParse(cut[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) continue;
            if (!int.TryParse(cut[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var used)) continue;
            if (!int.TryParse(cut[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var free)) continue;
            if (!cut[4].EndsWith('%')) continue;
            if (!float.TryParse(cut[4].AsSpan(0, cut[4].Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) continue;

            // The mount point is everything from column 5 on: rejoin in case a
            // path contains spaces.
            var mount = string.Join(" ", cut.Skip(5));

            // Skip pseudo and empty filesystems (udev reports 0 available).
            if (!mount.StartsWith('/') || free <= 0) continue;

            result.Add(new FileSystemInfo.FileSystemInfoElement
            {
                MountPoint = mount,
                Size = FormatKilobytes(size),
                Used = FormatKilobytes(used),
                Free = FormatKilobytes(free),
                Percent = percent,
            });
        }
        return result;
    }

    // MARK: - Darwin forwarding (the Swift declared these on ProcParsers)

    /// <seealso cref="DarwinParsers.ParseDarwinCPU"/>
    public static ProcessPercentInfo? ParseDarwinCPU(string raw) => DarwinParsers.ParseDarwinCPU(raw);

    /// <seealso cref="DarwinParsers.ParseDarwinTopLoad"/>
    public static SystemLoadInternal ParseDarwinTopLoad(string raw) => DarwinParsers.ParseDarwinTopLoad(raw);

    /// <seealso cref="DarwinParsers.ParseDarwinUptime"/>
    public static double ParseDarwinUptime(string raw) => DarwinParsers.ParseDarwinUptime(raw);

    /// <seealso cref="DarwinParsers.ParseDarwinReleaseName"/>
    public static string ParseDarwinReleaseName(string raw) => DarwinParsers.ParseDarwinReleaseName(raw);

    /// <seealso cref="DarwinParsers.ParseDarwinMemory"/>
    public static DarwinParsers.DarwinMemoryReading ParseDarwinMemory(string raw) => DarwinParsers.ParseDarwinMemory(raw);

    /// <seealso cref="DarwinParsers.ParseDarwinNetstat"/>
    public static Dictionary<string, (int Rx, int Tx)> ParseDarwinNetstat(string raw) => DarwinParsers.ParseDarwinNetstat(raw);

    // MARK: - helpers

    /// <summary>
    /// Foundation's <c>.whitespacesAndNewlines</c> is Unicode-aware and includes U+FEFF.
    /// </summary>
    internal static string TrimWhitespaceAndNewlines(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsWhitespaceOrNewline(value[start])) start++;
        while (end > start && IsWhitespaceOrNewline(value[end - 1])) end--;
        return value.Substring(start, end - start);
    }

    internal static bool IsWhitespaceOrNewline(char c) => char.IsWhiteSpace(c) || c == '\uFEFF';

    /// <summary>Swift's <c>Double(String)</c>: invariant culture, rejects empty/garbage.</summary>
    internal static double ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    internal static float ParseFloat(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>Swift's <c>Float(String)</c> accepts "inf"/"infinity" — loadavg rejects those.</summary>
    private static float? ParseFloatOrInfinity(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
