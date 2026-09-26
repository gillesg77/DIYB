using System.Collections.Immutable;
using System.Net;
using DIYB.Core.Devices;
using DIYB.Core.Firmware;
using Xunit;

namespace DIYB.Core.Tests;

public class FirmwareVersionTests
{
    [Theory]
    [InlineData("3.7.2", "3.7.10", -1)]
    [InlineData("3.7.2", "3.7.2", 0)]
    [InlineData("3.8.0", "3.7.9", 1)]
    [InlineData("v3.7.2", "3.7.2", 0)]
    [InlineData("3.7", "3.7.0", 0)]
    [InlineData("4.0.0", "3.99.99", 1)]
    public void Compare_les_versions_segment_par_segment(string left, string right, int expected)
    {
        var comparison = FirmwareVersion.Parse(left).CompareTo(FirmwareVersion.Parse(right));

        Assert.Equal(expected, Math.Sign(comparison));
    }

    [Fact]
    public void Une_preversion_precede_la_version_definitive()
    {
        Assert.True(FirmwareVersion.Parse("3.7.2-rc1") < FirmwareVersion.Parse("3.7.2"));
    }

    [Fact]
    public void Une_version_absente_est_vide()
    {
        Assert.True(FirmwareVersion.Parse(null).IsEmpty);
        Assert.True(FirmwareVersion.Parse("   ").IsEmpty);
    }
}

public class FirmwareAdvisorTests
{
    [Fact]
    public async Task Signale_une_mise_a_jour_disponible_au_catalogue()
    {
        var advisor = new FirmwareAdvisor(new[]
        {
            Catalog(new FirmwareRelease { Model = "diy_plug", Version = "3.8.0" }),
        });

        await advisor.RefreshAsync();
        var advice = advisor.Advise(new[] { Device("a", "3.7.2", "diy_plug") }).Single();

        Assert.Equal(UpdateStatus.UpdateAvailable, advice.Status);
        Assert.Equal("3.8.0", advice.Candidate!.Version);
        Assert.False(advice.FromFleetOnly);
    }

    [Fact]
    public async Task Ignore_les_preversions_sauf_demande_explicite()
    {
        var advisor = new FirmwareAdvisor(new[]
        {
            Catalog(
                new FirmwareRelease { Model = "*", Version = "3.7.2" },
                new FirmwareRelease { Model = "*", Version = "3.9.0", Channel = "prerelease" }),
        });

        await advisor.RefreshAsync();
        var devices = new[] { Device("a", "3.7.2", "diy_plug") };

        Assert.Equal(UpdateStatus.UpToDate, advisor.Advise(devices).Single().Status);
        Assert.Equal(UpdateStatus.UpdateAvailable, advisor.Advise(devices, includePrerelease: true).Single().Status);
    }

    [Fact]
    public void Detecte_un_retard_par_rapport_au_parc_sans_catalogue()
    {
        var advisor = new FirmwareAdvisor(Array.Empty<IFirmwareCatalog>());

        var advice = advisor.Advise(new[]
        {
            Device("ancien", "3.5.0", "diy_plug"),
            Device("recent", "3.7.2", "diy_plug"),
        });

        var late = advice.Single(a => a.DeviceId == "ancien");
        Assert.Equal(UpdateStatus.UpdateAvailable, late.Status);
        Assert.True(late.FromFleetOnly);

        Assert.Equal(UpdateStatus.Unknown, advice.Single(a => a.DeviceId == "recent").Status);
    }

    private static IFirmwareCatalog Catalog(params FirmwareRelease[] releases) => new StubCatalog(releases);

    private static DiyDevice Device(string id, string firmware, string type) => new()
    {
        DeviceId = id,
        Address = IPAddress.Loopback,
        State = new DeviceState
        {
            DeviceId = id,
            FirmwareVersion = firmware,
            DeviceType = type,
            Channels = ImmutableArray.Create(new ChannelState { Outlet = 0 }),
        },
    };

    private sealed class StubCatalog : IFirmwareCatalog
    {
        private readonly IReadOnlyList<FirmwareRelease> _releases;

        public StubCatalog(IReadOnlyList<FirmwareRelease> releases) => _releases = releases;

        public string Name => "stub";

        public Task<IReadOnlyList<FirmwareRelease>> GetReleasesAsync(CancellationToken ct = default) =>
            Task.FromResult(_releases);
    }
}
