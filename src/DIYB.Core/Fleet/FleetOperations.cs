using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;
using DIYB.Core.Discovery;
using DIYB.Core.Protocol;
using DIYB.Core.Storage;

namespace DIYB.Core.Fleet;

public sealed record OperationResult
{
    public required string DeviceId { get; init; }

    public required bool Success { get; init; }

    public ErrorCode? Error { get; init; }

    public string? DebugMessage { get; init; }

    public static OperationResult Ok(string deviceId) => new() { DeviceId = deviceId, Success = true };

    public static OperationResult Failed(string deviceId, DiyException e) => new()
    {
        DeviceId = deviceId,
        Success = false,
        Error = e.Code,
        DebugMessage = e.Message,
    };
}

public sealed record FleetProgress(int Completed, int Total, string DeviceId);

/// <summary>Actions groupées sur plusieurs appareils, avec parallélisme borné et
/// collecte des échecs plutôt qu'interruption au premier.</summary>
public sealed class FleetOperations
{
    private readonly DiyClient _client;
    private readonly DeviceRegistry _registry;

    public FleetOperations(DiyClient client, DeviceRegistry registry)
    {
        _client = client;
        _registry = registry;
    }

    /// <summary>Au-delà, le point d'accès Wi-Fi commence à perdre des requêtes.</summary>
    public int MaxParallelism { get; init; } = 6;

    public Task<IReadOnlyList<OperationResult>> SetSwitchAsync(
        IEnumerable<DiyDevice> devices, bool on, IProgress<FleetProgress>? progress = null, CancellationToken ct = default) =>
        RunAsync(devices, (device, token) => _client.SetSwitchAsync(device, null, on, token), progress, ct);

    public Task<IReadOnlyList<OperationResult>> SetStartupAsync(
        IEnumerable<DiyDevice> devices, StartupMode mode, IProgress<FleetProgress>? progress = null, CancellationToken ct = default) =>
        RunAsync(devices, (device, token) => _client.SetStartupAsync(device, null, mode, token), progress, ct);

    public Task<IReadOnlyList<OperationResult>> SetPulseAsync(
        IEnumerable<DiyDevice> devices, bool enabled, int widthMs, IProgress<FleetProgress>? progress = null, CancellationToken ct = default) =>
        RunAsync(devices, (device, token) => _client.SetPulseAsync(device, null, enabled, widthMs, token), progress, ct);

    public Task<IReadOnlyList<OperationResult>> RefreshAsync(
        IEnumerable<DiyDevice> devices, IProgress<FleetProgress>? progress = null, CancellationToken ct = default) =>
        RunAsync(devices, async (device, token) =>
        {
            var state = await _client.GetInfoAsync(device, token).ConfigureAwait(false);
            _registry.ApplyState(device.DeviceId, state);
        }, progress, ct);

    /// <summary>Écarts d'un parc par rapport à un profil, sans rien modifier.</summary>
    public IReadOnlyList<PlannedChange> PlanProfile(IEnumerable<DiyDevice> devices, ConfigProfile profile) =>
        devices.SelectMany(profile.Plan).ToArray();

    public Task<IReadOnlyList<OperationResult>> ApplyProfileAsync(
        IEnumerable<DiyDevice> devices, ConfigProfile profile, IProgress<FleetProgress>? progress = null, CancellationToken ct = default)
    {
        var targets = devices.Where(d => profile.Plan(d).Count > 0).ToArray();

        return RunAsync(targets, async (device, token) =>
        {
            foreach (var outlet in Outlets(device))
            {
                if (profile.Startup is { } startup)
                    await _client.SetStartupAsync(device, outlet, startup, token).ConfigureAwait(false);

                if (profile.PulseEnabled is { } pulse)
                {
                    var width = profile.PulseWidthMs ?? device.State?.Channel(outlet)?.PulseWidthMs ?? 500;
                    await _client.SetPulseAsync(device, outlet, pulse, width, token).ConfigureAwait(false);
                }
                else if (profile.PulseWidthMs is { } widthOnly && (device.State?.Channel(outlet)?.PulseEnabled ?? false))
                {
                    await _client.SetPulseAsync(device, outlet, true, widthOnly, token).ConfigureAwait(false);
                }
            }

            var state = await _client.GetInfoAsync(device, token).ConfigureAwait(false);
            _registry.ApplyState(device.DeviceId, state);
        }, progress, ct);
    }

    /// <summary>Fait battre le relais pour repérer physiquement l'appareil, puis
    /// rétablit l'état initial.</summary>
    public async Task IdentifyAsync(DiyDevice device, int blinks = 3, CancellationToken ct = default)
    {
        var original = device.State?.Channel(0)?.Switch ?? SwitchState.Off;
        var wasOn = original == SwitchState.On;

        try
        {
            for (var i = 0; i < blinks; i++)
            {
                await _client.SetSwitchAsync(device, null, !wasOn, ct).ConfigureAwait(false);
                await Task.Delay(400, ct).ConfigureAwait(false);
                await _client.SetSwitchAsync(device, null, wasOn, ct).ConfigureAwait(false);
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Une annulation en plein clignotement ne doit pas laisser le relais inversé.
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _client.SetSwitchAsync(device, null, wasOn, restore.Token).ConfigureAwait(false);
            }
            catch (DiyException)
            {
            }
        }
    }

    private static IEnumerable<int> Outlets(DiyDevice device)
    {
        var channels = device.State?.Channels ?? default;
        return channels.IsDefaultOrEmpty ? new[] { 0 } : channels.Select(c => c.Outlet).ToArray();
    }

    private async Task<IReadOnlyList<OperationResult>> RunAsync(
        IEnumerable<DiyDevice> devices,
        Func<DiyDevice, CancellationToken, Task> action,
        IProgress<FleetProgress>? progress,
        CancellationToken ct)
    {
        var targets = devices.ToArray();
        var results = new OperationResult[targets.Length];
        var completed = 0;

        using var throttle = new SemaphoreSlim(MaxParallelism);

        var tasks = targets.Select(async (device, index) =>
        {
            await throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await action(device, ct).ConfigureAwait(false);
                results[index] = OperationResult.Ok(device.DeviceId);
            }
            catch (DiyException e)
            {
                results[index] = OperationResult.Failed(device.DeviceId, e);
            }
            finally
            {
                throttle.Release();
                progress?.Report(new FleetProgress(Interlocked.Increment(ref completed), targets.Length, device.DeviceId));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
