using System.Security.Cryptography;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;
using DIYB.Core.Discovery;
using DIYB.Core.Protocol;

namespace DIYB.Core.Ota;

public enum OtaPhase
{
    Checking,
    Unlocking,
    Serving,
    Downloading,
    Rebooting,
    Verifying,
    Completed,
    Failed,
}

public sealed record OtaProgress(OtaPhase Phase, long BytesSent = 0, long TotalBytes = 0)
{
    public double Ratio => TotalBytes > 0 ? Math.Clamp((double)BytesSent / TotalBytes, 0, 1) : 0;
}

public sealed record OtaOptions
{
    /// <summary>En dessous de ce niveau, un téléchargement de plusieurs centaines de
    /// kilo-octets a peu de chances d'aboutir.</summary>
    public int MinimumRssi { get; init; } = -70;

    /// <summary>Passe outre le contrôle de signal.</summary>
    public bool IgnoreSignalCheck { get; init; }

    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan RebootTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed class OtaFlasher
{
    private readonly DiyClient _client;
    private readonly DeviceRegistry? _registry;
    private readonly ApiLog _log;

    public OtaFlasher(DiyClient client, ApiLog log, DeviceRegistry? registry = null)
    {
        _client = client;
        _registry = registry;
        _log = log;
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task FlashAsync(
        DiyDevice device,
        string firmwarePath,
        IProgress<OtaProgress>? progress = null,
        OtaOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new OtaOptions();
        progress?.Report(new OtaProgress(OtaPhase.Checking));

        if (!File.Exists(firmwarePath))
        {
            throw new DiyException(ErrorCode.OtaFileMissing, $"Firmware not found: {firmwarePath}.",
                new Dictionary<string, object?> { ["path"] = firmwarePath });
        }

        var size = new FileInfo(firmwarePath).Length;
        var sha256 = await ComputeSha256Async(firmwarePath, ct).ConfigureAwait(false);

        await EnsureSignalAsync(device, options, ct).ConfigureAwait(false);

        var localAddress = LocalAddressPicker.ForDevice(device.Address);
        if (localAddress is null)
        {
            throw new DiyException(ErrorCode.OtaNoLocalAddress,
                $"No local interface on the same subnet as {device.Address}.",
                new Dictionary<string, object?> { ["deviceId"] = device.DeviceId, ["address"] = device.Address.ToString() });
        }

        using var server = FirmwareServer.Start(localAddress, firmwarePath);
        var transferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        server.BytesServed += (_, sent) => progress?.Report(new OtaProgress(OtaPhase.Downloading, sent, size));
        server.TransferCompleted += (_, _) => transferred.TrySetResult();

        progress?.Report(new OtaProgress(OtaPhase.Unlocking));
        await _client.UnlockOtaAsync(device, ct).ConfigureAwait(false);

        progress?.Report(new OtaProgress(OtaPhase.Serving, 0, size));
        _log.Info("ota.started", device.DeviceId);
        await _client.FlashAsync(device, server.Url, sha256, ct).ConfigureAwait(false);

        await WaitAsync(transferred.Task, options.DownloadTimeout, ErrorCode.Timeout,
            $"Device {device.DeviceId} did not download the firmware in time.", device.DeviceId, ct).ConfigureAwait(false);

        progress?.Report(new OtaProgress(OtaPhase.Rebooting, size, size));
        await VerifyReturnAsync(device, options, progress, ct).ConfigureAwait(false);

        progress?.Report(new OtaProgress(OtaPhase.Completed, size, size));
        _log.Info("ota.completed", device.DeviceId);
    }

    private async Task EnsureSignalAsync(DiyDevice device, OtaOptions options, CancellationToken ct)
    {
        if (options.IgnoreSignalCheck)
            return;

        int rssi;
        try
        {
            rssi = await _client.GetSignalStrengthAsync(device, ct).ConfigureAwait(false);
        }
        catch (DiyException)
        {
            rssi = device.State?.Rssi ?? options.MinimumRssi;
        }

        if (rssi < options.MinimumRssi)
        {
            throw new DiyException(ErrorCode.OtaSignalTooWeak,
                $"Signal {rssi} dBm below the {options.MinimumRssi} dBm threshold.",
                new Dictionary<string, object?>
                {
                    ["deviceId"] = device.DeviceId,
                    ["rssi"] = rssi,
                    ["threshold"] = options.MinimumRssi,
                });
        }
    }

    /// <summary>Attend que l'appareil réapparaisse après redémarrage et relit sa
    /// version. Sans registre de découverte, l'étape est sautée.</summary>
    private async Task VerifyReturnAsync(DiyDevice device, OtaOptions options, IProgress<OtaProgress>? progress, CancellationToken ct)
    {
        if (_registry is null)
            return;

        progress?.Report(new OtaProgress(OtaPhase.Verifying));

        var deadline = DateTimeOffset.Now + options.RebootTimeout;
        while (DateTimeOffset.Now < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            _registry.Refresh();

            var current = _registry.Find(device.DeviceId);
            if (current is null)
                continue;

            try
            {
                var state = await _client.GetInfoAsync(current, ct).ConfigureAwait(false);
                _registry.ApplyState(device.DeviceId, state);
                return;
            }
            catch (DiyException)
            {
                // Le module répond au mDNS avant d'ouvrir son serveur HTTP.
            }
        }

        throw new DiyException(ErrorCode.OtaDeviceDidNotReturn,
            $"Device {device.DeviceId} did not come back after flashing.",
            new Dictionary<string, object?> { ["deviceId"] = device.DeviceId });
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout, ErrorCode code, string message, string deviceId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var completed = await Task.WhenAny(task, Task.Delay(timeout, timeoutCts.Token)).ConfigureAwait(false);
        timeoutCts.Cancel();

        if (completed != task)
            throw new DiyException(code, message, new Dictionary<string, object?> { ["deviceId"] = deviceId });

        await task.ConfigureAwait(false);
    }
}
