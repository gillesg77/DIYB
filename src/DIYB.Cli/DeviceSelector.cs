using DIYB.Core.Devices;
using DIYB.Core.Storage;

namespace DIYB.Cli;

/// <summary>Traduit les opérandes et options de ciblage en liste d'appareils.</summary>
internal static class DeviceSelector
{
    public static IReadOnlyList<DiyDevice> Resolve(
        IReadOnlyCollection<DiyDevice> discovered,
        DeviceBook book,
        IReadOnlyList<string> operands,
        CliArguments arguments)
    {
        var candidates = arguments.Flag("all-modes")
            ? discovered
            : discovered.Where(d => d.State?.IsControllable ?? true).ToArray();

        if (arguments.Value("tag") is { } tag)
        {
            return candidates
                .Where(d => book.Get(d.DeviceId).Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }

        if (arguments.Flag("all") || operands.Count == 0)
            return candidates.ToArray();

        // Un opérande désigne un identifiant, un nom donné localement ou une adresse.
        return candidates
            .Where(d => operands.Any(o => Matches(d, book, o)))
            .ToArray();
    }

    private static bool Matches(DiyDevice device, DeviceBook book, string token) =>
        string.Equals(device.DeviceId, token, StringComparison.OrdinalIgnoreCase)
        || string.Equals(book.Get(device.DeviceId).Name, token, StringComparison.CurrentCultureIgnoreCase)
        || string.Equals(device.Address.ToString(), token, StringComparison.Ordinal);
}
