using Catussh.Core.Machine;

namespace Catussh.Core.Tests;

/// <summary>
/// Port of <c>Foundation/MachineStatus/Tests/MachineStatusTests/DarwinParsersTests.swift</c>.
/// </summary>
public sealed class DarwinParsersTests
{
    private const string TopFixture = """
        Processes: 700 total, 10 running, 690 sleeping, 3000 threads
        Load Avg: 1.11, 2.22, 3.33
        CPU usage: 10.0% user, 20.0% sys, 70.0% idle
        PhysMem: 10G used, 6G unused.
        Processes: 743 total, 14 running, 729 sleeping, 3558 threads
        Load Avg: 98.70, 115.18, 55.43
        CPU usage: 50.31% user, 32.91% sys, 16.77% idle
        PhysMem: 15G used, 90M unused.
        """;

    [Test]
    public async Task TopUsesLastSampleForCpuAndLoad()
    {
        var processor = ProcessorInfo.FromDarwinTop(TopFixture);
        await Assert.That(processor!.Summary.SumUser).IsEqualTo(50.31f).Within(0.001f);
        await Assert.That(processor.Summary.SumSystem).IsEqualTo(32.91f).Within(0.001f);
        await Assert.That(processor.Summary.SumUsed).IsEqualTo(83.23f).Within(0.001f);
        await Assert.That(processor.Cores.Count).IsEqualTo(0);

        var load = ProcParsers.ParseDarwinTopLoad(TopFixture);
        await Assert.That(load.Load1Avg).IsEqualTo(98.70f).Within(0.001f);
        await Assert.That(load.Load5Avg).IsEqualTo(115.18f).Within(0.001f);
        await Assert.That(load.Load15Avg).IsEqualTo(55.43f).Within(0.001f);
        await Assert.That(load.RunningProcess).IsEqualTo(14);
        await Assert.That(load.TotalProcess).IsEqualTo(743);
    }

    [Test]
    public async Task MalformedTopReturnsNoReading()
    {
        await Assert.That(ProcessorInfo.FromDarwinTop("not top output")).IsNull();
        var load = ProcParsers.ParseDarwinTopLoad("not top output");
        await Assert.That(load).IsEqualTo(new SystemLoadInternal());
    }

    [Test]
    public async Task DarwinMemoryUsesPageSizeAndSwapUnits()
    {
        const string fixture = """
            hw.memsize: 17179869184
            hw.pagesize: 16384
            vm.swapusage: total = 2048.00M  used = 512.00M  free = 1536.00M  (encrypted)
            Mach Virtual Memory Statistics: (page size of 16384 bytes)
            Pages free:                                     4497.
            Pages speculative:                              1547.
            Pages occupied by compressor:                  21904.
            File-backed pages:                            407513.
            Anonymous pages:                              486560.
            """;
        var memory = MemoryInfo.FromDarwinMemory(fixture);
        await Assert.That(memory.MemTotal).IsEqualTo(16_777_216d);
        await Assert.That(memory.MemFree).IsEqualTo(96704d);
        await Assert.That(memory.MemCached).IsEqualTo(6_495_456d);
        await Assert.That(memory.MemBuffers).IsEqualTo(0d);
        await Assert.That(memory.SwapTotal).IsEqualTo(2_097_152d);
        await Assert.That(memory.SwapFree).IsEqualTo(1_572_864d);
        await Assert.That(memory.SwapUsed).IsEqualTo(0.25).Within(0.001);
        await Assert.That(memory.PhyUsed).IsGreaterThan(0d);
        await Assert.That(memory.PhyUsed).IsLessThan(1d);
    }

    [Test]
    public async Task DarwinMemoryFallsBackToVmStatPageSize()
    {
        const string fixture = """
            hw.memsize: 8589934592
            Mach Virtual Memory Statistics: (page size of 4096 bytes)
            Pages free: 1000.
            Pages speculative: 500.
            File-backed pages: 2000.
            """;
        var memory = MemoryInfo.FromDarwinMemory(fixture);
        await Assert.That(memory.MemFree).IsEqualTo(6000d);
        await Assert.That(memory.MemCached).IsEqualTo(6000d);
    }

    [Test]
    public async Task DarwinNetstatReadsLinkRowsAndOptionalAddressColumn()
    {
        const string before = """
            Name       Mtu   Network       Address            Ipkts Ierrs     Ibytes    Opkts Oerrs     Obytes  Coll
            lo0        16384 <Link#1>                          10     0       100       10     0       100     0
            en0        1500  <Link#11>   be:c4:9b:88:a5:08    20     0      1000       30     0      2000     0
            gif0*      1280  <Link#2>                             0     0         0        0     0         0     0
            en0        1500  192.168.31    192.168.31.241      20     -      1000       30     -      2000     -
            """;
        const string after = """
            lo0        16384 <Link#1>                          15     0       150       20     0       250     0
            en0        1500  <Link#11>   be:c4:9b:88:a5:08    25     0      3000       40     0      6000     0
            gif0       1280  <Link#2>                             1     0        64        2     0       128     0
            """;
        var network = NetworkInfo.FromNetstat(before, after);
        await Assert.That(network!.Elements.Count).IsEqualTo(3);
        await Assert.That(network.Elements.First(e => e.Device == "en0").RxBytesPerSec).IsEqualTo(2000);
        await Assert.That(network.Elements.First(e => e.Device == "en0").TxBytesPerSec).IsEqualTo(4000);
        await Assert.That(network.Elements.First(e => e.Device == "lo0").RxBytesPerSec).IsEqualTo(50);
        await Assert.That(network.Elements.First(e => e.Device == "gif0").TxBytesPerSec).IsEqualTo(128);
    }

    [Test]
    public async Task DarwinUptimeAndReleaseName()
    {
        const string uptime = "1000\n{ sec = 750, usec = 912078 } Sat Oct 17 21:39:26 2026\n";
        await Assert.That(ProcParsers.ParseDarwinUptime(uptime)).IsEqualTo(250d);
        await Assert.That(ProcParsers.ParseDarwinUptime("garbage")).IsEqualTo(0d);

        const string release = """
            ProductName:            macOS
            ProductVersion:         27.2
            BuildVersion:           26B5112a
            """;
        await Assert.That(ProcParsers.ParseDarwinReleaseName(release)).IsEqualTo("macOS 27.2");
    }

    [Test]
    public async Task MonitorScriptContainsDarwinProbesAndLinuxFallbacks()
    {
        var script = MonitorScript.Build(includeStatic: true);
        await Assert.That(script.Contains("/usr/bin/top -l 2 -n 0 -s 1")).IsTrue();
        await Assert.That(script.Contains("/usr/sbin/netstat -ibn")).IsTrue();
        await Assert.That(script.Contains("/usr/bin/vm_stat")).IsTrue();
        await Assert.That(script.Contains("vm.swapusage")).IsTrue();
        await Assert.That(script.Contains("/usr/bin/sw_vers")).IsTrue();
        await Assert.That(script.Contains("kern.boottime")).IsTrue();
        await Assert.That(script.Contains("/bin/cat /proc/meminfo")).IsTrue();
    }
}
