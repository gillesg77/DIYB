using DIYB.Core.Devices;

namespace DIYB.Core.Protocol;

/// <summary>Achemine une requête vers un appareil.</summary>
public interface IDiyTransport
{
    Task<string> PostAsync(DiyDevice device, string endpoint, string body, CancellationToken ct);
}
