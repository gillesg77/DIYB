using DIYB.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DIYB.App.Dialogs;

/// <summary>Construction commune des dialogues : largeur, défilement et boutons.</summary>
internal static class DialogFactory
{
    private const double MaxWidth = 580;

    public static StackPanel Panel() => new()
    {
        Spacing = 12,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public static ContentDialog Create(
        XamlRoot root,
        string title,
        UIElement content,
        string? primary = null,
        string? secondary = null,
        double maxHeight = 460)
    {
        var scroller = new ScrollViewer
        {
            MaxHeight = maxHeight,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // Gouttière pour la barre de défilement : sans elle, elle recouvre le bord
            // droit du contenu.
            Padding = new Thickness(0, 0, 14, 0),
            Content = content,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = scroller,
            CloseButtonText = Localizer.Current["action.close"],
            DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };

        if (primary is not null)
            dialog.PrimaryButtonText = primary;

        if (secondary is not null)
            dialog.SecondaryButtonText = secondary;

        // Les dialogues par défaut sont trop étroits pour un formulaire à libellés.
        dialog.Resources["ContentDialogMaxWidth"] = MaxWidth;
        dialog.Resources["ContentDialogMinWidth"] = 420d;

        return dialog;
    }

    /// <summary>Paire libellé / valeur des fiches de détail.</summary>
    public static StackPanel Field(string label, string value)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        panel.Children.Add(new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });

        return panel;
    }
}
