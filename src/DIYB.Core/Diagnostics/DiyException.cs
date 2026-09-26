namespace DIYB.Core.Diagnostics;

/// <summary>Erreur remontée à l'utilisateur. <see cref="Exception.Message"/> reste en
/// anglais pour les journaux ; l'IHM affiche la traduction de <see cref="Code"/>.</summary>
public sealed class DiyException : Exception
{
    public DiyException(ErrorCode code, string debugMessage, IReadOnlyDictionary<string, object?>? args = null, Exception? inner = null)
        : base(debugMessage, inner)
    {
        Code = code;
        Args = args ?? new Dictionary<string, object?>();
    }

    public ErrorCode Code { get; }

    /// <summary>Paramètres à injecter dans le message traduit.</summary>
    public IReadOnlyDictionary<string, object?> Args { get; }

    public static DiyException FromFirmware(int error, string deviceId)
    {
        var code = error switch
        {
            400 => ErrorCode.FirmwareBadRequest,
            401 => ErrorCode.FirmwareUnauthorized,
            403 => ErrorCode.FirmwareDeviceMismatch,
            404 => ErrorCode.FirmwareNotFound,
            422 => ErrorCode.FirmwareBadParameter,
            _ => ErrorCode.Unknown,
        };

        return new DiyException(code, $"Device {deviceId} returned error {error}.", new Dictionary<string, object?>
        {
            ["deviceId"] = deviceId,
            ["error"] = error,
        });
    }
}
