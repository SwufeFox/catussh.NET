using System.Text.Json;

namespace Catussh.Core.Store;

/// <summary>
/// The app's saved state — port of <c>CatusshStore</c> from
/// <c>CatusshModule/Store/CatusshStore.swift</c>, narrowed to what the desktop UI
/// shows today: machines and identities.
///
/// The Swift keeps these in CloudKit-synced containers; here they are one JSON file
/// under <c>%APPDATA%\catussh.NET</c>. Writes go through a temp file and a rename so
/// a crash mid-save cannot leave a half-written store behind.
/// </summary>
public sealed class CatusshStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private StoreDocument _document = new();

    public CatusshStore(string? path = null)
    {
        _path = path ?? DefaultPath();
        Load();
    }

    /// <summary><c>%APPDATA%\catussh.NET\store.json</c>.</summary>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "catussh.NET",
        "store.json");

    public string Path_ => _path;

    public IReadOnlyList<RDMachine> Machines
    {
        get { lock (_gate) return _document.Machines.ToList(); }
    }

    public IReadOnlyList<Identity> Identities
    {
        get { lock (_gate) return _document.Identities.ToList(); }
    }

    /// <summary>True when the file existed but could not be parsed; the UI says so instead of showing an empty list.</summary>
    public bool LoadFailed { get; private set; }

    public string? LoadError { get; private set; }

    /// <summary>Newest first, the order the home page's recent list wants.</summary>
    public IReadOnlyList<RDMachine> RecentMachines(int limit)
    {
        lock (_gate)
        {
            return _document.Machines
                .OrderByDescending(m => m.LastConnection)
                .Take(Math.Max(0, limit))
                .ToList();
        }
    }

    /// <summary>Machines whose group matches, preserving save order — the Servers table's sections.</summary>
    public IReadOnlyList<IGrouping<string, RDMachine>> GroupedMachines()
    {
        lock (_gate)
        {
            return _document.Machines
                .GroupBy(m => string.IsNullOrWhiteSpace(m.Group) ? "Ungrouped" : m.Group)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public RDMachine? FindMachine(string id)
    {
        lock (_gate) return _document.Machines.FirstOrDefault(m => m.Id == id);
    }

    public Identity? Identity(string id)
    {
        lock (_gate) return _document.Identities.FirstOrDefault(i => i.Id == id);
    }

    /// <summary>The identity a machine names, when it names one that still exists.</summary>
    public Identity? IdentityFor(RDMachine machine) =>
        string.IsNullOrEmpty(machine.AssociatedIdentity) ? null : Identity(machine.AssociatedIdentity);

    /// <summary>Identities offered to a server set to "Automatic", in the Swift's try order.</summary>
    public IReadOnlyList<Identity> AutoIdentities()
    {
        lock (_gate)
        {
            return _document.Identities
                .Where(i => i.AuthenticAutomatically)
                .OrderByDescending(i => i.LastRecentUsed)
                .ToList();
        }
    }

    public void Upsert(RDMachine machine)
    {
        lock (_gate)
        {
            var index = _document.Machines.FindIndex(m => m.Id == machine.Id);
            if (index >= 0) _document.Machines[index] = machine;
            else _document.Machines.Add(machine);
            Save();
        }
    }

    public void Upsert(Identity identity)
    {
        lock (_gate)
        {
            var index = _document.Identities.FindIndex(i => i.Id == identity.Id);
            if (index >= 0) _document.Identities[index] = identity;
            else _document.Identities.Add(identity);
            Save();
        }
    }

    public bool RemoveMachine(string id)
    {
        lock (_gate)
        {
            var removed = _document.Machines.RemoveAll(m => m.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public bool RemoveIdentity(string id)
    {
        lock (_gate)
        {
            var removed = _document.Identities.RemoveAll(i => i.Id == id) > 0;
            if (!removed) return false;

            // A machine pointing at a deleted identity falls back to Automatic, which
            // is what the Swift does rather than leaving a dangling id.
            for (var i = 0; i < _document.Machines.Count; i++)
            {
                if (_document.Machines[i].AssociatedIdentity == id)
                    _document.Machines[i] = _document.Machines[i] with { AssociatedIdentity = null };
            }
            Save();
            return true;
        }
    }

    /// <summary>Records a connect attempt so "last used" and the recent list stay true.</summary>
    public void MarkConnected(string machineId, string? banner)
    {
        lock (_gate)
        {
            var index = _document.Machines.FindIndex(m => m.Id == machineId);
            if (index < 0) return;
            _document.Machines[index] = _document.Machines[index] with
            {
                LastConnection = DateTime.UtcNow,
                LastBanner = banner ?? _document.Machines[index].LastBanner,
            };
            Save();
        }
    }

    /// <summary>Seeds the demo servers once, so a first run has something to click.</summary>
    public void SeedIfEmpty(IReadOnlyList<RDMachine> machines, IReadOnlyList<Identity> identities)
    {
        lock (_gate)
        {
            if (_document.Machines.Count > 0 || _document.Identities.Count > 0) return;
            _document.Machines.AddRange(machines);
            _document.Identities.AddRange(identities);
            Save();
        }
    }

    // ---------------------------------------------------------------- file

    private void Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return;
                var json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json)) return;
                _document = JsonSerializer.Deserialize<StoreDocument>(json, JsonOptions) ?? new StoreDocument();
                LoadFailed = false;
                LoadError = null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Keep whatever was on disk; the UI surfaces this rather than silently
                // presenting an empty server list as if nothing was saved.
                LoadFailed = true;
                LoadError = ex.Message;
            }
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_document, JsonOptions));
        // File.Move with overwrite is atomic on the same volume, so a crash leaves
        // either the old file or the new one, never a truncated one.
        File.Move(temp, _path, overwrite: true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class StoreDocument
    {
        public List<RDMachine> Machines { get; set; } = [];
        public List<Identity> Identities { get; set; } = [];
    }
}
