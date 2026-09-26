using System.Text.RegularExpressions;
using DIYB.Core.Diagnostics;
using DIYB.Localization;
using Xunit;

namespace DIYB.Core.Tests;

public class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{[A-Za-z0-9_]+\}", RegexOptions.Compiled);

    private static string StringsDirectory => Path.Combine(AppContext.BaseDirectory, "Strings");

    [Fact]
    public void Les_catalogues_sont_livres_avec_l_application()
    {
        Assert.True(Directory.Exists(StringsDirectory), StringsDirectory);
        Assert.True(Directory.GetFiles(StringsDirectory, "*.json").Length >= 2);
    }

    [Fact]
    public void Chaque_langue_couvre_toutes_les_cles_de_reference()
    {
        var reference = Load("en");

        foreach (var path in Directory.GetFiles(StringsDirectory, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(path);
            var catalog = Load(code);

            var missing = reference.Keys.Where(k => !catalog.ContainsKey(k)).ToArray();
            var extra = catalog.Keys.Where(k => !reference.ContainsKey(k)).ToArray();

            Assert.True(missing.Length == 0, $"{code} : clés manquantes {string.Join(", ", missing)}");
            Assert.True(extra.Length == 0, $"{code} : clés en trop {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void Les_marqueurs_de_substitution_sont_preserves()
    {
        // Un marqueur perdu à la traduction casse le formatage à l'exécution.
        var reference = Load("en");

        foreach (var path in Directory.GetFiles(StringsDirectory, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(path);
            var catalog = Load(code);

            foreach (var (key, expected) in reference)
            {
                if (!catalog.TryGetValue(key, out var translated))
                    continue;

                var wanted = Placeholder.Matches(expected).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal);
                var actual = Placeholder.Matches(translated).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal);

                Assert.True(wanted.SequenceEqual(actual), $"{code} / {key}");
            }
        }
    }

    [Fact]
    public void Chaque_langue_porte_son_nom_affiche()
    {
        Localizer.Current.Load(StringsDirectory);

        Assert.All(Localizer.Current.Languages, language =>
        {
            Assert.False(string.IsNullOrWhiteSpace(language.DisplayName));
            Assert.NotEqual(language.Code, language.DisplayName);
        });
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("he", true)]
    [InlineData("fa", true)]
    [InlineData("ur", true)]
    [InlineData("fr", false)]
    [InlineData("zh-Hans", false)]
    public void Le_sens_de_lecture_est_reconnu(string code, bool rightToLeft)
    {
        Assert.Equal(rightToLeft, Localizer.IsRightToLeftCode(code));
    }

    [Fact]
    public void Une_cle_absente_retombe_sur_l_anglais()
    {
        Localizer.Current.Load(StringsDirectory, "fr");

        // Clé inexistante : la clé elle-même est rendue, sans exception.
        Assert.Equal("clé.inexistante", Localizer.Current["clé.inexistante"]);
        Assert.Equal("Français", Localizer.Current["language.name"]);
    }

    [Fact]
    public void Les_erreurs_du_coeur_sont_traduites_avec_leurs_parametres()
    {
        Localizer.Current.Load(StringsDirectory, "fr");

        var error = new DiyException(ErrorCode.Unreachable, "debug", new Dictionary<string, object?>
        {
            ["deviceId"] = "10011c676a",
            ["address"] = "192.168.5.231",
        });

        var message = Localizer.Current.Describe(error);

        Assert.Contains("10011c676a", message);
        Assert.Contains("192.168.5.231", message);
        Assert.DoesNotContain("{deviceId}", message);
        Assert.DoesNotContain("{address}", message);
    }

    private static Dictionary<string, string> Load(string code)
    {
        var json = File.ReadAllText(Path.Combine(StringsDirectory, code + ".json"));
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    }
}
