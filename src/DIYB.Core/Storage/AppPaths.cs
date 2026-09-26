namespace DIYB.Core.Storage;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIYB");

    public static string DeviceBook => Path.Combine(Root, "devices.json");

    public static string Profiles => Path.Combine(Root, "profiles.json");

    public static string Settings => Path.Combine(Root, "settings.json");

    public static string Snapshots => Path.Combine(Root, "snapshots");

    /// <summary>Traductions additionnelles déposées à côté de l'exécutable.</summary>
    public static string Languages => Path.Combine(AppContext.BaseDirectory, "Strings");

    public static void EnsureRoot()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Snapshots);
    }
}
