namespace DIYB.Core.Firmware;

public sealed record FirmwareRelease
{
    /// <summary>Modèle visé, comparé au champ <c>type</c> du TXT mDNS. La valeur
    /// <c>*</c> s'applique à tout appareil.</summary>
    public required string Model { get; init; }

    public required string Version { get; init; }

    public Uri? Url { get; init; }

    /// <summary>Empreinte attendue, en hexadécimal minuscule. Absente, le flash reste
    /// possible mais l'empreinte est recalculée depuis le fichier local.</summary>
    public string? Sha256 { get; init; }

    public string Channel { get; init; } = "release";

    public DateTimeOffset? PublishedAt { get; init; }

    public string? Notes { get; init; }

    public string Source { get; init; } = string.Empty;
}

/// <summary>Source de firmwares. Plusieurs sources coexistent : catalogue maison,
/// dépôt public, et à terme un fournisseur constructeur.</summary>
public interface IFirmwareCatalog
{
    string Name { get; }

    Task<IReadOnlyList<FirmwareRelease>> GetReleasesAsync(CancellationToken ct = default);
}
