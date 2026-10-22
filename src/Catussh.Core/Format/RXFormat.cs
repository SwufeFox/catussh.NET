using System.Globalization;

namespace Catussh.Core.Format;

/// <summary>
/// Port of <c>RXFormat</c> from <c>Foundation/CatusshDesign/Sources/CatusshDesign/Metric.swift</c>.
/// These strings are what the monitor cards print, so they must match the Swift
/// byte for byte (thousand-based units, rounded integers above 100).
/// </summary>
public static class RXFormat
{
    /// <summary><c>23 %</c> style percentages: integers.</summary>
    public static string Percent(double value)
    {
        if (!double.IsFinite(value)) return "0";
        return ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One decimal for rates and sizes.</summary>
    public static string OneDecimal(double value)
    {
        if (!double.IsFinite(value)) return "0";
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Splits a byte count into a number and a unit (<c>1.4</c>, <c>MB</c>).
    /// Deliberately base-1000: this matches the Swift formatter, not disk vendors.
    /// </summary>
    public static (string Value, string Unit) Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = Math.Max(0, double.IsFinite(bytes) ? bytes : 0);
        var index = 0;
        while (value >= 1000 && index < units.Length - 1)
        {
            value /= 1000;
            index++;
        }
        if (index == 0 || value >= 100)
            return (((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture), units[index]);
        return (value.ToString("0.0", CultureInfo.InvariantCulture), units[index]);
    }

    public static string BytesString(double value)
    {
        var (number, unit) = Bytes(value);
        return $"{number} {unit}";
    }

    public static (string Value, string Unit) Rate(double bytesPerSecond)
    {
        var (number, unit) = Bytes(bytesPerSecond);
        return (number, unit + "/s");
    }

    public static string RateString(double bytesPerSecond)
    {
        var (number, unit) = Rate(bytesPerSecond);
        return $"{number} {unit}";
    }

    /// <summary>
    /// "41 days, 3 h" style uptime. Foundation's DateComponentsFormatter keeps two
    /// largest non-zero units; this reproduces that ordering explicitly.
    /// </summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return "—";

        var total = (long)seconds;
        var days = total / 86_400;
        var hours = total % 86_400 / 3600;
        var minutes = total % 3600 / 60;

        var parts = new List<string>(2);
        if (days > 0) parts.Add($"{days} d");
        if (hours > 0) parts.Add($"{hours} h");
        if (parts.Count < 2 && minutes > 0) parts.Add($"{minutes} min");
        if (parts.Count == 0) parts.Add("0 min");
        return string.Join(" ", parts);
    }
}
