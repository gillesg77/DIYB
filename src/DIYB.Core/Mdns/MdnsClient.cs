using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DIYB.Core.Mdns;

public sealed record MdnsMessageEventArgs(DnsMessage Message, IPEndPoint Remote, IPAddress LocalInterface);

/// <summary>Client mDNS minimal : émission de requêtes sur chaque interface et
/// écoute permanente du groupe multicast.</summary>
public sealed class MdnsClient : IDisposable
{
    private static readonly IPAddress MulticastGroup = IPAddress.Parse("224.0.0.251");
    private const int MulticastPort = 5353;

    private readonly object _gate = new();
    private readonly List<Socket> _senders = new();
    private CancellationTokenSource? _cts;
    private Socket? _receiver;
    private bool _disposed;
    private long _packetsReceived;

    /// <summary>Datagrammes reçus depuis le démarrage, tous émetteurs confondus.
    /// Un réseau actif en produit toujours — imprimantes, téléviseurs, partages —
    /// donc un compteur resté à zéro trahit un blocage en entrée plutôt qu'une
    /// absence d'appareils.</summary>
    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

    public event EventHandler<MdnsMessageEventArgs>? MessageReceived;

    /// <summary>Levé quand la pile réseau change, après reconstruction des sockets.</summary>
    public event EventHandler? InterfacesChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            OpenSockets();
        }

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    public void Stop()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            CloseSockets();
        }
    }

    /// <summary>Émet une requête sur toutes les interfaces actives. Les échecs par
    /// interface sont ignorés : une carte peut disparaître entre deux envois.</summary>
    public void Query(string serviceName, ushort type = DnsType.Ptr)
    {
        var payload = DnsMessageCodec.BuildQuery(serviceName, type);
        var endpoint = new IPEndPoint(MulticastGroup, MulticastPort);

        Socket[] senders;
        lock (_gate)
            senders = _senders.ToArray();

        foreach (var sender in senders)
        {
            try
            {
                sender.SendTo(payload, endpoint);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void OpenSockets()
    {
        var addresses = LocalMulticastAddresses().ToArray();

        _receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _receiver.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _receiver.ExclusiveAddressUse = false;
        _receiver.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));

        foreach (var address in addresses)
        {
            try
            {
                _receiver.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MulticastGroup, address));
            }
            catch (SocketException)
            {
                // Interface sans support multicast effectif.
                continue;
            }

            var sender = CreateSender(address);
            if (sender is not null)
                _senders.Add(sender);
        }

        var token = _cts!.Token;
        _ = Task.Run(() => ReceiveLoopAsync(_receiver, token), token);

        // Les sockets d'émission sont écoutées elles aussi : un répondeur qui juge la
        // requête « legacy » répond en unicast au port source au lieu du groupe.
        foreach (var sender in _senders)
            _ = Task.Run(() => ReceiveLoopAsync(sender, token), token);
    }

    /// <summary>Socket d'émission liée au port 5353 quand c'est possible : une requête
    /// émise depuis un autre port est traitée comme une requête unicast héritée, ce qui
    /// prive l'application des annonces spontanées adressées au groupe.</summary>
    private static Socket? CreateSender(IPAddress address)
    {
        foreach (var port in new[] { MulticastPort, 0 })
        {
            var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                sender.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                sender.ExclusiveAddressUse = false;
                sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                sender.Bind(new IPEndPoint(address, port));
                return sender;
            }
            catch (SocketException)
            {
                sender.Dispose();
            }
        }

        return null;
    }

    private void CloseSockets()
    {
        foreach (var sender in _senders)
            sender.Dispose();

        _senders.Clear();
        _receiver?.Dispose();
        _receiver = null;
    }

    private async Task ReceiveLoopAsync(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[9000];
        var any = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            // Compté avant l'analyse : un datagramme même illisible prouve que la
            // réception fonctionne.
            Interlocked.Increment(ref _packetsReceived);

            DnsMessage message;
            try
            {
                message = DnsMessageCodec.Read(buffer, result.ReceivedBytes);
            }
            catch (FormatException)
            {
                continue;
            }

            if (result.RemoteEndPoint is not IPEndPoint remote)
                continue;

            MessageReceived?.Invoke(this, new MdnsMessageEventArgs(message, remote, LocalAddressFor(remote.Address)));
        }
    }

    /// <summary>Interface locale la plus probable pour une adresse distante, utilisée
    /// seulement pour l'affichage.</summary>
    private static IPAddress LocalAddressFor(IPAddress remote)
    {
        foreach (var candidate in LocalMulticastAddresses())
        {
            if (candidate.AddressFamily == remote.AddressFamily)
                return candidate;
        }

        return IPAddress.Any;
    }

    private static IEnumerable<IPAddress> LocalMulticastAddresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || !nic.SupportsMulticast)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return unicast.Address;
            }
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_cts is null || _disposed)
                return;

            _cts.Cancel();
            _cts.Dispose();
            CloseSockets();

            _cts = new CancellationTokenSource();
            try
            {
                OpenSockets();
            }
            catch (SocketException)
            {
                return;
            }
        }

        InterfacesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }
}
