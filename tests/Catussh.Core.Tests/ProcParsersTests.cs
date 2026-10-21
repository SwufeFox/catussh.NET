using Catussh.Core.Machine;

namespace Catussh.Core.Tests;

internal static class TestText
{
    /// <summary>Test-side shim for Foundation's <c>.whitespacesAndNewlines</c> trimming.</summary>
    public static string Trim(string value) => value.Trim(' ', '\t', '\n', '\r', '\v', '\f', '\u00A0', '\uFEFF');
}

/// <summary>
/// Port of <c>Foundation/MachineStatus/Tests/MachineStatusTests/ProcParsersTests.swift</c>:
/// fixture-driven tests for the Linux /proc and os-release parsers.
/// </summary>
public sealed class ProcParsersTests
{
    // MARK: - /proc/meminfo

    private const string MeminfoFixture = """
        MemTotal:       16384000 kB
        MemFree:         1024000 kB
        MemAvailable:    8192000 kB
        Buffers:          512000 kB
        Cached:          4096000 kB
        SwapCached:            0 kB
        SwapTotal:       2097152 kB
        SwapFree:        1048576 kB
        HugePages_Total:       0
        Hugepagesize:       2048 kB
        """;

    [Test]
    public async Task MeminfoParsesKeyedValues()
    {
        var info = ProcParsers.ParseMeminfo(MeminfoFixture);
        await Assert.That(info["MEMTOTAL"]).IsEqualTo(16_384_000d);
        await Assert.That(info["MEMFREE"]).IsEqualTo(1_024_000d);
        await Assert.That(info["BUFFERS"]).IsEqualTo(512_000d);
        await Assert.That(info["CACHED"]).IsEqualTo(4_096_000d);
        await Assert.That(info["SWAPTOTAL"]).IsEqualTo(2_097_152d);
        await Assert.That(info["SWAPFREE"]).IsEqualTo(1_048_576d);
        // A kB-suffixed line parses under its uppercased, colon-stripped key.
        await Assert.That(info["HUGEPAGESIZE"]).IsEqualTo(2048d);
        // Lines without a kB suffix are ignored.
        await Assert.That(info.ContainsKey("HUGEPAGES_TOTAL")).IsFalse();
    }

    [Test]
    public async Task SwapUsageIsRelativeToSwapTotal()
    {
        // Regression: swap usage was divided by physical memory total, making the
        // reported swap percentage always wrong.
        var info = new MemoryInfo(
            total: 16_384_000, free: 1_024_000, buffers: 512_000, cached: 4_096_000,
            swapTotal: 2_097_152, swapFree: 1_048_576);
        await Assert.That(info.SwapUsed).IsEqualTo(0.5).Within(0.001);
        await Assert.That(info.PhyUsed)
            .IsEqualTo((16_384_000 - 1_024_000 - 4_096_000 - 512_000) / 16_384_000d)
            .Within(0.001);
    }

    [Test]
    public async Task ZeroSwapDoesNotDivideByZero()
    {
        var info = new MemoryInfo(
            total: 16_384_000, free: 1_024_000, buffers: 0, cached: 0,
            swapTotal: 0, swapFree: 0);
        await Assert.That(info.SwapUsed).IsEqualTo(0d);
        await Assert.That(double.IsNaN(info.SwapUsed)).IsFalse();
    }

    // MARK: - /proc/stat

    private const string ProcStatFixture = """
        cpu  100 0 200 1600 50 0 10 0 0 0
        cpu0 50 0 100 800 25 0 5 0 0 0
        cpu1 50 0 100 800 25 0 5 0 0 0
        intr 123456
        """;

    [Test]
    public async Task ProcStatParsesSummaryAndCores()
    {
        var (summary, cores) = ProcParsers.ParseProcStat(ProcStatFixture);
        await Assert.That(summary!.User).IsEqualTo(100f);
        await Assert.That(summary.Idle).IsEqualTo(1600f);
        await Assert.That(cores["cpu0"].System).IsEqualTo(100f);
        await Assert.That(cores["cpu1"].IOwait).IsEqualTo(25f);
        await Assert.That(cores.Count).IsEqualTo(2);
    }

    [Test]
    public async Task PercentageBetweenSamples()
    {
        var (privSum, _) = ProcParsers.ParseProcStat(ProcStatFixture);
        var currRaw = ProcStatFixture.Replace("cpu  100", "cpu  200"); // user +100
        var (currSum, _) = ProcParsers.ParseProcStat(currRaw);

        var percent = ProcParsers.CalculatePercent(privSum!, currSum!);
        // total delta is 100, all of it user time
        await Assert.That(percent.SumUser).IsEqualTo(100f).Within(0.01f);
        await Assert.That(percent.SumUsed).IsEqualTo(100f).Within(0.01f);
    }

