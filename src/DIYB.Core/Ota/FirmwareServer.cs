using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DIYB.Core.Ota;

/// <summary>Serveur HTTP à usage unique qui publie un fichier de firmware le temps
/// du flash. Construit sur <see cref="TcpListener"/> plutôt que sur
/// <see cref="HttpListener"/>, qui réclamerait une réservation d'URL administrateur
/// pour écouter sur autre chose que localhost.</summary>
public sealed class FirmwareServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _filePath;
    private readonly string _route;
    private readonly CancellationTokenSource _cts = new();

    private FirmwareServer(TcpListener listener, string filePath, string route, IPAddress address, int port)
    {
        _listener = listener;
        _filePath = filePath;
        _route = route;
        Address = address;
        Port = port;
    }

    public IPAddress Address { get; }

    public int Port { get; }

    public Uri Url => new($"http://{Address}:{Port}{_route}");

    /// <summary>Octets transmis pour la requête en cours.</summary>
    public event EventHandler<long>? BytesServed;

    /// <summary>Levé lorsqu'un client a reçu le fichier entier.</summary>
    public event EventHandler? TransferCompleted;

    public static FirmwareServer Start(IPAddress bindAddress, string filePath, int port = 0)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);

        var listener = new TcpListener(bindAddress, port);
        listener.Start();

        var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        var route = "/" + Guid.NewGuid().ToString("N")[..8] + ".bin";

        var server = new FirmwareServer(listener, filePath, route, bindAddress, actualPort);
        _ = Task.Run(() => server.AcceptLoopAsync(server._cts.Token));
        return server;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client, ct), ct);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();

                var request = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
                if (request is null)
                    return;

                var (method, target) = request.Value;
                if (!string.Equals(target, _route, StringComparison.Ordinal))
                {
                    await WriteStatusAsync(stream, "404 Not Found", ct).ConfigureAwait(false);
                    return;
                }

                var info = new FileInfo(_filePath);
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/octet-stream\r\n" +
                    $"Content-Length: {info.Length}\r\n" +
                    "Connection: close\r\n\r\n");

                await stream.WriteAsync(header, ct).ConfigureAwait(false);

                if (string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
                    return;

                await SendFileAsync(stream, info.Length, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Le module coupe la connexion dès la dernière trame reçue.
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task SendFileAsync(Stream stream, long total, CancellationToken ct)
    {
        await using var file = File.OpenRead(_filePath);
        var buffer = new byte[16 * 1024];
        long sent = 0;

        while (true)
        {
            var read = await file.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;

            await stream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            sent += read;
            BytesServed?.Invoke(this, sent);
        }

        await stream.FlushAsync(ct).ConfigureAwait(false);

        if (sent >= total)
            TransferCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lit la ligne de requête puis consomme les en-têtes jusqu'à la ligne
    /// vide. La taille est bornée pour ne pas dépendre du bon vouloir du client.</summary>
    private static async Task<(string Method, string Target)?> ReadRequestLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[2048];
        var count = 0;

        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count, buffer.Length - count), ct).ConfigureAwait(false);
            if (read == 0)
                break;

            count += read;
            var text = Encoding.ASCII.GetString(buffer, 0, count);
            if (!text.Contains("\r\n\r\n", StringComparison.Ordinal))
                continue;

            var firstLine = text.Split("\r\n", StringSplitOptions.None)[0];
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? (parts[0], parts[1]) : null;
        }

        return null;
    }

    private static Task WriteStatusAsync(Stream stream, string status, CancellationToken ct)
    {
        var payload = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        return stream.WriteAsync(payload, ct).AsTask();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
        }

        _cts.Dispose();
    }
}
