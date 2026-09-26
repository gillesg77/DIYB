using System.Net;
using DIYB.Core.Devices;
using DIYB.Core.Mdns;

namespace DIYB.Core.Discovery;

public abstract record DiscoveryEvent;

public sealed record DeviceSeen(DiyDevice Device) : DiscoveryEvent;

public sealed record DeviceGone(string DeviceId) : DiscoveryEvent;

/// <summary>Enregistrement manquant pour compléter un appareil. Un module répond à la
/// requête PTR par le seul PTR : l'adresse et l'état réclament une requête de suivi.</summary>
public sealed record ResolveNeeded(string Name, ushort Type) : DiscoveryEvent;

/// <summary>Assemble les enregistrements PTR, SRV, TXT et A d'un ou plusieurs
/// messages en appareils complets. Les sections arrivent parfois éclatées sur
/// plusieurs paquets, l'état partiel est donc conservé entre les appels.</summary>
public sealed class EwelinkRecordAssembler
{
    private readonly Dictionary<string, Instance> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IPAddress> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DiscoveryEvent> Consume(DnsMessage message, DateTimeOffset now)
    {
        var events = new List<DiscoveryEvent>();
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in message.AllRecords)
        {
            switch (record)
            {
                case AddressRecord a when a.Type == DnsType.A:
                    _hosts[Normalize(a.Name)] = a.Address;
                    foreach (var name in _instances.Where(i => Matches(i.Value.Host, a.Name)).Select(i => i.Key))
                        touched.Add(name);
                    break;

                case PtrRecord ptr when Matches(ptr.Name, EwelinkTxt.ServiceType):
                    if (ptr.IsGoodbye)
                    {
                        var removed = Forget(ptr.Target);
                        if (removed is not null)
                            events.Add(new DeviceGone(removed));
                    }
                    else
                    {
                        Track(ptr.Target);
                        touched.Add(Normalize(ptr.Target));
                    }

                    break;

                case SrvRecord srv when IsEwelinkInstance(srv.Name):
                    var forSrv = Track(srv.Name);
                    forSrv.Host = srv.Target;
                    forSrv.Port = srv.Port;
                    touched.Add(Normalize(srv.Name));
                    break;

                case TxtRecord txt when IsEwelinkInstance(txt.Name):
                    var forTxt = Track(txt.Name);
                    forTxt.Txt = txt.ToDictionary();
                    touched.Add(Normalize(txt.Name));
                    break;
            }
        }

        foreach (var name in touched)
        {
            if (!_instances.TryGetValue(name, out var instance))
                continue;

            if (Build(instance, now) is { } device)
                events.Add(new DeviceSeen(device));
            else
                events.AddRange(MissingRecords(instance));
        }

        return events;
    }

    /// <summary>Requêtes à émettre pour achever un appareil incomplet.</summary>
    private IEnumerable<ResolveNeeded> MissingRecords(Instance instance)
    {
        if (instance.Host is null)
            yield return new ResolveNeeded(instance.Name, DnsType.Srv);

        if (instance.Txt is null)
            yield return new ResolveNeeded(instance.Name, DnsType.Txt);

        if (instance.Host is not null && ResolveAddress(instance) is null)
            yield return new ResolveNeeded(instance.Host, DnsType.A);
    }

    private DiyDevice? Build(Instance instance, DateTimeOffset now)
    {
        if (instance.Txt is null)
            return null;

        var deviceId = EwelinkTxt.DeviceIdFrom(instance.Txt, instance.Name);
        if (deviceId is null)
            return null;

        var address = ResolveAddress(instance);
        if (address is null)
            return null;

        var state = EwelinkTxt.ParseState(instance.Txt, deviceId);
        if (state is null)
            return null;

        return new DiyDevice
        {
            DeviceId = deviceId,
            Address = address,
            Port = instance.Port ?? 8081,
            HostName = instance.Host,
            LastSeen = now,
            State = state,
        };
    }

    private IPAddress? ResolveAddress(Instance instance) =>
        instance.Host is not null && _hosts.TryGetValue(Normalize(instance.Host), out var address) ? address : null;

    private Instance Track(string instanceName)
    {
        var key = Normalize(instanceName);
        if (!_instances.TryGetValue(key, out var instance))
            _instances[key] = instance = new Instance(instanceName);

        return instance;
    }

    private string? Forget(string instanceName)
    {
        var key = Normalize(instanceName);
        if (!_instances.Remove(key, out var instance) || instance.Txt is null)
            return null;

        return EwelinkTxt.DeviceIdFrom(instance.Txt, instance.Name);
    }

    private static bool IsEwelinkInstance(string name) =>
        Normalize(name).EndsWith('.' + EwelinkTxt.ServiceType, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string? left, string? right) =>
        left is not null && right is not null && Normalize(left).Equals(Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string name) => name.TrimEnd('.');

    private sealed class Instance
    {
        public Instance(string name) => Name = name;

        public string Name { get; }

        public string? Host { get; set; }

        public int? Port { get; set; }

        public IReadOnlyDictionary<string, string>? Txt { get; set; }
    }
}
