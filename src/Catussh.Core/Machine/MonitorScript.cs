using System.Text;

namespace Catussh.Core.Machine;

/// <summary>
/// Port of <c>ServerStatus.monitorScript(includeStatic:)</c> from
/// <c>ServerStatusInfo.swift</c>.
/// </summary>
public static class MonitorScript
{
    /// <summary>
    /// One remote run that samples the CPU and network counters twice one second
    /// apart and reads every other payload behind markers, so a refresh costs one
    /// exec channel instead of nine and one remote second instead of two. Linux
    /// reads /proc; Darwin uses its native tools. Static payloads join only while
    /// <paramref name="includeStatic"/> — they cannot change while a connection
    /// stays up.
    /// </summary>
    public static string Build(bool includeStatic)
    {
        var sections = new List<(string Name, string Command)>
        {
            ("platform", "echo \"$CATUSSH_KERNEL\""),
            ("cpu-a", "if [ \"$CATUSSH_KERNEL\" != Darwin ]; then /bin/cat /proc/stat; fi"),
            ("net-a", "if [ \"$CATUSSH_KERNEL\" = Darwin ]; then /usr/sbin/netstat -ibn; else /bin/cat /proc/net/dev; fi"),
            ("darwin-top", "if [ \"$CATUSSH_KERNEL\" = Darwin ]; then /usr/bin/top -l 2 -n 0 -s 1; fi"),
            ("wait", "if [ \"$CATUSSH_KERNEL\" != Darwin ]; then /bin/sleep 1; fi"),
            ("cpu-b", "if [ \"$CATUSSH_KERNEL\" != Darwin ]; then /bin/cat /proc/stat; fi"),
            ("net-b", "if [ \"$CATUSSH_KERNEL\" = Darwin ]; then /usr/sbin/netstat -ibn; else /bin/cat /proc/net/dev; fi"),
            ("mem", "if [ \"$CATUSSH_KERNEL\" = Darwin ]; then /usr/sbin/sysctl hw.memsize hw.pagesize vm.swapusage; /usr/bin/vm_stat; else /bin/cat /proc/meminfo; fi"),
            ("df", "/bin/df -Pk"),
            ("uptime", "if [ \"$CATUSSH_KERNEL\" = Darwin ]; then /bin/date +%s; /usr/sbin/sysctl -n kern.boottime; else /bin/cat /proc/uptime; fi"),
            ("loadavg", "if [ \"$CATUSSH_KERNEL\" != Darwin ]; then /bin/cat /proc/loadavg; fi"),
        };

        if (includeStatic)
        {
            sections.Add(("hostname", "/bin/hostname 2>/dev/null || hostname 2>/dev/null || echo \"localhost\""));
            sections.Add(("os-release", "if [ \"$CATUSSH_KERNEL\" = Darwin ]; then /usr/bin/sw_vers; else /bin/cat /etc/os-release; fi"));
        }

        var builder = new StringBuilder();
        builder.Append("CATUSSH_KERNEL=\"$(/usr/bin/uname -s)\"");
        foreach (var (name, command) in sections)
        {
            builder.Append('\n')
                .Append("echo '")
                .Append(ServerStatusMarkers.SectionMarkerPrefix)
                .Append(name)
                .Append(ServerStatusMarkers.SectionMarkerSuffix)
                .Append("'\n")
                .Append(command);
        }
        return builder.ToString();
    }
}
