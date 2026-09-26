using DIYB.Core.Devices;
using DIYB.Core.Discovery;
using DIYB.Core.Mdns;
using Xunit;

namespace DIYB.Core.Tests;

public class DiscoveryTests
{
    private const string Instance = "eWeLink_10011c676a._ewelink._tcp.local";
    private const string Host = "eWeLink_10011c676a.local";

    [Fact]
    public void Assemble_un_appareil_depuis_une_annonce_complete()
    {
        var message = Read(new DnsMessageBuilder()
            .Ptr(EwelinkTxt.ServiceType, Instance)
            .Srv(Instance, Host, 8081)
            .Txt(Instance, new[]
            {
                "txtvers=1",
                "id=10011c676a",
                "type=diy_plug",
                "apivers=1",
                "data1={\"switch\":\"on\",\"startup\":\"stay\",\"pulse\":\"off\",",
                "data2=\"pulseWidth\":500,\"fwVersion\":\"3.7.2\",\"rssi\":-58}",
            })
            .A(Host, "192.168.1.42")
            .Build());

        var events = new EwelinkRecordAssembler().Consume(message, DateTimeOffset.Now);

        var device = Assert.IsType<DeviceSeen>(Assert.Single(events)).Device;
        Assert.Equal("10011c676a", device.DeviceId);
        Assert.Equal("192.168.1.42", device.Address.ToString());
        Assert.Equal(8081, device.Port);
        Assert.Equal(SwitchState.On, device.State.Channels[0].Switch);
        Assert.Equal(StartupMode.Keep, device.State.Channels[0].Startup);
        Assert.Equal("3.7.2", device.State.FirmwareVersion);
        Assert.Equal("diy_plug", device.State.DeviceType);
    }

    [Fact]
    public void Attend_l_enregistrement_d_adresse_avant_de_publier()
    {
        var assembler = new EwelinkRecordAssembler();

        var withoutAddress = assembler.Consume(Read(new DnsMessageBuilder()
            .Srv(Instance, Host, 8081)
            .Txt(Instance, new[] { "id=10011c676a", "data1={\"switch\":\"off\"}" })
            .Build()), DateTimeOffset.Now);

        Assert.Empty(withoutAddress.OfType<DeviceSeen>());

        var resolve = Assert.Single(withoutAddress.OfType<ResolveNeeded>());
        Assert.Equal(Host, resolve.Name);
        Assert.Equal(DnsType.A, resolve.Type);

        var withAddress = assembler.Consume(Read(new DnsMessageBuilder()
            .A(Host, "192.168.1.42")
            .Build()), DateTimeOffset.Now);

        var device = Assert.IsType<DeviceSeen>(Assert.Single(withAddress)).Device;
        Assert.Equal("192.168.1.42", device.Address.ToString());
    }

    [Fact]
    public void Un_ptr_seul_declenche_les_requetes_srv_et_txt()
    {
        // Les modules répondent à la requête de service par le seul PTR : sans
        // requête de suivi, l'appareil resterait invisible.
        var events = new EwelinkRecordAssembler().Consume(Read(new DnsMessageBuilder()
            .Ptr(EwelinkTxt.ServiceType, Instance)
            .Build()), DateTimeOffset.Now);

        var asked = events.OfType<ResolveNeeded>().ToArray();

        Assert.Empty(events.OfType<DeviceSeen>());
        Assert.Contains(asked, r => r.Name == Instance && r.Type == DnsType.Srv);
        Assert.Contains(asked, r => r.Name == Instance && r.Type == DnsType.Txt);
    }

    [Fact]
    public void Un_ptr_a_ttl_nul_retire_l_appareil()
    {
        var assembler = new EwelinkRecordAssembler();

        assembler.Consume(Read(new DnsMessageBuilder()
            .Srv(Instance, Host, 8081)
            .Txt(Instance, new[] { "id=10011c676a", "data1={\"switch\":\"on\"}" })
            .A(Host, "192.168.1.42")
            .Build()), DateTimeOffset.Now);

        var events = assembler.Consume(Read(new DnsMessageBuilder()
            .Ptr(EwelinkTxt.ServiceType, Instance, ttl: 0)
            .Build()), DateTimeOffset.Now);

        var gone = Assert.IsType<DeviceGone>(Assert.Single(events));
        Assert.Equal("10011c676a", gone.DeviceId);
    }

    [Fact]
    public void Deduit_l_identifiant_du_nom_d_instance_si_le_txt_ne_le_porte_pas()
    {
        var txt = new Dictionary<string, string> { ["type"] = "diy_plug" };

        Assert.Equal("10011c676a", EwelinkTxt.DeviceIdFrom(txt, Instance));
    }

    [Fact]
    public void Concatene_les_segments_d_etat_dans_l_ordre()
    {
        var txt = new Dictionary<string, string>
        {
            ["data1"] = "{\"switch\":",
            ["data2"] = "\"on\",",
            ["data3"] = "\"rssi\":-62}",
        };

        Assert.Equal("{\"switch\":\"on\",\"rssi\":-62}", EwelinkTxt.JoinStateChunks(txt));
    }

    [Fact]
    public void Un_txt_chiffre_ne_produit_pas_d_etat_de_canal()
    {
        var txt = new Dictionary<string, string>
        {
            ["encrypt"] = "true",
            ["type"] = "diy_plug",
            ["data1"] = "charge-chiffree",
        };

        var state = EwelinkTxt.ParseState(txt, "10011c676a");

        Assert.NotNull(state);
        Assert.Empty(state!.Channels);
        Assert.Equal("diy_plug", state.DeviceType);

        // Un module au TXT chiffré est resté appairé au cloud : rien n'est pilotable.
        Assert.True(state.RequiresKey);
        Assert.False(state.IsControllable);
    }

    [Fact]
    public void Un_module_en_mode_diy_est_pilotable()
    {
        var txt = new Dictionary<string, string>
        {
            ["type"] = "diy_plug",
            ["data1"] = "{\"switch\":\"off\",\"startup\":\"stay\"}",
        };

        var state = EwelinkTxt.ParseState(txt, "10011c676a");

        Assert.NotNull(state);
        Assert.True(state!.IsControllable);
        Assert.Equal(StartupMode.Keep, state.Channels[0].Startup);
    }

    private static DnsMessage Read(byte[] payload) => DnsMessageCodec.Read(payload, payload.Length);
}
