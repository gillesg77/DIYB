using DIYB.Core.Diagnostics;
using DIYB.Core.Discovery;
using DIYB.Core.Mdns;
using DIYB.Localization;
using Xunit;

namespace DIYB.Core.Tests;

[Collection("Localisation")]
public class InboundDiagnosticTests
{
    [Fact]
    public void Un_client_jamais_demarre_n_a_rien_recu()
    {
        using var mdns = new MdnsClient();

        Assert.Equal(0, mdns.PacketsReceived);
    }

    [Fact]
    public void Un_chemin_sans_regle_ne_peut_pas_recevoir()
    {
        // Aucune règle ne vise ce chemin : l'entrant est refusé par défaut.
        var status = FirewallInspector.Inspect(@"C:\introuvableucun-programme.exe");

        Assert.Equal(FirewallVerdict.NoRule, status.Verdict);
        Assert.True(status.PreventsDiscovery);
    }

    [Fact]
    public void Un_chemin_vide_ne_donne_aucun_verdict()
    {
        // Ne rien conclure vaut mieux qu'accuser le pare-feu à tort.
        Assert.Equal(FirewallVerdict.Unknown, FirewallInspector.Inspect(null).Verdict);
        Assert.Equal(FirewallVerdict.Unknown, FirewallInspector.Inspect("   ").Verdict);
        Assert.False(FirewallInspector.Inspect(null).PreventsDiscovery);
    }

    [Fact]
    public void L_inspection_du_processus_courant_aboutit()
    {
        // Le verdict dépend du poste ; seule compte l'absence d'exception.
        var status = FirewallInspector.InspectCurrentProcess();

        Assert.True(Enum.IsDefined(status.Verdict));
    }

    [Fact]
    public void Le_registre_expose_le_verdict_du_pare_feu()
    {
        using var mdns = new MdnsClient();
        using var registry = new DeviceRegistry(mdns, new ApiLog());

        Assert.Equal(0, registry.PacketsReceived);
        Assert.Equal(registry.Firewall.PreventsDiscovery, registry.InboundBlocked);
    }

    [Fact]
    public void Le_message_de_pare_feu_existe_dans_toutes_les_langues()
    {
        var dossier = Path.Combine(AppContext.BaseDirectory, "Strings");

        foreach (var fichier in Directory.GetFiles(dossier, "*.json"))
        {
            Localizer.Current.Load(dossier, Path.GetFileNameWithoutExtension(fichier));
            var message = Localizer.Current["devices.emptyFirewall"];

            Assert.NotEqual("devices.emptyFirewall", message);
            Assert.True(message.Length > 40, Path.GetFileName(fichier));
        }
    }

    [Fact]
    public void Le_message_de_pare_feu_se_distingue_du_message_ordinaire()
    {
        var dossier = Path.Combine(AppContext.BaseDirectory, "Strings");
        Localizer.Current.Load(dossier, "fr");

        Assert.NotEqual(Localizer.Current["devices.empty"], Localizer.Current["devices.emptyFirewall"]);
    }
}
