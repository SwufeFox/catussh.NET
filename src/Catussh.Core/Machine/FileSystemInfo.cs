namespace Catussh.Core.Machine;

/// <summary>Port of <c>SSI+FileSystemInfo.swift</c> — 服务器磁盘信息（df -Pk）。</summary>
public sealed record FileSystemInfo
{
    public IReadOnlyList<FileSystemInfoElement> Elements { get; init; } = Array.Empty<FileSystemInfoElement>();

    public FileSystemInfo() { }

    public FileSystemInfo(IReadOnlyList<FileSystemInfoElement> elements) => Elements = elements;

    public static FileSystemInfo FromDfPk(string dfPk) => new(ProcParsers.ParseDfPk(dfPk));

    /// <summary><see cref="Id"/> mirrors the Swift <c>id</c>: the mount point.</summary>
    public sealed record FileSystemInfoElement
    {
        public string MountPoint { get; init; } = "";
        public string Size { get; init; } = "";
        public string Used { get; init; } = "";
        public string Free { get; init; } = "";
        public float Percent { get; init; }

        public string Id => MountPoint;
    }
}
