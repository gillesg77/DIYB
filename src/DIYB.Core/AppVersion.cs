using System.Reflection;

namespace DIYB.Core;

/// <summary>Version de l'application, lue dans l'assembly d'entrée plutôt que
/// recopiée : un numéro écrit en dur finit toujours par mentir.</summary>
public static class AppVersion
{
    private static readonly Lazy<string> Value = new(Resolve);

    /// <summary>Forme courte, « 0.1.0 ».</summary>
    public static string Display => Value.Value;

    private static string Resolve()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Le suffixe « +abcdef » ajouté par SourceLink n'intéresse personne.
            var separator = informational.IndexOf('+');
            return separator < 0 ? informational : informational[..separator];
        }

        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