    [Test]
    public async Task IdenticalSamplesYieldZeroNotNaN()
    {
        // Regression: an idle machine can report identical counters twice; dividing
        // by the zero delta produced NaN/inf percentages.
        var (sum, _) = ProcParsers.ParseProcStat(ProcStatFixture);
        var percent = ProcParsers.CalculatePercent(sum!, sum!);
        await Assert.That(percent.SumUsed).IsEqualTo(0f);
        await Assert.That(float.IsNaN(percent.SumUsed)).IsFalse();
        await Assert.That(float.IsInfinity(percent.SumSystem)).IsFalse();
    }

    // MARK: - /etc/os-release

    [Test]
    public async Task OsReleasePrefersPrettyNameAndStripsDoubleQuotes()
    {
        const string fixture = """
            NAME="Ubuntu"
            VERSION_ID="22.04"
            PRETTY_NAME="Ubuntu 22.04.1 LTS"
            """;
        await Assert.That(ProcParsers.ParseOsReleaseName(fixture)).IsEqualTo("Ubuntu 22.04.1 LTS");
    }

    [Test]
    public async Task OsReleaseFallsBackToName()
    {
        await Assert.That(ProcParsers.ParseOsReleaseName("NAME=\"Debian GNU/Linux\"")).IsEqualTo("Debian GNU/Linux");
    }

    [Test]
    public async Task OsReleaseStripsSingleQuotes()
    {
        // Regression: the old condition compared prefixes against double quotes but
        // suffixes against single quotes, so nothing was stripped.
        await Assert.That(ProcParsers.ParseOsReleaseName("NAME='Fedora Linux'")).IsEqualTo("Fedora Linux");
    }

    [Test]
    public async Task OsReleaseUnquotedAndMissing()
    {
        await Assert.That(ProcParsers.ParseOsReleaseName("NAME=Alpine Linux")).IsEqualTo("Alpine Linux");
        await Assert.That(ProcParsers.ParseOsReleaseName("")).IsEqualTo("Generic Linux");
    }

    // MARK: - /proc/loadavg

    [Test]
    public async Task LoadavgParses()
    {
        var load = ProcParsers.ParseLoadavg("0.27 0.34 0.39 2/916 23817");
        await Assert.That(load.Load1Avg).IsEqualTo(0.27f).Within(0.001f);
        await Assert.That(load.Load5Avg).IsEqualTo(0.34f).Within(0.001f);
        await Assert.That(load.Load15Avg).IsEqualTo(0.39f).Within(0.001f);
        await Assert.That(load.RunningProcess).IsEqualTo(2);
        await Assert.That(load.TotalProcess).IsEqualTo(916);
    }

    [Test]
    public async Task LoadavgMalformedReturnsZero()
    {
        var load = ProcParsers.ParseLoadavg("garbage");
        await Assert.That(load.Load1Avg).IsEqualTo(0f);
        await Assert.That(load.TotalProcess).IsEqualTo(0);
    }

    // MARK: - merged monitor script

    private const string MergedOutputFixture = """
        <<<CATUSSH-SECTION:cpu-a>>>
        cpu  100 0 200 1600 50 0 10 0 0 0
        cpu0 50 0 100 800 25 0 5 0 0 0
        <<<CATUSSH-SECTION:net-a>>>
        Inter-|   Receive                                                |  Transmit
         face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
            lo: 1000      10    0    0    0     0          0         0     1000      10    0    0    0     0       0          0
          eth0: 5000      50    0    0    0     0          0         0     2000      20    0    0    0     0       0          0
        <<<CATUSSH-SECTION:wait>>>
        <<<CATUSSH-SECTION:cpu-b>>>
        cpu  200 0 300 3200 100 0 20 0 0 0
        cpu0 100 0 150 1600 50 0 10 0 0 0
        <<<CATUSSH-SECTION:uptime>>>
        773229.50 1531748.20
        <<<CATUSSH-SECTION:hostname>>>
        web-01
        """;

    [Test]
    public async Task SplitMonitorSectionsAssignsPayloads()
    {
        var sections = ProcParsers.SplitMonitorSections(MergedOutputFixture);
        await Assert.That(sections["cpu-a"].Contains("cpu0 50")).IsTrue();
        await Assert.That(sections["net-a"].Contains("eth0:")).IsTrue();
        await Assert.That(TestText.Trim(sections["uptime"])).IsEqualTo("773229.50 1531748.20");
        await Assert.That(TestText.Trim(sections["hostname"])).IsEqualTo("web-01");
        // The sleep marker opens a section with no payload.
        await Assert.That(TestText.Trim(sections["wait"])).IsEqualTo("");
        await Assert.That(sections["cpu-b"].Contains("cpu0 50")).IsFalse();
    }

    [Test]
    public async Task SplitMonitorSectionsIgnoresInlineMarkerLookalikes()
    {
        // Only a full-line marker opens a section; payload text containing the marker
        // inside a longer line must not split.
        const string raw = """
            <<<CATUSSH-SECTION:df>>>
            /dev/sda1 100 50 50 50% /mnt/<<<CATUSSH-SECTION:mem>>>-disk
            """;
        var sections = ProcParsers.SplitMonitorSections(raw);
        await Assert.That(sections.ContainsKey("mem")).IsFalse();
        await Assert.That(sections["df"].Contains("/mnt/<<<CATUSSH-SECTION:mem>>>-disk")).IsTrue();
    }

