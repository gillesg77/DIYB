using System.Text;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;

namespace DIYB.Core.Protocol;

public sealed class HttpDiyTransport : IDiyTransport, IDisposable
{
    private readonly HttpClient _http;

    public HttpDiyTransport(TimeSpan? timeout = null)
    {
        // Un appareil du réseau local répond en quelques dizaines de millisecondes.
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(3) };
    }

    public async Task<string> PostAsync(DiyDevice device, string endpoint, string body, CancellationToken ct)
    {
        var uri = new Uri(device.BaseUri, endpoint);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.PostAsync(uri, content, ct).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new DiyException(ErrorCode.Timeout, $"Timeout on {uri}.", Args(device), e);
        }
        catch (HttpRequestException e)
        {
            throw new DiyException(ErrorCode.Unreachable, $"Cannot reach {uri}.", Args(device), e);
        }
    }

    private static Dictionary<string, object?> Args(DiyDevice device) => new()
    {
        ["deviceId"] = device.DeviceId,
        ["address"] = device.Address.ToString(),
    };

    public void Dispose() => _http.Dispose();
}
