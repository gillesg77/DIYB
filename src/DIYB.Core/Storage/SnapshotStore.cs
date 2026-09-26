using DIYB.Core.Devices;

namespace DIYB.Core.Storage;

public sealed record DeviceSnapshot
{
    public required string DeviceId { get; init; }

    public required DateTimeOffset TakenAt { get; init; }

    public required DeviceState State { get; init; }

    public string? Label { get; init; }
}

/// <summary>Sauvegardes de configuration, un fichier par appareil et par date.</summary>
public sealed class SnapshotStore
{
    private readonly string _directory;

    public SnapshotStore(string? directory = null) => _directory = directory ?? AppPaths.Snapshots;

    public async Task<string> CaptureAsync(DiyDevice device, string? label = null, CancellationToken ct = default)
    {
        var snapshot = new DeviceSnapshot
        {
            DeviceId = device.DeviceId,
            TakenAt = DateTimeOffset.Now,
            State = device.State,
            Label = label,
        };

        var path = Path.Combine(_directory, $"{device.DeviceId}-{snapshot.TakenAt:yyyyMMdd-HHmmss}.json");
        await JsonStore.SaveAsync(path, snapshot, ct).ConfigureAwait(false);
        return path;
    }

    public IReadOnlyList<string> List(string deviceId)
    {
        if (!Directory.Exists(_directory))
            return Array.Empty<string>();

        return Directory.GetFiles(_directory, $"{deviceId}-*.json")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .ToArray();
    }

    public Task<DeviceSnapshot?> LoadAsync(string path, CancellationToken ct = default) =>
        JsonStore.LoadAsync<DeviceSnapshot>(path, ct);
}
