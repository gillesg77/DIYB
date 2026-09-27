namespace DIYB.Core.Storage;

/// <summary>Géométrie de la fenêtre, en pixels physiques.</summary>
public sealed record WindowPlacement
{
    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool Maximized { get; init; }
}

public sealed record AppSettings
{
    public string? Language { get; init; }

    public WindowPlacement? Window { get; init; }

    public string? ActiveProfile { get; init; }

    public bool ShowCloudDevices { get; init; }

    /// <summary>Catalogues de firmware interrogés au démarrage : chemins locaux ou URL.</summary>
    public IReadOnlyList<string> FirmwareCatalogs { get; init; } = Array.Empty<string>();
}
