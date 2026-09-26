using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DIYB.Core.Ota;

/// <summary>Choix de l'adresse locale à publier dans l'URL de téléchargement.</summary>
public static class LocalAddressPicker
{
    /// <summary>Adresse locale située sur le même sous-réseau que l'appareil. Une
    /// machine raccordée au filaire et au Wi-Fi en expose plusieurs, dont une seule
    /// est joignable depuis le module.</summary>
    public static IPAddress? ForDevice(IPAddress deviceAddress)
    {
        if (deviceAddress.AddressFamily != AddressFamily.InterNetwork)
            return null;

        foreach (var (address, mask) in LocalIPv4Addresses())
        {
            if (SameSubnet(address, deviceAddress, mask))
                return address;
        }

        return null;
    }

    public static IEnumerable<(IPAddress Address, IPAddress Mask)> LocalIPv4Addresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var mask = unicast.IPv4Mask;
                if (mask is null || mask.Equals(IPAddress.Any))
                    continue;

                yield return (unicast.Address, mask);
            }
        }
    }

    public static bool SameSubnet(IPAddress left, IPAddress right, IPAddress mask)
    {
        var a = left.GetAddressBytes();
        var b = right.GetAddressBytes();
        var m = mask.GetAddressBytes();

        if (a.Length != b.Length || a.Length != m.Length)
            return false;

        for (var i = 0; i < a.Length; i++)
        {
            if ((a[i] & m[i]) != (b[i] & m[i]))
                return false;
        }

        return true;
    }
}
