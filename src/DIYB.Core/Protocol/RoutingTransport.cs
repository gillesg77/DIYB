using DIYB.Core.Devices;

namespace DIYB.Core.Protocol;

/// <summary>Aiguille vers le réseau ou vers le simulateur selon l'appareil.</summary>
public sealed class RoutingTransport : IDiyTransport
{
    private readonly IDiyTransport _network;
    private readonly IDiyTransport _simulator;

    public RoutingTransport(IDiyTransport network, IDiyTransport simulator)
    {
        _network = network;
        _simulator = simulator;
    }

    public Task<string> PostAsync(DiyDevice device, string endpoint, string body, CancellationToken ct) =>
        (device.IsSimulated ? _simulator : _network).PostAsync(device, endpoint, body, ct);
}
