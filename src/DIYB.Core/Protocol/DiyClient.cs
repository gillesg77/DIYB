using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;

namespace DIYB.Core.Protocol;

/// <summary>Client du mode DIY eWeLink. Une méthode par opération de l'IHM, la forme
/// mono-canal ou multi-canaux de la charge utile étant choisie d'après l'état connu
/// de l'appareil.</summary>
public sealed class DiyClient
{
    /// <summary>Le firmware exige une clé d'API même en mode DIY, où elle n'est pas
    /// vérifiée. La valeur conventionnelle est « 123 ».</summary>
    private const string SelfApiKey = "123";

    public const int MinPulseWidthMs = 500;
    public const int MaxPulseWidthMs = 36_000_000;
    public const int PulseWidthStepMs = 500;

    private readonly IDiyTransport _transport;
    private readonly ApiLog _log;

    public DiyClient(IDiyTransport transport, ApiLog log)
    {
        _transport = transport;
        _log = log;
    }

    public async Task<DeviceState> GetInfoAsync(DiyDevice device, CancellationToken ct = default)
    {
        var data = await CallAsync(device, DiyEndpoints.Info, new { }, ct).ConfigureAwait(false);
        return DeviceStateParser.Parse(device.DeviceId, data, device.State?.DeviceType);
    }

    /// <summary>Commande le relais. <paramref name="outlet"/> à <c>null</c> vise tous
    /// les canaux.</summary>
    public Task SetSwitchAsync(DiyDevice device, int? outlet, bool on, CancellationToken ct = default)
    {
        var wire = (on ? SwitchState.On : SwitchState.Off).ToWire();

        if (!IsMultiChannel(device))
            return CallAsync(device, DiyEndpoints.Switch, new { @switch = wire }, ct);

        var targets = Outlets(device, outlet);
        var payload = new { switches = targets.Select(o => new { @switch = wire, outlet = o }).ToArray() };
        return CallAsync(device, DiyEndpoints.Switches, payload, ct);
    }

    public Task SetStartupAsync(DiyDevice device, int? outlet, StartupMode mode, CancellationToken ct = default)
    {
        var wire = mode.ToWire();

        if (!IsMultiChannel(device))
            return CallAsync(device, DiyEndpoints.Startup, new { startup = wire }, ct);

        // En multi-canaux le champ s'appelle « configure », sur le même point d'entrée.
        var targets = Outlets(device, outlet);
        var payload = new { configure = targets.Select(o => new { startup = wire, outlet = o }).ToArray() };
        return CallAsync(device, DiyEndpoints.Startup, payload, ct);
    }

    public Task SetPulseAsync(DiyDevice device, int? outlet, bool enabled, int widthMs, CancellationToken ct = default)
    {
        ValidatePulseWidth(widthMs, device.DeviceId);
        var wire = enabled ? "on" : "off";

        if (!IsMultiChannel(device))
            return CallAsync(device, DiyEndpoints.Pulse, new { pulse = wire, pulseWidth = widthMs }, ct);

        var targets = Outlets(device, outlet);
        var payload = new { pulses = targets.Select(o => new { pulse = wire, width = widthMs, outlet = o }).ToArray() };
        return CallAsync(device, DiyEndpoints.Pulses, payload, ct);
    }

    public async Task<int> GetSignalStrengthAsync(DiyDevice device, CancellationToken ct = default)
    {
        var data = await CallAsync(device, DiyEndpoints.SignalStrength, new { }, ct).ConfigureAwait(false);
        if (data.TryGetProperty("signalStrength", out var v) && v.TryGetInt32(out var rssi))
            return rssi;

        throw new DiyException(ErrorCode.InvalidResponse, $"No signalStrength in response from {device.DeviceId}.");
    }

    /// <summary>Change le réseau Wi-Fi cible. L'appareil redémarre et quitte le mode
    /// DIY, donc la liste.</summary>
    public Task SetWifiAsync(DiyDevice device, string ssid, string password, CancellationToken ct = default)
    {
        // Le firmware accepte un SSID vide ou une clé WPA trop courte, puis laisse
        // l'appareil injoignable sans message.
        if (string.IsNullOrWhiteSpace(ssid) || password.Length is > 0 and < 8)
        {
            throw new DiyException(ErrorCode.WifiCredentialsInvalid,
                $"Invalid Wi-Fi credentials for {device.DeviceId}.",
                new Dictionary<string, object?> { ["deviceId"] = device.DeviceId });
        }

        return CallAsync(device, DiyEndpoints.Wifi, new { ssid, password }, ct, redactedFields: new[] { "password" });
    }

    public Task UnlockOtaAsync(DiyDevice device, CancellationToken ct = default) =>
        CallAsync(device, DiyEndpoints.OtaUnlock, new { }, ct);

