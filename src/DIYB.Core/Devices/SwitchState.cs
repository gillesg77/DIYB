namespace DIYB.Core.Devices;

public enum SwitchState
{
    Unknown,
    Off,
    On,
}

/// <summary>Comportement du relais à la mise sous tension. Le firmware attend
/// « on », « off » ou « stay ».</summary>
public enum StartupMode
{
    Unknown,
    Off,
    On,
    Keep,
}

public static class ProtocolEnums
{
    public static string ToWire(this SwitchState state) => state switch
    {
        SwitchState.On => "on",
        SwitchState.Off => "off",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static SwitchState ToSwitchState(string? wire) => wire?.ToLowerInvariant() switch
    {
        "on" => SwitchState.On,
        "off" => SwitchState.Off,
        _ => SwitchState.Unknown,
    };

    public static string ToWire(this StartupMode mode) => mode switch
    {
        StartupMode.On => "on",
        StartupMode.Off => "off",
        StartupMode.Keep => "stay",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static StartupMode ToStartupMode(string? wire) => wire?.ToLowerInvariant() switch
    {
        "on" => StartupMode.On,
        "off" => StartupMode.Off,
        "stay" => StartupMode.Keep,
        _ => StartupMode.Unknown,
    };
}
