using System.Text.Json;
using System.Text.Json.Serialization;

namespace DIYB.Core.Firmware;

/// <summary>Catalogue lu depuis un fichier local ou une URL. Format attendu :
/// <c>{ "releases": [ { "model": …, "version": …, "url": …, "sha256": … } ] }</c>.</summary>
public sealed class JsonFirmwareCatalog : IFirmwareCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _location;
    private readonly HttpClient? _http;

    public JsonFirmwareCatalog(string location, HttpClient? http = null)
    {
        _location = location;
        _http = http;
    }

    public string Name => _location;

    public async Task<IReadOnlyList<FirmwareRelease>> GetReleasesAsync(CancellationToken ct = default)
    {
        var json = Uri.TryCreate(_location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? await FetchAsync(uri, ct).ConfigureAwait(false)
            : await File.ReadAllTextAsync(_location, ct).ConfigureAwait(false);

        var document = JsonSerializer.Deserialize<CatalogDocument>(json, Options);
        if (document?.Releases is null)
            return Array.Empty<FirmwareRelease>();

        return document.Releases
            .Where(r => !string.IsNullOrWhiteSpace(r.Version))
            .Select(r => new FirmwareRelease
            {
                Model = string.IsNullOrWhiteSpace(r.Model) ? "*" : r.Model!,
                Version = r.Version!,
                Url = Uri.TryCreate(r.Url, UriKind.Absolute, out var u) ? u : null,
                Sha256 = r.Sha256?.ToLowerInvariant(),
                Channel = string.IsNullOrWhiteSpace(r.Channel) ? "release" : r.Channel!,
                PublishedAt = r.PublishedAt,
                Notes = r.Notes,
                Source = Name,
            })
            .ToArray();
    }

    private async Task<string> FetchAsync(Uri uri, CancellationToken ct)
    {
        var http = _http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            return await http.GetStringAsync(uri, ct).ConfigureAwait(false);
        }
        finally
        {
            if (_http is null)
                http.Dispose();
        }
    }

    private sealed class CatalogDocument
    {
        public List<ReleaseEntry>? Releases { get; set; }
    }

    private sealed class ReleaseEntry
    {
        public string? Model { get; set; }

        public string? Version { get; set; }

        public string? Url { get; set; }

        public string? Sha256 { get; set; }

        public string? Channel { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }

        public string? Notes { get; set; }
    }
}
