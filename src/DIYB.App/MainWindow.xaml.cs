using DIYB.Core;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DIYB.App;

/// <summary>Coquille de fenêtre. L'interface vit dans <see cref="MainPage"/> : un
/// <c>Window</c> n'est pas un <c>FrameworkElement</c>, donc les liaisons compilées à
/// convertisseur ne peuvent pas y résoudre les ressources.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = $"DIYB {AppVersion.Display}";

        // Matériau Mica : la fenêtre prend la teinte du bureau, sur laquelle les
        // cartes de contenu se détachent.
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop();

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "DIYB.ico");
        if (File.Exists(icon))
            AppWindow.SetIcon(icon);

        Page.Host = this;
        Closed += async (_, _) => await Page.ShutdownAsync();
    }
}
