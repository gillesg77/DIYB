using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using DIYB.Core.Devices;
using DIYB.Core.Protocol;

namespace DIYB.Core.Simulation;

/// <summary>Appareils factices répondant au protocole en mémoire. Permet de faire
/// tourner et de démontrer l'application sans matériel.</summary>
public sealed class SimulatedDeviceHost : IDiyTransport
{
    private readonly ConcurrentDictionary<string, DeviceState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random;

    public SimulatedDeviceHost(int seed = 1789) => _random = new Random(seed);

    /// <summary>Latence injectée dans chaque réponse.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(60);

    /// <summary>Proportion de requêtes rejetées, pour exercer la gestion d'erreurs.</summary>
    public double FailureRate { get; set; }

    public event EventHandler<string>? StateChanged;

    public DiyDevice Create(string deviceId, int channels = 1, string deviceType = "diy_plug", string firmware = "3.7.2")
    {
        var list = Enumerable.Range(0, Math.Max(1, channels)).Select(outlet => new ChannelState
        {
            Outlet = outlet,
            Switch = outlet % 2 == 0 ? SwitchState.On : SwitchState.Off,
            Startup = StartupMode.Keep,
            PulseEnabled = false,
            PulseWidthMs = 500,
        });

        var state = new DeviceState
        {
            DeviceId = deviceId,
            Channels = list.ToImmutableArray(),
            FirmwareVersion = firmware,
            Ssid = "LAB-WIFI",
            Rssi = -45 - _random.Next(0, 35),
            DeviceType = deviceType,
            MacAddress = "02:00:00:" + deviceId[^6..].Insert(2, ":").Insert(5, ":"),
        };

        _states[deviceId] = state;

        return new DiyDevice
        {
            DeviceId = deviceId,
            Address = IPAddress.Parse("127.0.0.1"),
            Port = 8081,
            HostName = $"eWeLink_{deviceId}.local",
            State = state,
            IsSimulated = true,
        };
    }

    public async Task<string> PostAsync(DiyDevice device, string endpoint, string body, CancellationToken ct)
    {
        if (Latency > TimeSpan.Zero)
            await Task.Delay(Latency, ct).ConfigureAwait(false);

        if (FailureRate > 0 && _random.NextDouble() < FailureRate)
            return Envelope(400, null);

        if (!_states.TryGetValue(device.DeviceId, out var state))
            return Envelope(403, null);

        using var request = JsonDocument.Parse(body);
        var data = request.RootElement.TryGetProperty("data", out var d) ? d : default;

        switch (endpoint)
        {
            case DiyEndpoints.Info:
                return Envelope(0, DeviceStateWriter.Write(state));

            case DiyEndpoints.Switch:
            case DiyEndpoints.Switches:
                Mutate(device.DeviceId, ApplySwitch(state, data));
                return Envelope(0, null);

            case DiyEndpoints.Startup:
                Mutate(device.DeviceId, ApplyStartup(state, data));
                return Envelope(0, null);

            case DiyEndpoints.Pulse:
            case DiyEndpoints.Pulses:
                Mutate(device.DeviceId, ApplyPulse(state, data));
                return Envelope(0, null);

            case DiyEndpoints.SignalStrength:
                // Le niveau dérive légèrement pour animer l'historique de signal.
                var rssi = Math.Clamp((state.Rssi ?? -60) + _random.Next(-2, 3), -95, -30);
                Mutate(device.DeviceId, state with { Rssi = rssi });
                return Envelope(0, $"{{\"signalStrength\":{rssi}}}");

            case DiyEndpoints.OtaUnlock:
                Mutate(device.DeviceId, state with { OtaUnlocked = true });
                return Envelope(0, null);

            case DiyEndpoints.OtaFlash:
                return state.OtaUnlocked ? Envelope(0, null) : Envelope(401, null);

            case DiyEndpoints.Wifi:
                return Envelope(0, null);

            default:
                return Envelope(404, null);
        }
    }

    private void Mutate(string deviceId, DeviceState state)
    {
        _states[deviceId] = state;
        StateChanged?.Invoke(this, deviceId);
    }

    private static DeviceState ApplySwitch(DeviceState state, JsonElement data)
    {
        if (data.TryGetProperty("switch", out var single))
            return WithChannels(state, (channel, _) => channel with { Switch = ProtocolEnums.ToSwitchState(single.GetString()) });

        if (!data.TryGetProperty("switches", out var array) || array.ValueKind != JsonValueKind.Array)
            return state;

        var wanted = array.EnumerateArray()
            .Where(e => e.TryGetProperty("outlet", out _))
            .ToDictionary(
                e => e.GetProperty("outlet").GetInt32(),
                e => ProtocolEnums.ToSwitchState(e.TryGetProperty("switch", out var s) ? s.GetString() : null));

        return WithChannels(state, (channel, outlet) =>
            wanted.TryGetValue(outlet, out var value) ? channel with { Switch = value } : channel);
    }

    private static DeviceState ApplyStartup(DeviceState state, JsonElement data)
    {
        if (data.TryGetProperty("startup", out var single))
            return WithChannels(state, (channel, _) => channel with { Startup = ProtocolEnums.ToStartupMode(single.GetString()) });

        if (!data.TryGetProperty("configure", out var array) || array.ValueKind != JsonValueKind.Array)
            return state;

        var wanted = array.EnumerateArray()
            .Where(e => e.TryGetProperty("outlet", out _))
            .ToDictionary(
                e => e.GetProperty("outlet").GetInt32(),
                e => ProtocolEnums.ToStartupMode(e.TryGetProperty("startup", out var s) ? s.GetString() : null));

        return WithChannels(state, (channel, outlet) =>
            wanted.TryGetValue(outlet, out var value) ? channel with { Startup = value } : channel);
    }

    private static DeviceState ApplyPulse(DeviceState state, JsonElement data)
    {
        if (data.TryGetProperty("pulse", out var single))
        {
            var width = data.TryGetProperty("pulseWidth", out var w) && w.TryGetInt32(out var value) ? value : 500;
            return WithChannels(state, (channel, _) => channel with
            {
                PulseEnabled = single.GetString() == "on",
                PulseWidthMs = width,
            });
        }

        if (!data.TryGetProperty("pulses", out var array) || array.ValueKind != JsonValueKind.Array)
            return state;

        var wanted = array.EnumerateArray()
            .Where(e => e.TryGetProperty("outlet", out _))
            .ToDictionary(
                e => e.GetProperty("outlet").GetInt32(),
                e => (
                    Enabled: e.TryGetProperty("pulse", out var p) && p.GetString() == "on",
                    Width: e.TryGetProperty("width", out var w) && w.TryGetInt32(out var value) ? value : 500));

        return WithChannels(state, (channel, outlet) =>
            wanted.TryGetValue(outlet, out var entry)
                ? channel with { PulseEnabled = entry.Enabled, PulseWidthMs = entry.Width }
                : channel);
    }

    private static DeviceState WithChannels(DeviceState state, Func<ChannelState, int, ChannelState> change) =>
        state with { Channels = state.Channels.Select(c => change(c, c.Outlet)).ToImmutableArray() };

    private static string Envelope(int error, string? data) =>
        $"{{\"seq\":1,\"error\":{error},\"data\":{data ?? "{}"}}}";
}
