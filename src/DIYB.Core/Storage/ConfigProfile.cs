using DIYB.Core.Devices;

namespace DIYB.Core.Storage;

public enum ConfigProperty
{
    Switch,
    Startup,
    Pulse,
    PulseWidth,
}

public sealed record PlannedChange
{
    public required string DeviceId { get; init; }

    public required int Outlet { get; init; }

    public required ConfigProperty Property { get; init; }

    public string? From { get; init; }

    public required string To { get; init; }
}

public enum ComplianceState
{
    Unknown,
    Compliant,
    Drifted,
}

/// <summary>Configuration de référence appliquée à un ensemble d'appareils. Une
/// propriété laissée nulle n'est pas gouvernée par le profil.</summary>
public sealed record ConfigProfile
{
    public required string Name { get; init; }

    public StartupMode? Startup { get; init; }

    public bool? PulseEnabled { get; init; }

    public int? PulseWidthMs { get; init; }

    /// <summary>Restreint le profil aux appareils portant l'une de ces étiquettes.
    /// Vide, le profil s'applique à tous.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public bool Governs(ConfigProperty property) => property switch
    {
        ConfigProperty.Startup => Startup.HasValue,
        ConfigProperty.Pulse => PulseEnabled.HasValue,
        ConfigProperty.PulseWidth => PulseWidthMs.HasValue,
        _ => false,
    };

    public bool AppliesTo(IReadOnlyList<string> deviceTags) =>
        Tags.Count == 0 || Tags.Any(t => deviceTags.Contains(t, StringComparer.OrdinalIgnoreCase));

    /// <summary>Écarts entre l'état courant et la référence, sans rien appliquer.</summary>
    public IReadOnlyList<PlannedChange> Plan(DiyDevice device)
    {
        var changes = new List<PlannedChange>();
        var channels = device.State?.Channels ?? default;
        if (channels.IsDefaultOrEmpty)
            return changes;

        foreach (var channel in channels)
        {
            if (Startup is { } startup && channel.Startup != startup)
            {
                changes.Add(new PlannedChange
                {
                    DeviceId = device.DeviceId,
                    Outlet = channel.Outlet,
                    Property = ConfigProperty.Startup,
                    From = channel.Startup.ToString(),
                    To = startup.ToString(),
                });
            }

            if (PulseEnabled is { } pulse && channel.PulseEnabled != pulse)
            {
                changes.Add(new PlannedChange
                {
                    DeviceId = device.DeviceId,
                    Outlet = channel.Outlet,
                    Property = ConfigProperty.Pulse,
                    From = channel.PulseEnabled ? "On" : "Off",
                    To = pulse ? "On" : "Off",
                });
            }

            // La largeur n'a de sens qu'avec le mode impulsionnel actif.
            var pulseTarget = PulseEnabled ?? channel.PulseEnabled;
            if (PulseWidthMs is { } width && pulseTarget && channel.PulseWidthMs != width)
            {
                changes.Add(new PlannedChange
                {
                    DeviceId = device.DeviceId,
                    Outlet = channel.Outlet,
                    Property = ConfigProperty.PulseWidth,
                    From = channel.PulseWidthMs.ToString(),
                    To = width.ToString(),
                });
            }
        }

        return changes;
    }

    public ComplianceState Evaluate(DiyDevice device)
    {
        var channels = device.State?.Channels ?? default;
        if (channels.IsDefaultOrEmpty)
            return ComplianceState.Unknown;

        if (channels.Any(c => c.Startup == StartupMode.Unknown && Startup.HasValue))
            return ComplianceState.Unknown;

        return Plan(device).Count == 0 ? ComplianceState.Compliant : ComplianceState.Drifted;
    }
}

public sealed class ProfileStore
{
    private readonly string _path;
    private List<ConfigProfile> _profiles = new();

    public ProfileStore(string? path = null) => _path = path ?? AppPaths.Profiles;

    public IReadOnlyList<ConfigProfile> Profiles => _profiles;

    public ConfigProfile? Find(string name) =>
        _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Save(ConfigProfile profile)
    {
        _profiles.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        _profiles.Add(profile);
    }

    public void Remove(string name) =>
        _profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task LoadAsync(CancellationToken ct = default) =>
        _profiles = await JsonStore.LoadAsync<List<ConfigProfile>>(_path, ct).ConfigureAwait(false) ?? new List<ConfigProfile>();

    public Task PersistAsync(CancellationToken ct = default) => JsonStore.SaveAsync(_path, _profiles, ct);
}
