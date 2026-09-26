using System.Globalization;

namespace DIYB.Cli;

/// <summary>Découpage de la ligne de commande. Les options acceptent les deux formes
/// <c>--nom valeur</c> et <c>--nom=valeur</c> ; une option seule vaut vrai.</summary>
internal sealed class CliArguments
{
    private readonly Dictionary<string, string?> _options;

    private CliArguments(string command, IReadOnlyList<string> operands, Dictionary<string, string?> options)
    {
        Command = command;
        Operands = operands;
        _options = options;
    }

    /// <summary>Premier opérande, chaîne vide si la ligne n'en contient aucun.</summary>
    public string Command { get; }

    /// <summary>Opérandes suivant la commande, dans l'ordre.</summary>
    public IReadOnlyList<string> Operands { get; }

    public IReadOnlyCollection<string> OptionNames => _options.Keys;

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        var operands = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];

            if (token is "-h" or "-?" or "/?")
            {
                options["help"] = null;
                continue;
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                operands.Add(token);
                continue;
            }

            var name = token[2..];
            var separator = name.IndexOf('=');

            if (separator >= 0)
            {
                options[name[..separator]] = name[(separator + 1)..];
                continue;
            }

            // Une valeur ne peut pas commencer par « -- » : sinon l'option est booléenne.
            var next = i + 1 < args.Count ? args[i + 1] : null;
            if (next is not null && !next.StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = next;
                i++;
            }
            else
            {
                options[name] = null;
            }
        }

        var command = operands.Count > 0 ? operands[0] : string.Empty;
        var rest = operands.Count > 0 ? operands.Skip(1).ToArray() : Array.Empty<string>();

        return new CliArguments(command, rest, options);
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name, string? fallback = null) =>
        _options.TryGetValue(name, out var value) && value is not null ? value : fallback;

    public int Int(string name, int fallback) =>
        _options.TryGetValue(name, out var value)
        && value is not null
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    /// <summary>Vrai si l'option est présente, sauf valeur explicitement négative.</summary>
    public bool Flag(string name) =>
        _options.TryGetValue(name, out var value)
        && value is not ("false" or "0" or "no" or "non");
}
