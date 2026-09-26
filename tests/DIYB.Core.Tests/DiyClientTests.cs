using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;
using DIYB.Core.Protocol;
using Xunit;

namespace DIYB.Core.Tests;

internal sealed class RecordingTransport : IDiyTransport
{
    public List<(string Endpoint, string Body)> Calls { get; } = new();

    public string Response { get; set; } = "{\"seq\":1,\"error\":0,\"data\":{}}";

    public Task<string> PostAsync(DiyDevice device, string endpoint, string body, CancellationToken ct)
    {
        Calls.Add((endpoint, body));
        return Task.FromResult(Response);
    }

    public JsonElement LastData()
    {
        using var document = JsonDocument.Parse(Calls[^1].Body);
        return document.RootElement.GetProperty("data").Clone();
    }
}

public class DiyClientTests
{
    [Fact]
    public async Task Un_appareil_mono_canal_utilise_le_point_d_entree_simple()
    {
        var (client, transport, device) = Build(channels: 1);

        await client.SetSwitchAsync(device, null, on: true);

        Assert.Equal(DiyEndpoints.Switch, transport.Calls[^1].Endpoint);
        Assert.Equal("on", transport.LastData().GetProperty("switch").GetString());
    }

    [Fact]
    public async Task Un_appareil_multi_canaux_vise_tous_les_canaux_par_defaut()
    {
        var (client, transport, device) = Build(channels: 4);

        await client.SetSwitchAsync(device, null, on: false);

        Assert.Equal(DiyEndpoints.Switches, transport.Calls[^1].Endpoint);

        var switches = transport.LastData().GetProperty("switches").EnumerateArray().ToArray();
        Assert.Equal(4, switches.Length);
        Assert.Equal(new[] { 0, 1, 2, 3 }, switches.Select(s => s.GetProperty("outlet").GetInt32()));
        Assert.All(switches, s => Assert.Equal("off", s.GetProperty("switch").GetString()));
    }

    [Fact]
    public async Task Le_mode_de_demarrage_multi_canaux_passe_par_configure()
    {
        var (client, transport, device) = Build(channels: 2);

        await client.SetStartupAsync(device, outlet: 1, StartupMode.Keep);

        Assert.Equal(DiyEndpoints.Startup, transport.Calls[^1].Endpoint);

        var configure = transport.LastData().GetProperty("configure").EnumerateArray().Single();
        Assert.Equal(1, configure.GetProperty("outlet").GetInt32());
        Assert.Equal("stay", configure.GetProperty("startup").GetString());
    }

    [Theory]
    [InlineData(700)]
    [InlineData(0)]
    [InlineData(400)]
    [InlineData(40_000_000)]
    public async Task Une_largeur_d_impulsion_invalide_est_refusee_avant_emission(int width)
    {
        var (client, transport, device) = Build(channels: 1);

        var error = await Assert.ThrowsAsync<DiyException>(() => client.SetPulseAsync(device, null, true, width));

        Assert.Equal(ErrorCode.PulseWidthOutOfRange, error.Code);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public async Task Un_canal_inconnu_est_refuse()
    {
        var (client, _, device) = Build(channels: 2);

        var error = await Assert.ThrowsAsync<DiyException>(() => client.SetSwitchAsync(device, outlet: 7, on: true));

        Assert.Equal(ErrorCode.UnknownOutlet, error.Code);
    }

    [Fact]
    public async Task Le_code_d_erreur_du_firmware_est_traduit()
    {
        var (client, transport, device) = Build(channels: 1);
        transport.Response = "{\"seq\":1,\"error\":403}";

        var error = await Assert.ThrowsAsync<DiyException>(() => client.GetInfoAsync(device));

        Assert.Equal(ErrorCode.FirmwareDeviceMismatch, error.Code);
    }

    [Fact]
    public async Task Une_reponse_illisible_remonte_en_reponse_invalide()
    {
        var (client, transport, device) = Build(channels: 1);
        transport.Response = "pas du json";

        var error = await Assert.ThrowsAsync<DiyException>(() => client.GetInfoAsync(device));

        Assert.Equal(ErrorCode.InvalidResponse, error.Code);
    }

    [Fact]
    public async Task La_cle_wifi_n_apparait_pas_dans_le_journal()
    {
        var log = new ApiLog();
        var transport = new RecordingTransport();
        var client = new DiyClient(transport, log);
        var device = Device(1);

        await client.SetWifiAsync(device, "ATELIER", "motdepasse-secret");

        var entry = log.Snapshot().Last();
        Assert.DoesNotContain("motdepasse-secret", entry.Request);
        Assert.Contains("***", entry.Request);
        Assert.Contains("ATELIER", entry.Request);
    }

    [Fact]
    public async Task Une_cle_wifi_trop_courte_est_refusee()
    {
        var (client, transport, device) = Build(channels: 1);

        var error = await Assert.ThrowsAsync<DiyException>(() => client.SetWifiAsync(device, "ATELIER", "court"));

        Assert.Equal(ErrorCode.WifiCredentialsInvalid, error.Code);
        Assert.Empty(transport.Calls);
    }

    private static (DiyClient Client, RecordingTransport Transport, DiyDevice Device) Build(int channels)
    {
        var transport = new RecordingTransport();
        return (new DiyClient(transport, new ApiLog()), transport, Device(channels));
    }

    private static DiyDevice Device(int channels) => new()
    {
        DeviceId = "10011c676a",
        Address = IPAddress.Parse("192.168.1.42"),
        State = new DeviceState
        {
            DeviceId = "10011c676a",
            Channels = Enumerable.Range(0, channels)
                .Select(outlet => new ChannelState { Outlet = outlet, Switch = SwitchState.Off })
                .ToImmutableArray(),
        },
    };
}
