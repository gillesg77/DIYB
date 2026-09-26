using DIYB.Core.Devices;
using DIYB.Core.Protocol;
using Xunit;

namespace DIYB.Core.Tests;

public class DeviceStateParserTests
{
    [Fact]
    public void Lit_un_appareil_mono_canal()
    {
        const string json = """
            {"switch":"on","startup":"stay","pulse":"off","pulseWidth":1500,
             "ssid":"ATELIER","otaUnlock":false,"fwVersion":"3.7.2","rssi":-58,"staMac":"aa:bb:cc:dd:ee:ff"}
            """;

        var state = DeviceStateParser.Parse("10011c676a", json, "diy_plug");

        Assert.False(state.IsMultiChannel);
        Assert.Single(state.Channels);
        Assert.Equal(SwitchState.On, state.Channels[0].Switch);
        Assert.Equal(StartupMode.Keep, state.Channels[0].Startup);
        Assert.False(state.Channels[0].PulseEnabled);
        Assert.Equal(1500, state.Channels[0].PulseWidthMs);
        Assert.Equal("3.7.2", state.FirmwareVersion);
        Assert.Equal(-58, state.Rssi);
        Assert.Equal("diy_plug", state.DeviceType);
    }

    [Fact]
    public void Lit_un_appareil_multi_canaux_en_respectant_les_index()
    {
        const string json = """
            {"switches":[{"switch":"on","outlet":0},{"switch":"off","outlet":1}],
             "configure":[{"startup":"off","outlet":1},{"startup":"on","outlet":0}],
             "pulses":[{"pulse":"on","width":1000,"outlet":1}],
             "fwVersion":"3.6.0"}
            """;

        var state = DeviceStateParser.Parse("1001abcdef", json);

        Assert.True(state.IsMultiChannel);
        Assert.Equal(2, state.Channels.Length);

        var first = state.Channel(0)!;
        Assert.Equal(SwitchState.On, first.Switch);
        Assert.Equal(StartupMode.On, first.Startup);
        Assert.False(first.PulseEnabled);

        var second = state.Channel(1)!;
        Assert.Equal(SwitchState.Off, second.Switch);
        Assert.Equal(StartupMode.Off, second.Startup);
        Assert.True(second.PulseEnabled);
        Assert.Equal(1000, second.PulseWidthMs);
    }

    [Fact]
    public void Accepte_un_rssi_serialise_en_chaine()
    {
        var state = DeviceStateParser.Parse("1001", """{"switch":"off","rssi":"-71"}""");

        Assert.Equal(-71, state.Rssi);
    }

    [Fact]
    public void Ne_cree_aucun_canal_sans_information_d_etat()
    {
        var state = DeviceStateParser.Parse("1001", """{"fwVersion":"3.7.2"}""");

        Assert.Empty(state.Channels);
        Assert.Equal("3.7.2", state.FirmwareVersion);
    }

    [Fact]
    public void Ecrit_et_relit_un_etat_multi_canaux()
    {
        const string json = """
            {"switches":[{"switch":"on","outlet":0},{"switch":"off","outlet":1}],
             "configure":[{"startup":"stay","outlet":0},{"startup":"off","outlet":1}],
             "pulses":[{"pulse":"on","width":2000,"outlet":0},{"pulse":"off","width":500,"outlet":1}],
             "fwVersion":"3.7.2","rssi":-60}
            """;

        var original = DeviceStateParser.Parse("1001", json);
        var roundTripped = DeviceStateParser.Parse("1001", DeviceStateWriter.Write(original));

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void La_fusion_ne_supprime_pas_les_champs_absents()
    {
        var complete = DeviceStateParser.Parse("1001", """{"switch":"on","fwVersion":"3.7.2","ssid":"ATELIER"}""");
        var partial = DeviceStateParser.Parse("1001", """{"switch":"off"}""");

        var merged = complete.MergeWith(partial);

        Assert.Equal(SwitchState.Off, merged.Channels[0].Switch);
        Assert.Equal("3.7.2", merged.FirmwareVersion);
        Assert.Equal("ATELIER", merged.Ssid);
    }
}
