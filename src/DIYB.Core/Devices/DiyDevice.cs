using System.Net;

namespace DIYB.Core.Devices;

/// <summary>Appareil joignable : adresse réseau et dernier état connu.</summary>
public sealed record DiyDevice
{
    public required string DeviceId { get; init; }

    public required IPAddress Address { get; init; }

    /// <summary>Port annoncé par le SRV mDNS, 8081 en mode DIY.</summary>
    public int Port { get; init; } = 8081;

    public string? HostName { get; init; }

    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.Now;

    public DeviceState State { get; init; } = null!;

    /// <summary>Appareil du simulateur.</summary>
    public bool IsSimulated { get; init; }

    public Uri BaseUri => new($"http://{Address}:{Port}");

    public bool IsStale(TimeSpan ttl) => DateTimeOffset.Now - LastSeen > ttl;
}
