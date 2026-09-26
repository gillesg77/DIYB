using System.ComponentModel;
using System.Text.Json;
using DIYB.Core.Diagnostics;

namespace DIYB.Localization;

public sealed record LanguageInfo(string Code, string DisplayName, bool IsRightToLeft);

/// <summary>Traductions chargées depuis des fichiers JSON plats. Le changement de
/// langue est immédiat : les liaisons passent par l'indexeur, invalidé par une
/// notification sur « Item[] ».</summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public const string FallbackLanguage = "en";

    /// <summary>Langues écrites de droite à gauche : l'interface doit inverser son
    /// sens de lecture, pas seulement ses libellés.</summary>
    private static readonly HashSet<string> RightToLeftLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "ar", "he", "iw", "fa", "ur", "ps", "ckb", "yi" };

    private readonly Dictionary<string, Dictionary<string, string>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LanguageInfo> _languages = new();
    private string _language = FallbackLanguage;

    public static Localizer Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LanguageInfo> Languages => _languages;

    public bool IsRightToLeft => IsRightToLeftCode(_language);

    public static bool IsRightToLeftCode(string code) =>
        RightToLeftLanguages.Contains(code.Split('-')[0]);

    public string Language
    {
        get => _language;
        set
        {
            if (string.Equals(_language, value, StringComparison.OrdinalIgnoreCase) || !_catalogs.ContainsKey(value))
                return;

            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? LanguageChanged;

    /// <summary>Clé absente : la clé elle-même est rendue, ce qui rend le manque
    /// visible sans faire échouer l'affichage.</summary>
    public string this[string key] => Lookup(key) ?? key;

    /// <summary>Forme appelable depuis XAML : les liaisons compilées acceptent un
    /// appel de méthode à argument littéral, pas un indexeur à clé pointée.</summary>
    public string Get(string key) => Lookup(key) ?? key;

    public string Format(string key, params object?[] args)
    {
        var pattern = Lookup(key) ?? key;
        return args.Length == 0 ? pattern : string.Format(pattern, args);
    }

    /// <summary>Message destiné à l'utilisateur pour une erreur du coeur. Les
    /// paramètres nommés de l'exception sont substitués sous la forme {nom}.</summary>
    public string Describe(DiyException error)
    {
        var code = error.Code.ToString();
        var key = "error." + char.ToLowerInvariant(code[0]) + code[1..];
        var pattern = Lookup(key) ?? Lookup("error.unknown") ?? error.Message;

        foreach (var (name, value) in error.Args)
            pattern = pattern.Replace("{" + name + "}", value?.ToString() ?? string.Empty, StringComparison.Ordinal);

        return pattern;
    }

    public void Load(string directory, string? preferred = null)
    {
        _catalogs.Clear();
        _languages.Clear();

        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, "*.json"))
                TryLoadFile(file);
        }

        if (_catalogs.Count == 0)
            _catalogs[FallbackLanguage] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, catalog) in _catalogs.OrderBy(c => c.Key, StringComparer.Ordinal))
            _languages.Add(new LanguageInfo(code, catalog.GetValueOrDefault("language.name", code), IsRightToLeftCode(code)));

        _language = Resolve(preferred);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Languages)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

        // Les catalogues arrivent après la construction des vues : sans cet appel, les
        // liaisons déjà évaluées resteraient figées sur le nom de leur clé.
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryLoadFile(string file)
    {
        try
        {
            var json = File.ReadAllText(file);
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (entries is null)
                return;

            var code = Path.GetFileNameWithoutExtension(file);
            _catalogs[code] = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // Fichier de langue illisible : les autres restent disponibles.
        }
    }

    private string Resolve(string? preferred)
    {
        if (preferred is not null && _catalogs.ContainsKey(preferred))
            return preferred;

        var system = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (_catalogs.ContainsKey(system))
            return system;

        return _catalogs.ContainsKey(FallbackLanguage) ? FallbackLanguage : _catalogs.Keys.First();
    }

    /// <summary>Cherche la clé dans la langue courante puis dans la langue de repli,
    /// ce qui permet de livrer une traduction partielle.</summary>
    private string? Lookup(string key)
    {
        if (_catalogs.TryGetValue(_language, out var catalog) && catalog.TryGetValue(key, out var value))
            return value;

        if (_catalogs.TryGetValue(FallbackLanguage, out var fallback) && fallback.TryGetValue(key, out var backup))
            return backup;

        return null;
    }
}
