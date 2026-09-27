using System.Runtime.InteropServices;
using DIYB.App.ViewModels;
using DIYB.Core;
using DIYB.Core.Storage;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DIYB.App;

/// <summary>Coquille de fenêtre. L'interface vit dans <see cref="MainPage"/> : un
/// <c>Window</c> n'est pas un <c>FrameworkElement</c>, donc les liaisons compilées à
/// convertisseur ne peuvent pas y résoudre les ressources.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Taille au premier lancement, en unités logiques : la grille des
    /// appareils réclame près de 1240 pixels avant de défiler horizontalement.</summary>
    private const int DefaultWidth = 1400;
    private const int DefaultHeight = 900;

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

        RestorePlacement();

        Page.Host = this;
        Closed += async (_, _) =>
        {
            Page.ViewModel.Placement = CapturePlacement();
            await Page.ShutdownAsync();
        };
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Applique la géométrie retenue, ou une taille par défaut. Lue en
    /// synchrone : passer par le chargement asynchrone des réglages ferait sauter la
    /// fenêtre sous les yeux de l'utilisateur.</summary>
    private void RestorePlacement()
    {
        var saved = JsonStore.Load<AppSettings>(AppPaths.Settings)?.Window;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        if (saved is { Width: > 200, Height: > 200 } && IsOnScreen(saved, area))
        {
            AppWindow.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));

            if (saved.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();

            return;
        }

        // Le défaut est exprimé en unités logiques : sur un écran à 150 %, une
        // taille en pixels bruts donnerait une fenêtre minuscule.
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0)
            scale = 1;

        var width = Math.Min((int)(DefaultWidth * scale), area.Width - 40);
        var height = Math.Min((int)(DefaultHeight * scale), area.Height - 40);

        AppWindow.MoveAndResize(new RectInt32(
            area.X + ((area.Width - width) / 2),
            area.Y + ((area.Height - height) / 2),
            width,
            height));
    }

    /// <summary>Une fenêtre restaurée hors de tout écran serait injoignable : le cas
    /// se produit dès qu'on débranche un moniteur.</summary>
    private static bool IsOnScreen(WindowPlacement placement, RectInt32 area) =>
        placement.X < area.X + area.Width
        && placement.Y < area.Y + area.Height
        && placement.X + placement.Width > area.X
        && placement.Y + placement.Height > area.Y;

    private WindowPlacement CapturePlacement()
    {
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

        // Maximisée, la taille courante est celle de l'écran : on conserve alors la
        // géométrie précédente, pour retrouver ses dimensions en restaurant.
        if (maximized && JsonStore.Load<AppSettings>(AppPaths.Settings)?.Window is { } previous)
            return previous with { Maximized = true };

        return new WindowPlacement
        {
            X = AppWindow.Position.X,
            Y = AppWindow.Position.Y,
            Width = AppWindow.Size.Width,
            Height = AppWindow.Size.Height,
            Maximized = maximized,
        };
    }
}
