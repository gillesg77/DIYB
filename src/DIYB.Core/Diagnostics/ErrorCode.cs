namespace DIYB.Core.Diagnostics;

/// <summary>Codes d'erreur destinés à l'utilisateur. Le coeur ne formate aucun
/// message : la traduction se fait dans la couche IHM.</summary>
public enum ErrorCode
{
    Unknown,

    Unreachable,
    Timeout,
    InvalidResponse,

    // Valeurs du champ « error » renvoyé par le firmware.
    FirmwareBadRequest,
    FirmwareUnauthorized,
    FirmwareDeviceMismatch,
    FirmwareNotFound,
    FirmwareBadParameter,

    PulseWidthOutOfRange,
    UnknownOutlet,
    OtaSignalTooWeak,
    OtaFileMissing,
    OtaChecksumMismatch,
    OtaNoLocalAddress,
    OtaDeviceDidNotReturn,
    WifiCredentialsInvalid,
}
