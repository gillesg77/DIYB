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

    /// <summary>Lecture synchrone, pour ce qui doit être connu avant le premier
    /// rendu — la géométrie de la fenêtre, sous peine de la voir sauter.</summary>
    public static T? Load<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return default;
        }
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
