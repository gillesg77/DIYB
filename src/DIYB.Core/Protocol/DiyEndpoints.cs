namespace DIYB.Core.Protocol;

/// <summary>Points d'entrée REST du mode DIY eWeLink, tous en POST sur le port 8081.</summary>
public static class DiyEndpoints
{
    public const string Info = "/zeroconf/info";
    public const string Switch = "/zeroconf/switch";
    public const string Switches = "/zeroconf/switches";
    public const string Startup = "/zeroconf/startup";
    public const string Pulse = "/zeroconf/pulse";
    public const string Pulses = "/zeroconf/pulses";
    public const string Wifi = "/zeroconf/wifi";
    public const string SignalStrength = "/zeroconf/signal_strength";
    public const string OtaUnlock = "/zeroconf/ota_unlock";
    public const string OtaFlash = "/zeroconf/ota_flash";
}
