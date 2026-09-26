using System.Collections.Concurrent;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;
using DIYB.Core.Mdns;

namespace DIYB.Core.Discovery;

/// <summary>Inventaire courant des appareils. Alimenté par les annonces mDNS et par
/// les réponses <c>/zeroconf/info</c>, les deux sources étant fusionnées par
/// identifiant.</summary>
public sealed class DeviceRegistry : IDisposable
{
    private readonly MdnsClient _mdns;
    private readonly EwelinkRecordAssembler _assembler = new();
    private readonly ConcurrentDictionary<string, DiyDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ApiLog _log;
    private readonly object _gate = new();

    private readonly object _queryGate = new();
    private readonly Dictionary<(string Name, ushort Type), DateTimeOffset> _lastQuery = new();

    private Timer? _queryTimer;
    private Timer? _evictionTimer;
    private bool _running;

    public DeviceRegistry(MdnsClient mdns, ApiLog log)
    {
        _mdns = mdns;
        _log = log;
        _mdns.MessageReceived += OnMessageReceived;
        _mdns.InterfacesChanged += OnInterfacesChanged;
    }

    public event EventHandler<DiyDevice>? DeviceAdded;

    public event EventHandler<DiyDevice>? DeviceUpdated;

    public event EventHandler<string>? DeviceRemoved;

    private static readonly TimeSpan ResolveThrottle = TimeSpan.FromSeconds(3);

    public TimeSpan QueryInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Un appareil silencieux au-delà de ce délai est retiré. Les Sonoff
    /// réannoncent spontanément bien plus souvent.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(3);

    public IReadOnlyCollection<DiyDevice> Devices => _devices.Values.ToArray();

    public DiyDevice? Find(string deviceId) => _devices.GetValueOrDefault(deviceId);

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
                return;

            _running = true;
            _mdns.Start();
            _queryTimer = new Timer(_ => Refresh(), null, TimeSpan.Zero, QueryInterval);
            _evictionTimer = new Timer(_ => EvictStale(), null, StaleAfter, TimeSpan.FromSeconds(30));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running)
                return;

            _running = false;
            _queryTimer?.Dispose();
            _queryTimer = null;
            _evictionTimer?.Dispose();
            _evictionTimer = null;
            _mdns.Stop();
        }
    }

    public void Refresh() => _mdns.Query(EwelinkTxt.ServiceType);

    /// <summary>Insère ou met à jour un appareil, en fusionnant l'état partiel avec
    /// celui déjà connu.</summary>
    public DiyDevice Upsert(DiyDevice device) => Accept(device, logDiscovery: false);

    /// <summary>Applique un état fraîchement lu à un appareil déjà présent.</summary>
    public DiyDevice? ApplyState(string deviceId, DeviceState state)
    {
        if (!_devices.TryGetValue(deviceId, out var existing))
            return null;

        var updated = existing with { State = existing.State.MergeWith(state), LastSeen = DateTimeOffset.Now };
        _devices[deviceId] = updated;
        DeviceUpdated?.Invoke(this, updated);
        return updated;
    }

    public void Remove(string deviceId)
    {
        if (_devices.TryRemove(deviceId, out _))
            DeviceRemoved?.Invoke(this, deviceId);
    }

    public void Clear(bool keepSimulated = true)
    {
        foreach (var device in _devices.Values)
        {
            if (keepSimulated && device.IsSimulated)
                continue;

            Remove(device.DeviceId);
        }
    }

    private void OnMessageReceived(object? sender, MdnsMessageEventArgs e)
    {
        IReadOnlyList<DiscoveryEvent> events;
        try
        {
            events = _assembler.Consume(e.Message, DateTimeOffset.Now);
        }
        catch (FormatException)
        {
            return;
        }

        foreach (var discovery in events)
        {
            switch (discovery)
            {
                case DeviceSeen seen:
                    Accept(seen.Device, logDiscovery: true);
                    break;

                case DeviceGone gone:
                    Remove(gone.DeviceId);
                    break;

                case ResolveNeeded resolve:
                    QueryOnce(resolve.Name, resolve.Type);
                    break;
            }
        }
    }

    /// <summary>Requête de suivi, espacée par nom et par type : chaque annonce reçue
    /// en redemanderait une, ce qui inonderait le réseau.</summary>
    private void QueryOnce(string name, ushort type)
    {
        lock (_queryGate)
        {
            var key = (name.ToLowerInvariant(), type);
            if (_lastQuery.TryGetValue(key, out var last) && DateTimeOffset.Now - last < ResolveThrottle)
                return;

            _lastQuery[key] = DateTimeOffset.Now;
        }

        _mdns.Query(name, type);
    }

    private DiyDevice Accept(DiyDevice device, bool logDiscovery)
    {
        var isNew = !_devices.ContainsKey(device.DeviceId);
        var merged = _devices.AddOrUpdate(
            device.DeviceId,
            device,
            (_, existing) => device with { State = existing.State.MergeWith(device.State) });

        if (isNew)
        {
            if (logDiscovery)
                _log.Info("discovery.found", device.DeviceId);

            DeviceAdded?.Invoke(this, merged);
        }
        else
        {
            DeviceUpdated?.Invoke(this, merged);
        }

        return merged;
    }

    private void EvictStale()
    {
        foreach (var device in _devices.Values)
        {
            if (device.IsSimulated || !device.IsStale(StaleAfter))
                continue;

            _log.Warn("discovery.lost", device.DeviceId);
            Remove(device.DeviceId);
        }
    }

    private void OnInterfacesChanged(object? sender, EventArgs e) => Refresh();

    public void Dispose()
    {
        _mdns.MessageReceived -= OnMessageReceived;
        _mdns.InterfacesChanged -= OnInterfacesChanged;
        Stop();
    }
}
