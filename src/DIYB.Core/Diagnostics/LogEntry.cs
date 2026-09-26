namespace DIYB.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Une ligne du journal. Requête et réponse sont conservées brutes pour
/// l'inspecteur.</summary>
public sealed record LogEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    public required LogLevel Level { get; init; }

    /// <summary>Clé de traduction, ou libellé littéral pour les échanges protocolaires.</summary>
    public required string Message { get; init; }

    public string? DeviceId { get; init; }

    public string? Endpoint { get; init; }

    public string? Request { get; init; }

    public string? Response { get; init; }

    public TimeSpan? Duration { get; init; }

    public ErrorCode? ErrorCode { get; init; }
}
