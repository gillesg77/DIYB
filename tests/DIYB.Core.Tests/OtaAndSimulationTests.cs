using System.Net;
using System.Security.Cryptography;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;
using DIYB.Core.Ota;
using DIYB.Core.Protocol;
using DIYB.Core.Simulation;
using Xunit;

namespace DIYB.Core.Tests;

public class FirmwareServerTests
{
    [Fact]
    public async Task Sert_le_fichier_complet_et_annonce_sa_taille()
    {
        var path = Path.Combine(Path.GetTempPath(), $"diyb-{Guid.NewGuid():N}.bin");
        var content = RandomNumberGenerator.GetBytes(128 * 1024);
        await File.WriteAllBytesAsync(path, content);

        try
        {
            using var server = FirmwareServer.Start(IPAddress.Loopback, path);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var response = await http.GetAsync(server.Url);
            var received = await response.Content.ReadAsByteArrayAsync();

            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal(content.Length, response.Content.Headers.ContentLength);
            Assert.Equal(content, received);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Refuse_une_route_inconnue()
    {
        var path = Path.Combine(Path.GetTempPath(), $"diyb-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });

        try
        {
            using var server = FirmwareServer.Start(IPAddress.Loopback, path);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var response = await http.GetAsync(new Uri($"http://{server.Address}:{server.Port}/autre.bin"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Calcule_l_empreinte_attendue_par_le_firmware()
    {
        var path = Path.Combine(Path.GetTempPath(), $"diyb-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, "firmware"u8.ToArray());

        try
        {
            var hash = await OtaFlasher.ComputeSha256Async(path);

            Assert.Equal(64, hash.Length);
            Assert.Equal(hash.ToLowerInvariant(), hash);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class LocalAddressPickerTests
{
    [Theory]
    [InlineData("192.168.1.10", "192.168.1.42", "255.255.255.0", true)]
    [InlineData("192.168.2.10", "192.168.1.42", "255.255.255.0", false)]
    [InlineData("10.0.0.1", "10.0.255.7", "255.255.0.0", true)]
    public void Compare_les_sous_reseaux(string left, string right, string mask, bool expected)
    {
        var same = LocalAddressPicker.SameSubnet(
            IPAddress.Parse(left), IPAddress.Parse(right), IPAddress.Parse(mask));

        Assert.Equal(expected, same);
    }
}

public class SimulatedDeviceHostTests
{
    [Fact]
    public async Task Le_simulateur_repond_au_protocole_complet()
    {
        var host = new SimulatedDeviceHost { Latency = TimeSpan.Zero };
        var client = new DiyClient(host, new ApiLog());
        var device = host.Create("1001sim0001", channels: 1);

        await client.SetSwitchAsync(device, null, on: false);
        await client.SetStartupAsync(device, null, StartupMode.Off);
        await client.SetPulseAsync(device, null, enabled: true, widthMs: 1500);

        var state = await client.GetInfoAsync(device);

        Assert.Equal(SwitchState.Off, state.Channels[0].Switch);
        Assert.Equal(StartupMode.Off, state.Channels[0].Startup);
        Assert.True(state.Channels[0].PulseEnabled);
        Assert.Equal(1500, state.Channels[0].PulseWidthMs);
    }

    [Fact]
    public async Task Le_simulateur_gere_les_canaux_independamment()
    {
        var host = new SimulatedDeviceHost { Latency = TimeSpan.Zero };
        var client = new DiyClient(host, new ApiLog());
        var device = host.Create("1001sim0004", channels: 4);

        await client.SetStartupAsync(device, outlet: 2, StartupMode.On);
        var state = await client.GetInfoAsync(device);

        Assert.Equal(StartupMode.On, state.Channel(2)!.Startup);
        Assert.Equal(StartupMode.Keep, state.Channel(0)!.Startup);
    }

    [Fact]
    public async Task Le_flash_exige_le_deverrouillage_prealable()
    {
        var host = new SimulatedDeviceHost { Latency = TimeSpan.Zero };
        var client = new DiyClient(host, new ApiLog());
        var device = host.Create("1001sim0002");

        var refus = await Assert.ThrowsAsync<DiyException>(
            () => client.FlashAsync(device, new Uri("http://192.168.1.2:9000/f.bin"), new string('a', 64)));
        Assert.Equal(ErrorCode.FirmwareUnauthorized, refus.Code);

        await client.UnlockOtaAsync(device);
        await client.FlashAsync(device, new Uri("http://192.168.1.2:9000/f.bin"), new string('a', 64));
    }
}
