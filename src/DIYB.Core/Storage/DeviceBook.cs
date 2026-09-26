using System.Collections.Concurrent;

namespace DIYB.Core.Storage;

/// <summary>Métadonnées saisies par l'utilisateur pour un appareil. Elles survivent
/// aux redémarrages et aux changements d'adresse, la clé étant l'identifiant du
/// module.</summary>
public sealed record DeviceEntry
{
    public required string DeviceId { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string? Notes { get; init; }

    public DateTimeOffset? FirstSeen { get; init; }

    public DateTimeOffset? LastSeen { get; init; }
}

public sealed class DeviceBook
{
    private readonly ConcurrentDictionary<string, DeviceEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    public DeviceBook(string? path = null) => _path = path ?? AppPaths.DeviceBook;

    public event EventHandler<DeviceEntry>? EntryChanged;

    public IReadOnlyCollection<DeviceEntry> Entries => _entries.Values.ToArray();

    public DeviceEntry Get(string deviceId) =>
        _entries.GetValueOrDefault(deviceId) ?? new DeviceEntry { DeviceId = deviceId };

    public string DisplayName(string deviceId)
    {
        var name = Get(deviceId).Name;
        return string.IsNullOrWhiteSpace(name) ? deviceId : name;
    }

    public DeviceEntry Update(string deviceId, Func<DeviceEntry, DeviceEntry> change)
    {
        var updated = _entries.AddOrUpdate(
            deviceId,
            _ => change(new DeviceEntry { DeviceId = deviceId, FirstSeen = DateTimeOffset.Now }),
            (_, existing) => change(existing));

        EntryChanged?.Invoke(this, updated);
        return updated;
    }

    public DeviceEntry SetName(string deviceId, string? name) =>
        Update(deviceId, e => e with { Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim() });

    public DeviceEntry SetTags(string deviceId, IEnumerable<string> tags) =>
        Update(deviceId, e => e with
        {
            Tags = tags.Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        });

    public DeviceEntry Touch(string deviceId) => Update(deviceId, e => e with { LastSeen = DateTimeOffset.Now });

    public IReadOnlyList<string> AllTags() => _entries.Values
        .SelectMany(e => e.Tags)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var entries = await JsonStore.LoadAsync<List<DeviceEntry>>(_path, ct).ConfigureAwait(false);
        if (entries is null)
            return;

        foreach (var entry in entries)
            _entries[entry.DeviceId] = entry;
    }

    public Task SaveAsync(CancellationToken ct = default) =>
        JsonStore.SaveAsync(_path, _entries.Values.OrderBy(e => e.DeviceId, StringComparer.Ordinal).ToList(), ct);

    public Task ExportAsync(string path, CancellationToken ct = default) =>
        JsonStore.SaveAsync(path, _entries.Values.OrderBy(e => e.DeviceId, StringComparer.Ordinal).ToList(), ct);

    /// <summary>Fusionne un export dans le carnet courant. Les entrées importées
    /// écrasent les existantes de même identifiant.</summary>
    public async Task<int> ImportAsync(string path, CancellationToken ct = default)
    {
        var entries = await JsonStore.LoadAsync<List<DeviceEntry>>(path, ct).ConfigureAwait(false);
        if (entries is null)
            return 0;

        foreach (var entry in entries)
        {
            _entries[entry.DeviceId] = entry;
            EntryChanged?.Invoke(this, entry);
        }

        return entries.Count;
    }
}
