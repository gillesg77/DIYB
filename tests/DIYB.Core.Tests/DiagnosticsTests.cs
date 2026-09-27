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
    public void Le_registre_signale_un_entrant_probablement_bloque()
    {
        using var mdns = new MdnsClient();
        using var registry = new DeviceRegistry(mdns, new ApiLog());

        // Rien reçu : sur un réseau vivant, l'absence totale de trafic mDNS
        // désigne le pare-feu, pas l'absence d'appareils.
        Assert.True(registry.InboundLikelyBlocked);
        Assert.Equal(0, registry.PacketsReceived);
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
