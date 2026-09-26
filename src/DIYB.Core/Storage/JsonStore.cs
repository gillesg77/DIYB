using System.Text.Json;
using System.Text.Json.Serialization;

namespace DIYB.Core.Storage;

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T?> LoadAsync<T>(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return default;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct).ConfigureAwait(false);
    }

    /// <summary>Écriture par fichier temporaire puis remplacement : une coupure en
    /// cours d'enregistrement laisse l'ancien fichier intact.</summary>
    public static async Task SaveAsync<T>(string path, T value, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";

        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, value, Options, ct).ConfigureAwait(false);

        if (File.Exists(path))
            File.Replace(temporary, path, null);
        else
            File.Move(temporary, path);
    }
}
