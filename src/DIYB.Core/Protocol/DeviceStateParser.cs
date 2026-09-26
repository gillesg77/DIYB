using System.Collections.Immutable;
using System.Text.Json;
using DIYB.Core.Devices;

namespace DIYB.Core.Protocol;

/// <summary>Reconstruit un <see cref="DeviceState"/> depuis le JSON du firmware,
/// charge utile commune à <c>/zeroconf/info</c> et aux segments TXT mDNS.</summary>
public static class DeviceStateParser
{
    /// <summary><paramref name="deviceId"/> vient de l'enveloppe : la charge utile
    /// d'état ne le répète pas toujours.</summary>
    public static DeviceState Parse(string deviceId, JsonElement data, string? deviceType = null)
    {
        var channels = data.TryGetProperty("switches", out var switches) && switches.ValueKind == JsonValueKind.Array
            ? ParseMultiChannel(data, switches)
            : ParseSingleChannel(data);

        return new DeviceState
        {
            DeviceId = deviceId,
            Channels = channels,
            FirmwareVersion = GetString(data, "fwVersion"),
            Ssid = GetString(data, "ssid"),
            Rssi = GetInt(data, "rssi") ?? GetInt(data, "signalStrength"),
            OtaUnlocked = GetBool(data, "otaUnlock") ?? false,
            MacAddress = GetString(data, "staMac"),
            DeviceType = deviceType,
        };
    }

    public static DeviceState Parse(string deviceId, string json, string? deviceType = null)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(deviceId, doc.RootElement, deviceType);
    }

    private static ImmutableArray<ChannelState> ParseSingleChannel(JsonElement data)
    {
        // Un appareil vu par mDNS mais jamais interrogé n'a aucun de ces champs.
        var hasSwitch = data.TryGetProperty("switch", out _);
        var hasStartup = data.TryGetProperty("startup", out _);
        if (!hasSwitch && !hasStartup)
            return ImmutableArray<ChannelState>.Empty;

        return ImmutableArray.Create(new ChannelState
        {
            Outlet = 0,
            Switch = ProtocolEnums.ToSwitchState(GetString(data, "switch")),
            Startup = ProtocolEnums.ToStartupMode(GetString(data, "startup")),
            PulseEnabled = GetString(data, "pulse") == "on",
            PulseWidthMs = GetInt(data, "pulseWidth") ?? 500,
        });
    }

    private static ImmutableArray<ChannelState> ParseMultiChannel(JsonElement data, JsonElement switches)
    {
        // « configure » et « pulses » sont indexés par outlet, sans garantie d'ordre
        // ni de longueur par rapport à « switches ».
        var startups = IndexByOutlet(data, "configure");
        var pulses = IndexByOutlet(data, "pulses");

        var builder = ImmutableArray.CreateBuilder<ChannelState>();
        foreach (var entry in switches.EnumerateArray())
        {
            var outlet = GetInt(entry, "outlet") ?? builder.Count;
            startups.TryGetValue(outlet, out var startup);
            pulses.TryGetValue(outlet, out var pulse);

            builder.Add(new ChannelState
            {
                Outlet = outlet,
                Switch = ProtocolEnums.ToSwitchState(GetString(entry, "switch")),
                Startup = ProtocolEnums.ToStartupMode(GetString(startup, "startup")),
                PulseEnabled = GetString(pulse, "pulse") == "on",
                PulseWidthMs = GetInt(pulse, "width") ?? 500,
            });
        }

        builder.Sort((a, b) => a.Outlet.CompareTo(b.Outlet));
        return builder.ToImmutable();
    }

    private static Dictionary<int, JsonElement> IndexByOutlet(JsonElement data, string property)
    {
        var map = new Dictionary<int, JsonElement>();
        if (!data.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return map;

        var fallback = 0;
        foreach (var entry in array.EnumerateArray())
            map[GetInt(entry, "outlet") ?? fallback++] = entry;

        return map;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return null;

        // Certaines versions de firmware sérialisent le RSSI en chaîne.
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => null,
        };
    }

    private static bool? GetBool(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when v.TryGetInt32(out var n) => n != 0,
            _ => null,
        };
    }
}
