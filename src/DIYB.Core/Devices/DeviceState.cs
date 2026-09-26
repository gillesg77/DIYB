using System.Collections.Immutable;

namespace DIYB.Core.Devices;

/// <summary>Instantané d'un appareil, reconstruit depuis les TXT mDNS ou depuis
/// <c>/zeroconf/info</c>.</summary>
public sealed record DeviceState
{
    public required string DeviceId { get; init; }

    public ImmutableArray<ChannelState> Channels { get; init; } = ImmutableArray<ChannelState>.Empty;

    public string? FirmwareVersion { get; init; }

    public string? Ssid { get; init; }

    public int? Rssi { get; init; }

    /// <summary>Vrai lorsque <c>/zeroconf/ota_unlock</c> a déjà été accepté.</summary>
    public bool OtaUnlocked { get; init; }

    public string? MacAddress { get; init; }

    /// <summary>Type annoncé dans le TXT mDNS (<c>diy_plug</c>, <c>strip</c>…).</summary>
    public string? DeviceType { get; init; }

    /// <summary>Le module annonce un TXT chiffré : il est resté en mode cloud et son
    /// API locale réclame la clé d'appairage du compte eWeLink. Rien n'est pilotable
    /// tant qu'il n'est pas repassé en mode DIY.</summary>
    public bool RequiresKey { get; init; }

    public bool IsControllable => !RequiresKey;

    public bool IsMultiChannel => Channels.Length > 1;

    public ChannelState? Channel(int outlet) => Channels.FirstOrDefault(c => c.Outlet == outlet);

    public static DeviceState Unknown(string deviceId) => new() { DeviceId = deviceId };

    // ImmutableArray compare ses instances par référence : sans cette redéfinition,
    // deux états de contenu identique seraient tenus pour différents.
    public bool Equals(DeviceState? other) =>
        other is not null
        && string.Equals(DeviceId, other.DeviceId, StringComparison.OrdinalIgnoreCase)
        && Channels.SequenceEqual(other.Channels)
        && FirmwareVersion == other.FirmwareVersion
        && Ssid == other.Ssid
        && Rssi == other.Rssi
        && OtaUnlocked == other.OtaUnlocked
        && MacAddress == other.MacAddress
        && DeviceType == other.DeviceType
        && RequiresKey == other.RequiresKey;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeviceId, StringComparer.OrdinalIgnoreCase);
        foreach (var channel in Channels)
            hash.Add(channel);

        hash.Add(FirmwareVersion);
        hash.Add(Ssid);
        hash.Add(Rssi);
        hash.Add(OtaUnlocked);
        hash.Add(MacAddress);
        hash.Add(DeviceType);
        hash.Add(RequiresKey);
        return hash.ToHashCode();
    }

    /// <summary>Fusionne un état partiel dans l'état connu. Les annonces mDNS sont
    /// bornées à quatre segments TXT de 255 octets : un champ absent ne vaut pas
    /// effacement.</summary>
    public DeviceState MergeWith(DeviceState fresher)
    {
        if (!string.Equals(DeviceId, fresher.DeviceId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Fusion entre deux appareils distincts.", nameof(fresher));

        return this with
        {
            Channels = fresher.Channels.IsDefaultOrEmpty ? Channels : fresher.Channels,
            FirmwareVersion = fresher.FirmwareVersion ?? FirmwareVersion,
            Ssid = fresher.Ssid ?? Ssid,
            Rssi = fresher.Rssi ?? Rssi,
            OtaUnlocked = fresher.OtaUnlocked || OtaUnlocked,
            MacAddress = fresher.MacAddress ?? MacAddress,
            DeviceType = fresher.DeviceType ?? DeviceType,
            RequiresKey = fresher.RequiresKey,
        };
    }
}
