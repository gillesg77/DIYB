using DIYB.Core.Storage;
using Xunit;

namespace DIYB.Core.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _fichier = Path.Combine(Path.GetTempPath(), $"diyb-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Un_aller_retour_preserve_tous_les_champs()
    {
        var settings = new AppSettings
        {
            Language = "fr",
            ActiveProfile = "atelier",
            ShowCloudDevices = true,
            FirmwareCatalogs = new[] { "https://serveur/firmwares.json" },
            Window = new WindowPlacement { X = 100, Y = 50, Width = 1400, Height = 900, Maximized = true },
        };

        await JsonStore.SaveAsync(_fichier, settings);
        var relu = await JsonStore.LoadAsync<AppSettings>(_fichier);

        // Champ par champ : l'égalité synthétisée d'un record compare les listes
        // par référence, deux instances au contenu identique passent pour distinctes.
        Assert.NotNull(relu);
        Assert.Equal(settings.Language, relu!.Language);
        Assert.Equal(settings.ActiveProfile, relu.ActiveProfile);
        Assert.Equal(settings.ShowCloudDevices, relu.ShowCloudDevices);
        Assert.Equal(settings.FirmwareCatalogs, relu.FirmwareCatalogs);
        Assert.Equal(settings.Window, relu.Window);
    }

    [Fact]
    public async Task Modifier_un_champ_ne_supprime_pas_les_autres()
    {
        // L'interface reconstruisait les réglages de zéro à la fermeture, ce qui
        // effaçait les catalogues de firmware qu'elle ne pilote pas.
        var initial = new AppSettings
        {
            Language = "en",
            FirmwareCatalogs = new[] { "C:/catalogue.json" },
        };

        await JsonStore.SaveAsync(_fichier, initial with { Language = "de" });
        var relu = await JsonStore.LoadAsync<AppSettings>(_fichier);

        Assert.Equal("de", relu!.Language);
        Assert.Equal(new[] { "C:/catalogue.json" }, relu.FirmwareCatalogs);
    }

    [Fact]
    public void La_lecture_synchrone_tolere_un_fichier_absent_ou_illisible()
    {
        Assert.Null(JsonStore.Load<AppSettings>(_fichier));

        File.WriteAllText(_fichier, "{ ceci n'est pas du json");
        Assert.Null(JsonStore.Load<AppSettings>(_fichier));
    }

    [Fact]
    public async Task Des_reglages_anciens_restent_lisibles()
    {
        // Un fichier écrit avant l'ajout de la géométrie ne doit pas empêcher
        // l'application de démarrer.
        await File.WriteAllTextAsync(_fichier, """{ "language": "it", "showCloudDevices": true }""");

        var relu = JsonStore.Load<AppSettings>(_fichier);

        Assert.NotNull(relu);
        Assert.Equal("it", relu!.Language);
        Assert.True(relu.ShowCloudDevices);
        Assert.Null(relu.Window);
    }

    public void Dispose()
    {
        if (File.Exists(_fichier))
            File.Delete(_fichier);
    }
}
