using DIYB.Core.Devices;

namespace DIYB.Core.Firmware;

public enum UpdateStatus
{
    Unknown,
    UpToDate,
    UpdateAvailable,
    /// <summary>Version supérieure à toute référence connue.</summary>
    Ahead,
}

public sealed record FirmwareAdvice
{
    public required string DeviceId { get; init; }

    public required FirmwareVersion Current { get; init; }

    public FirmwareRelease? Candidate { get; init; }

    public required UpdateStatus Status { get; init; }

    /// <summary>Référence issue du seul parc, faute de catalogue couvrant ce modèle.</summary>
    public bool FromFleetOnly { get; init; }
}

/// <summary>Compare la version de chaque appareil aux catalogues disponibles, et à
/// défaut au parc lui-même.</summary>
public sealed class FirmwareAdvisor
{
    private readonly IReadOnlyList<IFirmwareCatalog> _catalogs;

    public FirmwareAdvisor(IReadOnlyList<IFirmwareCatalog> catalogs) => _catalogs = catalogs;

    public IReadOnlyList<FirmwareRelease> Releases { get; private set; } = Array.Empty<FirmwareRelease>();

    public DateTimeOffset? LastRefresh { get; private set; }

    /// <summary>Interroge chaque catalogue. Une source injoignable n'interrompt pas
    /// les autres ; ses erreurs sont rendues à l'appelant.</summary>
    public async Task<IReadOnlyList<Exception>> RefreshAsync(CancellationToken ct = default)
    {
        var releases = new List<FirmwareRelease>();
        var failures = new List<Exception>();

        foreach (var catalog in _catalogs)
        {
            try
            {
                releases.AddRange(await catalog.GetReleasesAsync(ct).ConfigureAwait(false));
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException)
            {
                failures.Add(e);
            }
        }

        Releases = releases;
        LastRefresh = DateTimeOffset.Now;
        return failures;
    }

    public IReadOnlyList<FirmwareAdvice> Advise(IEnumerable<DiyDevice> devices, bool includePrerelease = false)
    {
        var fleet = devices.ToArray();
        var fleetBest = BestPerModel(fleet);

        return fleet.Select(device => Advise(device, fleetBest, includePrerelease)).ToArray();
    }

    private FirmwareAdvice Advise(DiyDevice device, IReadOnlyDictionary<string, FirmwareVersion> fleetBest, bool includePrerelease)
    {
        var current = FirmwareVersion.Parse(device.State?.FirmwareVersion);
        var model = device.State?.DeviceType ?? "*";

        var candidate = Releases
            .Where(r => includePrerelease || !string.Equals(r.Channel, "prerelease", StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Model == "*" || string.Equals(r.Model, model, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => FirmwareVersion.Parse(r.Version))
            .FirstOrDefault();

        if (current.IsEmpty)
            return new FirmwareAdvice { DeviceId = device.DeviceId, Current = current, Candidate = candidate, Status = UpdateStatus.Unknown };

        if (candidate is not null)
        {
            var latest = FirmwareVersion.Parse(candidate.Version);
            return new FirmwareAdvice
            {
                DeviceId = device.DeviceId,
                Current = current,
                Candidate = candidate,
                Status = current < latest ? UpdateStatus.UpdateAvailable : current > latest ? UpdateStatus.Ahead : UpdateStatus.UpToDate,
            };
        }

        // Sans catalogue, un appareil en retard sur ses homologues reste détectable.
        if (fleetBest.TryGetValue(model, out var best) && current < best)
        {
            return new FirmwareAdvice
            {
                DeviceId = device.DeviceId,
                Current = current,
                Status = UpdateStatus.UpdateAvailable,
                FromFleetOnly = true,
            };
        }

        return new FirmwareAdvice { DeviceId = device.DeviceId, Current = current, Status = UpdateStatus.Unknown };
    }

    private static Dictionary<string, FirmwareVersion> BestPerModel(IEnumerable<DiyDevice> devices)
    {
        var best = new Dictionary<string, FirmwareVersion>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            var version = FirmwareVersion.Parse(device.State?.FirmwareVersion);
            if (version.IsEmpty)
                continue;

            var model = device.State?.DeviceType ?? "*";
            if (!best.TryGetValue(model, out var current) || version > current)
                best[model] = version;
        }

        return best;
    }
}
