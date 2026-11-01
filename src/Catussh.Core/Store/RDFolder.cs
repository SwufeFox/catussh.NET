using System.Text.Json.Serialization;

namespace Catussh.Core.Store;

/// <summary>A saved server folder and its owner.</summary>
public sealed record RDFolder
{
    public string Id { get; init; } = Guid.NewGuid().ToString().ToUpperInvariant();
    public string Name { get; init; } = "";
    public string Source { get; init; } = "local";
    public string? SourceUserID { get; init; }
    public int SortIndex { get; init; }

    [JsonIgnore]
    public bool IsLinked => Source == "uuRemote" && !string.IsNullOrEmpty(SourceUserID);
}
