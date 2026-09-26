using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace DIYB.App;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Collapsed;
}

/// <summary>Colore l'état du relais : la couleur porte l'information plus vite que
/// le libellé sur une liste longue. Le paramètre <c>fill</c> donne la teinte de fond
/// de la pastille, sinon c'est la couleur du texte.</summary>
public sealed class StateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var fill = parameter as string == "fill";
        var key = (value is true, fill) switch
        {
            (true, false) => "StateOnBrush",
            (true, true) => "StateOnFillBrush",
            (false, false) => "StateOffBrush",
            (false, true) => "StateOffFillBrush",
        };

        return Application.Current.Resources.TryGetValue(key, out var brush)
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
