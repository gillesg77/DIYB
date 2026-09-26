using DIYB.Localization;
using DIYB.Core.Diagnostics;

namespace DIYB.App.ViewModels;

/// <summary>Ligne du journal. Les libellés connus sont traduits, les autres (points
/// d'entrée REST) affichés tels quels.</summary>
public sealed class LogRow
{
    private static readonly Dictionary<string, string> KnownMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discovery.found"] = "log.discoveryFound",
        ["discovery.lost"] = "log.discoveryLost",
        ["ota.started"] = "log.otaStarted",
        ["ota.completed"] = "log.otaCompleted",
    };

    public LogRow(LogEntry entry) => Entry = entry;

    public LogEntry Entry { get; }

    public string Time => Entry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

    public string Level => Entry.Level.ToString().ToUpperInvariant();

    public bool IsError => Entry.Level == LogLevel.Error;

    public string DeviceId => Entry.DeviceId ?? string.Empty;

    public string Message => KnownMessages.TryGetValue(Entry.Message, out var key)
        ? Localizer.Current[key]
        : Entry.Message;

    public string Detail => Entry.Duration is { } duration
        ? Localizer.Current.Format("log.duration", (int)duration.TotalMilliseconds)
        : string.Empty;

    public string Request => Entry.Request ?? string.Empty;

    public string Response => Entry.Response ?? string.Empty;

    public bool HasPayload => !string.IsNullOrEmpty(Entry.Request) || !string.IsNullOrEmpty(Entry.Response);
}