    [Test]
    public async Task SplitMonitorSectionsToleratesCarriageReturns()
    {
        const string raw = "<<<CATUSSH-SECTION:hostname>>>\r\nweb-01\r\n<<<CATUSSH-SECTION:uptime>>>\r\n42 84\r\n";
        var sections = ProcParsers.SplitMonitorSections(raw);
        await Assert.That(TestText.Trim(sections["hostname"])).IsEqualTo("web-01");
        await Assert.That(ProcParsers.ParseUptime(sections["uptime"])).IsEqualTo(42d);
    }

    [Test]
    public async Task MonitorScriptSamplesCountersTwiceAroundOneSleep()
    {
        var script = MonitorScript.Build(includeStatic: true);
        await Assert.That(CountOf(script, "/bin/cat /proc/stat")).IsEqualTo(2);
        await Assert.That(CountOf(script, "/bin/cat /proc/net/dev")).IsEqualTo(2);
        await Assert.That(CountOf(script, "/bin/sleep 1")).IsEqualTo(1);
        foreach (var section in new[] { "cpu-a", "net-a", "cpu-b", "net-b", "mem", "df", "uptime", "loadavg", "hostname", "os-release" })
        {
            await Assert.That(script.Contains($"echo '{ServerStatusMarkers.SectionMarkerPrefix}{section}{ServerStatusMarkers.SectionMarkerSuffix}'"))
                .IsTrue()
                .Because($"missing marker for {section}");
        }
    }

    [Test]
    public async Task MonitorScriptOmitsStaticsUnlessAsked()
    {
        var script = MonitorScript.Build(includeStatic: false);
        await Assert.That(script.Contains("os-release")).IsFalse();
        await Assert.That(script.Contains("hostname")).IsFalse();
        await Assert.That(CountOf(script, "/bin/sleep 1")).IsEqualTo(1);
    }

    // MARK: - /proc/uptime

    [Test]
    public async Task ParseUptimeReadsFirstField()
    {
        await Assert.That(ProcParsers.ParseUptime("773229.50 1531748.20\n")).IsEqualTo(773_229.5).Within(0.001);
        await Assert.That(ProcParsers.ParseUptime("42")).IsEqualTo(42d);
        await Assert.That(ProcParsers.ParseUptime("garbage")).IsEqualTo(0d);
        await Assert.That(ProcParsers.ParseUptime("")).IsEqualTo(0d);
    }

    // MARK: - df -Pk

    private const string DfPkFixture = """
        Filesystem                        1024-blocks     Used Available Capacity Mounted on
        udev                                 8192000        0   8192000       0% /dev
        tmpfs                                1638400   124928   1513472       8% /run
        /dev/mapper/ubuntu--vg-ubuntu--lv   51281224 15994688  32923552      33% /
        tmpfs                                8192000        0   8192000       0% /dev/shm
        tmpfs                                   5120        0      5120       0% /run/lock
        none                                       0        0         0       0% /sys/fs/cgroup
        /dev/sda2                            1959920   251792   1606088      14% /boot
        /dev/sdb1                          976762532 6185396 920562116       1% /media/My Passport
        """;

    [Test]
    public async Task DfPkParsesNumericColumns()
    {
        var elements = ProcParsers.ParseDfPk(DfPkFixture);
        var root = elements.First(e => e.MountPoint == "/");
        await Assert.That(root.Size).IsEqualTo("49G");
        await Assert.That(root.Used).IsEqualTo("15G");
        await Assert.That(root.Free).IsEqualTo("31G");
        await Assert.That(root.Percent).IsEqualTo(33f);
    }

    [Test]
    public async Task DfPkKeepsLongDeviceNames()
    {
        // Regression: df -h wraps a long device name onto its own line, and the old
        // space-splitting parser silently dropped both halves; -P keeps one
        // filesystem per line.
        var elements = ProcParsers.ParseDfPk(DfPkFixture);
        await Assert.That(elements.Any(e => e.MountPoint == "/")).IsTrue();
        await Assert.That(elements.Count).IsEqualTo(7);
    }

    [Test]
    public async Task DfPkKeepsSpacesInMountPointAndSkipsEmptyFilesystems()
    {
        var elements = ProcParsers.ParseDfPk(DfPkFixture);
        var mounted = elements.First(e => e.MountPoint == "/media/My Passport");
        await Assert.That(mounted.Size).IsEqualTo("932G");
        await Assert.That(mounted.Used).IsEqualTo("5.9G");
        await Assert.That(elements.Any(e => e.MountPoint == "/sys/fs/cgroup")).IsFalse();
    }

    [Test]
    public async Task DfPkFormatsKilobytesLikeDfH()
    {
        await Assert.That(ProcParsers.FormatKilobytes(957_440)).IsEqualTo("935M");
        await Assert.That(ProcParsers.FormatKilobytes(512)).IsEqualTo("512K");
        await Assert.That(ProcParsers.FormatKilobytes(1_945_600)).IsEqualTo("1.9G");
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}

