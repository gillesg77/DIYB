using System.Text;
using System.Text.Json;
using DIYB.Core.Devices;

namespace DIYB.Core.Protocol;

/// <summary>Sérialise un état dans la forme attendue du firmware. Inverse de
/// <see cref="DeviceStateParser"/>, utilisé par le simulateur et par les exports.</summary>
public static class DeviceStateWriter
{
    public static string Write(DeviceState state)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (state.IsMultiChannel)
                WriteMultiChannel(writer, state);
            else
                WriteSingleChannel(writer, state);

            if (state.FirmwareVersion is not null)
                writer.WriteString("fwVersion", state.FirmwareVersion);

            if (state.Ssid is not null)
                writer.WriteString("ssid", state.Ssid);

            if (state.Rssi is { } rssi)
                writer.WriteNumber("rssi", rssi);

            if (state.MacAddress is not null)
                writer.WriteString("staMac", state.MacAddress);

            writer.WriteBoolean("otaUnlock", state.OtaUnlocked);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSingleChannel(Utf8JsonWriter writer, DeviceState state)
    {
        var channel = state.Channels.FirstOrDefault();
        if (channel is null)
            return;

        if (channel.Switch != SwitchState.Unknown)
            writer.WriteString("switch", channel.Switch.ToWire());

        if (channel.Startup != StartupMode.Unknown)
            writer.WriteString("startup", channel.Startup.ToWire());

        writer.WriteString("pulse", channel.PulseEnabled ? "on" : "off");
        writer.WriteNumber("pulseWidth", channel.PulseWidthMs);
    }

    private static void WriteMultiChannel(Utf8JsonWriter writer, DeviceState state)
    {
        writer.WriteStartArray("switches");
        foreach (var channel in state.Channels)
        {
            writer.WriteStartObject();
            writer.WriteString("switch", channel.Switch == SwitchState.Unknown ? "off" : channel.Switch.ToWire());
            writer.WriteNumber("outlet", channel.Outlet);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("configure");
        foreach (var channel in state.Channels)
        {
            writer.WriteStartObject();
            writer.WriteString("startup", channel.Startup == StartupMode.Unknown ? "off" : channel.Startup.ToWire());
            writer.WriteNumber("outlet", channel.Outlet);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("pulses");
        foreach (var channel in state.Channels)
        {
            writer.WriteStartObject();
            writer.WriteString("pulse", channel.PulseEnabled ? "on" : "off");
            writer.WriteNumber("width", channel.PulseWidthMs);
            writer.WriteNumber("outlet", channel.Outlet);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
