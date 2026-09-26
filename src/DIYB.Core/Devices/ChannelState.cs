namespace DIYB.Core.Devices;

/// <summary>État d'un canal. Un appareil mono-canal en expose un seul, d'indice 0.</summary>
public sealed record ChannelState
{
    public required int Outlet { get; init; }

    public SwitchState Switch { get; init; } = SwitchState.Unknown;

    public StartupMode Startup { get; init; } = StartupMode.Unknown;

    /// <summary>Mode impulsionnel : le relais retombe seul après <see cref="PulseWidthMs"/>.</summary>
    public bool PulseEnabled { get; init; }

    /// <summary>Multiple de 500 ms, de 500 ms à 10 heures.</summary>
    public int PulseWidthMs { get; init; } = 500;

    public static ChannelState Empty(int outlet) => new() { Outlet = outlet };
}
