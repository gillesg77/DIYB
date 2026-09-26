using System.Text.Json;
using DIYB.Core.Devices;
using DIYB.Core.Protocol;

namespace DIYB.Core.Discovery;

/// <summary>Décodage des enregistrements TXT du service <c>_ewelink._tcp</c>.</summary>
public static class EwelinkTxt
{
    public const string ServiceType = "_ewelink._tcp.local";

    private const string InstancePrefix = "eWeLink_";

    /// <summary>Identifiant tiré du TXT, à défaut du nom d'instance
    /// <c>eWeLink_&lt;id&gt;._ewelink._tcp.local</c>.</summary>
    public static string? DeviceIdFrom(IReadOnlyDictionary<string, string> txt, string instanceName)
    {
        if (txt.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id))
            return id;

        var label = instanceName.Split('.')[0];
        return label.StartsWith(InstancePrefix, StringComparison.OrdinalIgnoreCase)
            ? label[InstancePrefix.Length..]
            : null;
    }

    /// <summary>L'état est réparti sur <c>data1</c> à <c>data4</c>, chaque segment TXT
    /// étant borné à 255 octets. Leur concaténation forme le JSON complet.</summary>
    public static string? JoinStateChunks(IReadOnlyDictionary<string, string> txt)
    {
        var chunks = new List<string>(4);
        for (var i = 1; i <= 4; i++)
        {
            if (txt.TryGetValue($"data{i}", out var chunk) && !string.IsNullOrEmpty(chunk))
                chunks.Add(chunk);
        }

        return chunks.Count == 0 ? null : string.Concat(chunks);
    }

    /// <summary>Un TXT chiffré n'est exploitable qu'avec la clé d'appairage, absente
    /// en mode DIY.</summary>
    public static bool IsEncrypted(IReadOnlyDictionary<string, string> txt) =>
        txt.TryGetValue("encrypt", out var value) && value.Equals("true", StringComparison.OrdinalIgnoreCase);

    public static DeviceState? ParseState(IReadOnlyDictionary<string, string> txt, string deviceId)
    {
        txt.TryGetValue("type", out var deviceType);

        if (IsEncrypted(txt))
            return new DeviceState { DeviceId = deviceId, DeviceType = deviceType, RequiresKey = true };

        var json = JoinStateChunks(txt);
        if (json is null)
            return new DeviceState { DeviceId = deviceId, DeviceType = deviceType };

        try
        {
            return DeviceStateParser.Parse(deviceId, json, deviceType);
        }
        catch (JsonException)
        {
            // Annonce reçue en cours de mise à jour, segments incohérents entre eux.
            return null;
        }
    }
}
