using Catussh.Core.Store;

namespace Catussh.Core.RemoteDesktop;

public static class UURemoteFolderSync
{
    public static RDMachine MakeMachine(UUDeviceInfo device, string folder)
    {
        var machine = new RDMachine
        {
            RemoteAddress = device.DeviceID,
            RemotePort = "22",
            Name = string.IsNullOrWhiteSpace(device.Alias) ? device.DeviceID : device.Alias,
            Group = folder,
            Comment = $"{UUDeviceInfo.PlatformName(device.Platform)} device",
        };
        return machine with
        {
            RemoteDesktopProtocol = RDMachine.RemoteDesktopProtocolType.UuRemote,
            TransportMode = RDMachine.Transport.UURemote,
            UUDeviceID = device.DeviceID,
            UUDeviceAlias = device.Alias,
            UUDeviceStatus = device.Status,
            UUWallpaperUrl = device.WallpaperUrl ?? "",
            UURemoteManaged = true,
        };
    }

    public static void Apply(
        IEnumerable<UUDeviceInfo> devices,
        string folder,
        string ownDeviceID,
        CatusshStore store)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(store);
        var desktops = devices.Where(device => device.IsDesktop && device.DeviceID != ownDeviceID).ToArray();
        var present = desktops.Select(static device => device.DeviceID).ToHashSet(StringComparer.Ordinal);
        var existingMachines = store.Machines;
        var upserts = new Dictionary<string, RDMachine>(StringComparer.Ordinal);

        foreach (var device in desktops)
        {
            var machine = existingMachines.FirstOrDefault(saved => saved.UUDeviceID == device.DeviceID);
            if (machine is null)
            {
                var created = MakeMachine(device, folder);
                upserts[created.Id] = created;
                continue;
            }

            var oldAlias = string.IsNullOrEmpty(machine.UUDeviceAlias) ? device.DeviceID : machine.UUDeviceAlias;
            var name = machine.Name == oldAlias && !string.IsNullOrEmpty(device.Alias) ? device.Alias : machine.Name;
            var managed = machine.Group == folder && !machine.UURemoteManaged || machine.UURemoteManaged;
            if (name == machine.Name && device.Alias == machine.UUDeviceAlias && device.Status == machine.UUDeviceStatus &&
                (device.WallpaperUrl ?? "") == machine.UUWallpaperUrl && managed == machine.UURemoteManaged)
                continue;

            upserts[machine.Id] = machine with
            {
                Name = name,
                UUDeviceAlias = device.Alias,
                UUDeviceStatus = device.Status,
                UUWallpaperUrl = device.WallpaperUrl ?? "",
                UURemoteManaged = managed,
            };
        }

        var removals = existingMachines
            .Where(machine => machine.Group == folder && machine.UURemoteManaged && !present.Contains(machine.UUDeviceID))
            .Select(static machine => machine.Id)
            .ToArray();
        store.ApplyMachineChanges(upserts.Values.ToArray(), removals);
    }
}
