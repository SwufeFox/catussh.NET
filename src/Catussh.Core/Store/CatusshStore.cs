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
    public (string CredentialsJson, string Nickname) ReadUURemoteAccount()
    {
        lock (_gate)
            return (SecretProtector.Unprotect(_document.UURemoteCredentials), _document.UURemoteNickname);
    }

    public void SaveUURemoteAccount(string credentialsJson, string nickname)
    {
        lock (_gate)
        {
            _document.UURemoteCredentials = SecretProtector.Protect(credentialsJson ?? "");
            _document.UURemoteNickname = nickname ?? "";
            Save();
        }
    }

    public RDFolder EnsureUURemoteFolder(string userID)
    {
        if (string.IsNullOrWhiteSpace(userID)) throw new ArgumentException("UU account user id is empty.", nameof(userID));
        lock (_gate)
        {
            var index = _document.FolderEntries.FindIndex(folder => folder.IsLinked && folder.SourceUserID == userID);
            if (index >= 0) return _document.FolderEntries[index];

            index = _document.FolderEntries.FindIndex(folder => folder.Name == "UU Remote");
            if (index >= 0)
            {
                var linked = _document.FolderEntries[index] with { Source = "uuRemote", SourceUserID = userID };
                _document.FolderEntries[index] = linked;
                Save();
                return linked;
            }

            var created = new RDFolder { Name = "UU Remote", Source = "uuRemote", SourceUserID = userID, SortIndex = NextFolderSortIndex() };
            _document.FolderEntries.Add(created);
            _document.Folders.Add(created.Name);
            Save();
            return created;
        }
    }

    public RDFolder? Folder(string name)
    {
        lock (_gate)
            return _document.FolderEntries.FirstOrDefault(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));
    }

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

    /// <summary>Named folders with their ownership, including empty and linked folders.</summary>
    public IReadOnlyList<RDFolder> FolderInfos
    {
        get
        {
            lock (_gate)
                return _document.FolderEntries.OrderBy(static folder => folder.SortIndex)
                    .ThenBy(static folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Folder names for the existing UI contract, including names present on saved servers.</summary>
    public IReadOnlyList<string> Folders
    {
        get
        {
            lock (_gate)
            {
                return _document.FolderEntries.Select(static folder => folder.Name)
                    .Concat(_document.Folders)
                    .Concat(_document.Machines.Select(static machine => machine.Group))
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    public bool CreateFolder(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return false;
        lock (_gate)
        {
            if (AllFolderNamesLocked().Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                return false;
            _document.Folders.Add(trimmed);
            _document.FolderEntries.Add(new RDFolder { Name = trimmed, SortIndex = NextFolderSortIndex() });
            Save();
            return true;
        }
    }

    public bool RenameFolder(string name, string newName)
    {
        var oldName = (name ?? "").Trim();
        var trimmed = (newName ?? "").Trim();
        if (oldName.Length == 0 || trimmed.Length == 0) return false;
        lock (_gate)
        {
            var entry = _document.FolderEntries.FirstOrDefault(folder => string.Equals(folder.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (entry?.IsLinked == true) return false;
            var folders = AllFolderNamesLocked().ToList();
            if (!folders.Any(existing => string.Equals(existing, oldName, StringComparison.OrdinalIgnoreCase))) return false;
            if (folders.Any(existing => !string.Equals(existing, oldName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))) return false;

            _document.Folders.RemoveAll(existing => string.Equals(existing, oldName, StringComparison.OrdinalIgnoreCase));
            _document.Folders.Add(trimmed);
            if (entry is not null)
            {
                var index = _document.FolderEntries.IndexOf(entry);
                _document.FolderEntries[index] = entry with { Name = trimmed };
            }
            else
            {
                _document.FolderEntries.Add(new RDFolder { Name = trimmed, SortIndex = NextFolderSortIndex() });
            }
            for (var i = 0; i < _document.Machines.Count; i++)
            {
                if (string.Equals(_document.Machines[i].Group, oldName, StringComparison.OrdinalIgnoreCase))
                    _document.Machines[i] = _document.Machines[i] with { Group = trimmed };
            }
            Save();
            return true;
        }
    }

    public bool RemoveFolder(string name)
    {
        var target = (name ?? "").Trim();
        if (target.Length == 0) return false;
        lock (_gate)
        {
            if (_document.FolderEntries.Any(folder => folder.IsLinked && string.Equals(folder.Name, target, StringComparison.OrdinalIgnoreCase)))
                return false;
            var exists = _document.Folders.RemoveAll(existing =>
                string.Equals(existing, target, StringComparison.OrdinalIgnoreCase)) > 0;
            exists |= _document.FolderEntries.RemoveAll(folder =>
                string.Equals(folder.Name, target, StringComparison.OrdinalIgnoreCase)) > 0;
            for (var i = 0; i < _document.Machines.Count; i++)
            {
                if (!string.Equals(_document.Machines[i].Group, target, StringComparison.OrdinalIgnoreCase)) continue;
                _document.Machines[i] = _document.Machines[i] with { Group = "" };
                exists = true;
            }
            if (exists) Save();
            return exists;
        }
    }

    public bool MoveMachineToFolder(string machineId, string folder)
    {
        var target = (folder ?? "").Trim();
        lock (_gate)
        {
            var index = _document.Machines.FindIndex(machine => machine.Id == machineId);
            if (index < 0) return false;
            var targetFolder = target.Length == 0 ? null : _document.FolderEntries.FirstOrDefault(entry =>
                string.Equals(entry.Name, target, StringComparison.OrdinalIgnoreCase));
            if (target.Length > 0 && !AllFolderNamesLocked().Any(existing => string.Equals(existing, target, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (targetFolder?.IsLinked == true && !_document.Machines[index].IsUURemoteDevice)
                return false;
            _document.Machines[index] = _document.Machines[index] with { Group = target };
            Save();
            return true;
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
            if (!string.IsNullOrWhiteSpace(machine.Group))
            {
                if (!_document.Folders.Any(folder => string.Equals(folder, machine.Group, StringComparison.OrdinalIgnoreCase)))
                    _document.Folders.Add(machine.Group);
                if (!_document.FolderEntries.Any(folder => string.Equals(folder.Name, machine.Group, StringComparison.OrdinalIgnoreCase)))
                    _document.FolderEntries.Add(new RDFolder { Name = machine.Group, SortIndex = NextFolderSortIndex() });
            }
            var index = _document.Machines.FindIndex(m => m.Id == machine.Id);
            if (index >= 0) _document.Machines[index] = machine;
            else _document.Machines.Add(machine);
            Save();
        }
    }

    public void ApplyMachineChanges(IReadOnlyCollection<RDMachine> upserts, IReadOnlyCollection<string> removeIDs)
    {
        lock (_gate)
        {
            if (removeIDs.Count > 0)
            {
                var removed = removeIDs.ToHashSet(StringComparer.Ordinal);
                _document.Machines.RemoveAll(machine => removed.Contains(machine.Id));
            }

            foreach (var machine in upserts)
            {
                if (!string.IsNullOrWhiteSpace(machine.Group))
                {
                    if (!_document.Folders.Any(folder => string.Equals(folder, machine.Group, StringComparison.OrdinalIgnoreCase)))
                        _document.Folders.Add(machine.Group);
                    if (!_document.FolderEntries.Any(folder => string.Equals(folder.Name, machine.Group, StringComparison.OrdinalIgnoreCase)))
                        _document.FolderEntries.Add(new RDFolder { Name = machine.Group, SortIndex = NextFolderSortIndex() });
                }
                var index = _document.Machines.FindIndex(existing => existing.Id == machine.Id);
                if (index >= 0) _document.Machines[index] = machine;
                else _document.Machines.Add(machine);
            }

            if (upserts.Count > 0 || removeIDs.Count > 0) Save();
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
    private IEnumerable<string> AllFolderNamesLocked() =>
        _document.FolderEntries.Select(static folder => folder.Name)
            .Concat(_document.Folders)
            .Concat(_document.Machines.Select(static machine => machine.Group))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private int NextFolderSortIndex() =>
        _document.FolderEntries.Count == 0 ? 0 : _document.FolderEntries.Max(static folder => folder.SortIndex) + 1;


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
                _document.Folders ??= [];
                _document.FolderEntries ??= [];
                _document.Machines ??= [];
                _document.Identities ??= [];
                foreach (var name in _document.Folders.Concat(_document.Machines.Select(static machine => machine.Group))
                             .Where(static name => !string.IsNullOrWhiteSpace(name)))
                {
                    if (_document.FolderEntries.Any(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                    _document.FolderEntries.Add(new RDFolder { Name = name, SortIndex = NextFolderSortIndex() });
                }
                LoadFailed = false;
                LoadError = null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
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
        var persisted = new StoreDocument
        {
            Folders = _document.Folders,
            FolderEntries = _document.FolderEntries,
            UURemoteCredentials = _document.UURemoteCredentials,
            UURemoteNickname = _document.UURemoteNickname,
            Machines = _document.Machines.Select(machine => machine with
            {
                // Re-enter the attachment init setter so VNC secrets stay encrypted.
                Attachment = new Dictionary<string, string>(machine.Attachment),
            }).ToList(),
            Identities = _document.Identities,
        };
        File.WriteAllText(temp, JsonSerializer.Serialize(persisted, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class StoreDocument
    {
        public List<string> Folders { get; set; } = [];
        public List<RDFolder> FolderEntries { get; set; } = [];
        public string UURemoteCredentials { get; set; } = "";
        public string UURemoteNickname { get; set; } = "";
        public List<RDMachine> Machines { get; set; } = [];
        public List<Identity> Identities { get; set; } = [];
    }
}
