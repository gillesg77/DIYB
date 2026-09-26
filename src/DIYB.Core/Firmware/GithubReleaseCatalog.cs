using System.Net.Http.Headers;
using System.Text.Json;

namespace DIYB.Core.Firmware;

/// <summary>Catalogue adossé aux publications GitHub d'un dépôt de firmware
/// alternatif (Tasmota, ESPHome). L'API impose un en-tête User-Agent.</summary>
public sealed class GithubReleaseCatalog : IFirmwareCatalog
{
    private readonly string _owner;
    private readonly string _repository;
    private readonly string _assetPattern;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public GithubReleaseCatalog(string owner, string repository, string assetPattern = ".bin", HttpClient? http = null)
    {
        _owner = owner;
        _repository = repository;
        _assetPattern = assetPattern;
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DIYB", "1.0"));
    }

    public string Name => $"github:{_owner}/{_repository}";

    public async Task<IReadOnlyList<FirmwareRelease>> GetReleasesAsync(CancellationToken ct = default)
    {
        var uri = new Uri($"https://api.github.com/repos/{_owner}/{_repository}/releases?per_page=10");
        var json = await _http.GetStringAsync(uri, ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        var releases = new List<FirmwareRelease>();

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                continue;

            var tag = entry.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (tag is null)
                continue;

            var prerelease = entry.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            var published = entry.TryGetProperty("published_at", out var d) && d.TryGetDateTimeOffset(out var date)
                ? date
                : (DateTimeOffset?)null;

            if (!entry.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

                if (assetName is null || url is null || !assetName.Contains(_assetPattern, StringComparison.OrdinalIgnoreCase))
                    continue;

                releases.Add(new FirmwareRelease
                {
                    Model = "*",
                    Version = tag,
                    Url = new Uri(url),
                    Channel = prerelease ? "prerelease" : "release",
                    PublishedAt = published,
                    Notes = assetName,
                    Source = Name,
                });
            }
        }

        return releases;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}
