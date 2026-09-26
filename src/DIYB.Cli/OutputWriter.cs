using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DIYB.Core.Devices;
using DIYB.Core.Storage;
using DIYB.Localization;

namespace DIYB.Cli;

/// <summary>Projection d'un appareil pour la sortie JSON. Les noms de champs sont
/// stables et en anglais : c'est une interface machine, pas un affichage.</summary>
internal sealed record DeviceView
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Address { get; init; }

    public int Port { get; init; }

    public string? Type { get; init; }

    public string? Firmware { get; init; }

    public string? Ssid { get; init; }

    public int? Rssi { get; init; }

    /// <summary><c>diy</c> ou <c>cloud</c>.</summary>
    public required string Mode { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ChannelView> Channels { get; init; } = Array.Empty<ChannelView>();

    public static DeviceView From(DiyDevice device, DeviceBook book)
    {
        var entry = book.Get(device.DeviceId);
        var state = device.State;

        return new DeviceView
        {
            Id = device.DeviceId,
            Name = string.IsNullOrWhiteSpace(entry.Name) ? device.DeviceId : entry.Name,
            Address = device.Address.ToString(),
            Port = device.Port,
            Type = state?.DeviceType,
            Firmware = state?.FirmwareVersion,
            Ssid = state?.Ssid,
            Rssi = state?.Rssi,
            Mode = state?.IsControllable ?? true ? "diy" : "cloud",
            Tags = entry.Tags,
            Channels = state?.Channels.Select(ChannelView.From).ToArray() ?? Array.Empty<ChannelView>(),
        };
    }
}

internal sealed record ChannelView
{
    public int Outlet { get; init; }

    public required string Switch { get; init; }

    public required string Startup { get; init; }

    public bool Pulse { get; init; }

    public int PulseWidthMs { get; init; }

    public static ChannelView From(ChannelState channel) => new()
    {
        Outlet = channel.Outlet,
        Switch = channel.Switch.ToString().ToLowerInvariant(),
        Startup = channel.Startup.ToString().ToLowerInvariant(),
        Pulse = channel.PulseEnabled,
        PulseWidthMs = channel.PulseWidthMs,
    };
}

internal static class OutputWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void WriteJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));

    public static void WriteTable(IReadOnlyList<DeviceView> devices)
    {
        if (devices.Count == 0)
        {
            Console.WriteLine(Localizer.Current.Format("devices.count", 0));
            return;
        }

        var loc = Localizer.Current;
        var rows = new List<string[]>
        {
            new[]
            {
                loc["column.name"], loc["column.id"], loc["column.address"], loc["column.state"],
                loc["column.powerOn"], loc["column.inching"], loc["column.signal"], loc["column.firmware"],
            },
        };

        foreach (var device in devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            rows.Add(new[]
            {
                device.Name,
                device.Id,
                device.Address,
                device.Mode == "cloud" ? loc["device.cloudBadge"] : Join(device.Channels, c => c.Switch.ToUpperInvariant()),
                Join(device.Channels, c => c.Startup.ToUpperInvariant()),
                Join(device.Channels, c => c.Pulse ? $"{c.PulseWidthMs}ms" : "off"),
                device.Rssi is { } rssi ? $"{rssi} dBm" : "-",
                device.Firmware ?? "-",
            });
        }

        Write(rows);
    }

    public static void WriteChanges(IReadOnlyList<PlannedChange> changes, DeviceBook book)
    {
        var rows = new List<string[]> { new[] { "device", "outlet", "property", "from", "to" } };

        foreach (var change in changes)
        {
            rows.Add(new[]
            {
                book.DisplayName(change.DeviceId),
                change.Outlet.ToString(),
                change.Property.ToString(),
                change.From ?? "-",
                change.To,
            });
        }

        Write(rows);
    }

    /// <summary>Colonnes alignées sur la valeur la plus large, en-tête souligné.</summary>
    private static void Write(IReadOnlyList<string[]> rows)
    {
        var columns = rows[0].Length;
        var widths = new int[columns];

        foreach (var row in rows)
        {
            for (var i = 0; i < columns; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var line = new StringBuilder();
            for (var i = 0; i < columns; i++)
            {
                line.Append(rows[r][i].PadRight(i == columns - 1 ? 0 : widths[i] + 2));
            }

            Console.WriteLine(line.ToString().TrimEnd());

            if (r == 0)
                Console.WriteLine(new string('-', widths.Sum() + ((columns - 1) * 2)));
        }
    }

    private static string Join(IReadOnlyList<ChannelView> channels, Func<ChannelView, string> select) =>
        channels.Count == 0 ? "-" : string.Join("/", channels.Select(select));
}