    public Task FlashAsync(DiyDevice device, Uri downloadUrl, string sha256, CancellationToken ct = default) =>
        CallAsync(device, DiyEndpoints.OtaFlash, new { downloadUrl = downloadUrl.ToString(), sha256sum = sha256 }, ct);

    internal static void ValidatePulseWidth(int widthMs, string deviceId)
    {
        if (widthMs < MinPulseWidthMs || widthMs > MaxPulseWidthMs || widthMs % PulseWidthStepMs != 0)
        {
            throw new DiyException(ErrorCode.PulseWidthOutOfRange,
                $"Pulse width {widthMs} ms is out of range or not a multiple of {PulseWidthStepMs}.",
                new Dictionary<string, object?>
                {
                    ["deviceId"] = deviceId,
                    ["value"] = widthMs,
                    ["min"] = MinPulseWidthMs,
                    ["max"] = MaxPulseWidthMs,
                    ["step"] = PulseWidthStepMs,
                });
        }
    }

    private static bool IsMultiChannel(DiyDevice device) => device.State?.IsMultiChannel ?? false;

    private static int[] Outlets(DiyDevice device, int? outlet)
    {
        var channels = device.State?.Channels ?? default;

        if (outlet is null)
            return channels.IsDefaultOrEmpty ? new[] { 0 } : channels.Select(c => c.Outlet).ToArray();

        if (!channels.IsDefaultOrEmpty && channels.All(c => c.Outlet != outlet.Value))
        {
            throw new DiyException(ErrorCode.UnknownOutlet,
                $"Device {device.DeviceId} has no outlet {outlet.Value}.",
                new Dictionary<string, object?> { ["deviceId"] = device.DeviceId, ["outlet"] = outlet.Value });
        }

        return new[] { outlet.Value };
    }

    /// <summary>Émet la requête, journalise l'aller-retour et renvoie le champ
    /// « data » de la réponse.</summary>
    private async Task<JsonElement> CallAsync(
        DiyDevice device,
        string endpoint,
        object data,
        CancellationToken ct,
        string[]? redactedFields = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            deviceid = device.DeviceId,
            selfApikey = SelfApiKey,
            data,
        });

        var stopwatch = Stopwatch.StartNew();
        string? raw = null;
        try
        {
            raw = await _transport.PostAsync(device, endpoint, body, ct).ConfigureAwait(false);
            var result = ReadEnvelope(raw, device.DeviceId);
            Log(device, endpoint, body, raw, stopwatch.Elapsed, null, redactedFields);
            return result;
        }
        catch (DiyException e)
        {
            Log(device, endpoint, body, raw, stopwatch.Elapsed, e, redactedFields);
            throw;
        }
        catch (JsonException e)
        {
            var wrapped = new DiyException(ErrorCode.InvalidResponse, $"Malformed response from {device.DeviceId}.",
                new Dictionary<string, object?> { ["deviceId"] = device.DeviceId }, e);
            Log(device, endpoint, body, raw, stopwatch.Elapsed, wrapped, redactedFields);
            throw wrapped;
        }
    }

    private static JsonElement ReadEnvelope(string raw, string deviceId)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var error = root.TryGetProperty("error", out var e) && e.TryGetInt32(out var code) ? code : 0;
        if (error != 0)
            throw DiyException.FromFirmware(error, deviceId);

        // Clone obligatoire : le JsonDocument est libéré à la sortie de la méthode.
        return root.TryGetProperty("data", out var data) ? data.Clone() : EmptyObject;
    }

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    private void Log(DiyDevice device, string endpoint, string request, string? response, TimeSpan elapsed, DiyException? error, string[]? redactedFields)
    {
        _log.Add(new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = error is null ? LogLevel.Debug : LogLevel.Error,
            Message = endpoint,
            DeviceId = device.DeviceId,
            Endpoint = endpoint,
            Request = Redact(request, redactedFields),
            Response = response,
            Duration = elapsed,
            ErrorCode = error?.Code,
        });
    }

    /// <summary>Masque les champs sensibles avant journalisation.</summary>
    internal static string Redact(string json, string[]? fields)
    {
        if (fields is null || fields.Length == 0)
            return json;

        using var doc = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteRedacted(doc.RootElement, writer, fields, null);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteRedacted(JsonElement element, Utf8JsonWriter writer, string[] fields, string? propertyName)
    {
        if (propertyName is not null && fields.Contains(propertyName))
        {
            writer.WriteString(propertyName, "***");
            return;
        }

        if (propertyName is not null)
            writer.WritePropertyName(propertyName);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                    WriteRedacted(property.Value, writer, fields, property.Name);
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteRedacted(item, writer, fields, null);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
