using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Catussh.Core.Machine;

/// <summary>
/// Port of <c>SSI+GraphicsInfo.swift</c> — 服务器 GPU 信息，来自 <c>nvidia-smi -q -x</c>。
///
/// The Swift version decoded the XML with XMLCoder; this port walks the same tree
/// with <see cref="XDocument"/>. Field names and the "11019 MiB" → 11019 rule are
/// kept identical so both apps read each other's payloads.
/// </summary>
public static class GraphicsParser
{
    /// <summary>
    /// Returns null when the payload is not XML (nvidia-smi missing → the script
    /// echoes "no nvidia gpu available"), matching the Swift failable init.
    /// </summary>
    public static GraphicsInfo? FromNvidiaSmiXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "nvidia_smi_log") return null;

        var version = Child(root, "cuda_version") ?? "unknown";
        var units = new List<SingleGraphicsInfo>();
        try
        {
            foreach (var gpu in root.Elements().Where(e => e.Name.LocalName == "gpu"))
            {
                units.Add(SingleGraphicsInfo.FromXml(gpu));
            }
        }
        catch (FormatException)
        {
            // XMLCoder decoded the document as a whole; one malformed <gpu>
            // failed the entire reading, leaving the Swift failable init nil.
            return null;
        }
        return new GraphicsInfo(version, units);
    }

    /// <summary>
    /// XMLCoder resolved unkeyed/absent keys to type defaults; a missing element
    /// therefore yields "" here rather than throwing.
    /// </summary>
    internal static string? Child(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
}

/// <summary>Port of <c>ServerStatus.GraphicsInfo</c> (CodingKeys: cuda_version / gpu).</summary>
public sealed record GraphicsInfo
{
    /// <summary>CUDA version, like '11.2'.</summary>
    public string Version { get; init; } = "unknown";

    public IReadOnlyList<SingleGraphicsInfo> Units { get; init; } = Array.Empty<SingleGraphicsInfo>();

    public GraphicsInfo() { }

    public GraphicsInfo(string version, IReadOnlyList<SingleGraphicsInfo> units)
    {
        Version = version;
        Units = units;
    }
}

/// <summary>
/// Port of <c>ServerStatus.SingleGraphicsInfo</c> — 单块 GPU 信息。
/// Swift's CodingKeys remap product_name → Name and fb_memory_usage → Memory,
/// which this record spells out with the same on-the-wire names.
/// </summary>
public sealed record SingleGraphicsInfo
{
    public string Uuid { get; init; } = "";
    public string Name { get; init; } = "";
    public SingleGraphicsInfoMemory Memory { get; init; } = new();
    public string VbiosVersion { get; init; } = "";
    public string FanSpeed { get; init; } = "";
    public SingleGraphicsInfoUtilization Utilization { get; init; } = new();

    /// <summary>Stable across refreshes: nvidia-smi reports the GPU's own uuid.</summary>
    public string Id => Uuid;

    public SingleGraphicsInfo() { }

    public SingleGraphicsInfo(
        string uuid = "",
        string name = "",
        SingleGraphicsInfoMemory? memory = null,
        string vbiosVersion = "",
        string fanSpeed = "",
        SingleGraphicsInfoUtilization? utilization = null)
    {
        Uuid = uuid;
        Name = name;
        Memory = memory ?? new SingleGraphicsInfoMemory();
        VbiosVersion = vbiosVersion;
        FanSpeed = fanSpeed;
        Utilization = utilization ?? new SingleGraphicsInfoUtilization();
    }

    /// <summary>"11019 MiB" → 11019. The whole payload is rejected when a key is absent.</summary>
    internal static float S2F(string? status)
    {
        var head = (status ?? "").Split(' ')[0];
        return float.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : 0f;
    }

    internal static SingleGraphicsInfo FromXml(XElement gpu)
    {
        string Required(string name) =>
            GraphicsParser.Child(gpu, name)
            ?? throw new FormatException($"missing <{name}> in <gpu>");

        var memoryNode = gpu.Elements().FirstOrDefault(e => e.Name.LocalName == "fb_memory_usage")
            ?? throw new FormatException("missing <fb_memory_usage> in <gpu>");
        var utilizationNode = gpu.Elements().FirstOrDefault(e => e.Name.LocalName == "utilization")
            ?? throw new FormatException("missing <utilization> in <gpu>");

        string RequiredIn(XElement parent, string name) =>
            GraphicsParser.Child(parent, name)
            ?? throw new FormatException($"missing <{name}>");

        // "11019 MiB" → 11019: the whole element is rejected when a key is absent,
        // the way XMLCoder failed the decode rather than defaulting.
        var memory = new SingleGraphicsInfoMemory(
            S2F(RequiredIn(memoryNode, "total")),
            S2F(RequiredIn(memoryNode, "used")),
            S2F(RequiredIn(memoryNode, "free")));
        var utilization = new SingleGraphicsInfoUtilization(
            S2F(RequiredIn(utilizationNode, "gpu_util")),
            S2F(RequiredIn(utilizationNode, "memory_util")),
            S2F(RequiredIn(utilizationNode, "encoder_util")),
            S2F(RequiredIn(utilizationNode, "decoder_util")));

        return new SingleGraphicsInfo(
            uuid: Required("uuid"),
            name: Required("product_name"),
            memory: memory,
            vbiosVersion: Required("vbios_version"),
            fanSpeed: Required("fan_speed"),
            utilization: utilization);
    }

    public override string ToString() => $"uuid: {Uuid} name: {Name}";
}

/// <summary>Port of <c>SingleGraphicsInfo.Memory</c> — MiB values parsed out of the XML strings.</summary>
public sealed record SingleGraphicsInfoMemory(float Total = 0, float Used = 0, float Free = 0);

/// <summary>Port of <c>SingleGraphicsInfo.Utilization</c>.</summary>
public sealed record SingleGraphicsInfoUtilization(
    float GpuUtil = 0,
    float MemoryUtil = 0,
    float EncoderUtil = 0,
    float DecoderUtil = 0);
