using System.Collections.Immutable;
using System.Net;
using DIYB.Core.Devices;
using DIYB.Core.Storage;
using Xunit;

namespace DIYB.Core.Tests;

public class ConfigProfileTests
{
    [Fact]
    public void Ne_propose_que_les_ecarts_reels()
    {
        var profile = new ConfigProfile { Name = "atelier", Startup = StartupMode.Off };

        var conforme = Device(new ChannelState { Outlet = 0, Startup = StartupMode.Off });
        var derive = Device(new ChannelState { Outlet = 0, Startup = StartupMode.On });

        Assert.Empty(profile.Plan(conforme));
        Assert.Equal(ComplianceState.Compliant, profile.Evaluate(conforme));

        var change = Assert.Single(profile.Plan(derive));
        Assert.Equal(ConfigProperty.Startup, change.Property);
        Assert.Equal("On", change.From);
        Assert.Equal("Off", change.To);
        Assert.Equal(ComplianceState.Drifted, profile.Evaluate(derive));
    }

    [Fact]
    public void Planifie_chaque_canal_d_un_appareil_multi_canaux()
    {
        var profile = new ConfigProfile { Name = "tout-off", Startup = StartupMode.Off };

        var device = Device(
            new ChannelState { Outlet = 0, Startup = StartupMode.On },
            new ChannelState { Outlet = 1, Startup = StartupMode.Off },
            new ChannelState { Outlet = 2, Startup = StartupMode.Keep });

        var changes = profile.Plan(device);

        Assert.Equal(2, changes.Count);
        Assert.Equal(new[] { 0, 2 }, changes.Select(c => c.Outlet));
    }

    [Fact]
    public void La_largeur_d_impulsion_n_est_planifiee_que_si_le_mode_est_actif()
    {
        var profile = new ConfigProfile { Name = "impulsion", PulseWidthMs = 2000 };

        var inactif = Device(new ChannelState { Outlet = 0, PulseEnabled = false, PulseWidthMs = 500 });
        var actif = Device(new ChannelState { Outlet = 0, PulseEnabled = true, PulseWidthMs = 500 });

        Assert.Empty(profile.Plan(inactif));
        Assert.Single(profile.Plan(actif));
    }

    [Fact]
    public void Un_etat_inconnu_ne_vaut_pas_conformite()
    {
        var profile = new ConfigProfile { Name = "atelier", Startup = StartupMode.Off };
        var device = Device(new ChannelState { Outlet = 0, Startup = StartupMode.Unknown });

        Assert.Equal(ComplianceState.Unknown, profile.Evaluate(device));
    }

    [Fact]
    public void Le_ciblage_par_etiquette_restreint_le_profil()
    {
        var profile = new ConfigProfile { Name = "atelier", Tags = new[] { "atelier" } };

        Assert.True(profile.AppliesTo(new[] { "atelier", "rdc" }));
        Assert.False(profile.AppliesTo(new[] { "bureau" }));
        Assert.True(new ConfigProfile { Name = "global" }.AppliesTo(new[] { "bureau" }));
    }

    private static DiyDevice Device(params ChannelState[] channels) => new()
    {
        DeviceId = "10011c676a",
        Address = IPAddress.Loopback,
        State = new DeviceState { DeviceId = "10011c676a", Channels = channels.ToImmutableArray() },
    };
}
