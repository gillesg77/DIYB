using DIYB.Localization;
using DIYB.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DIYB.App.Dialogs;

public static class InchingDialog
{
    public static async Task<(bool Enabled, int WidthMs)?> ShowAsync(XamlRoot root, bool enabled, int widthMs)
    {
        var loc = Localizer.Current;

        var toggle = new ToggleSwitch { IsOn = enabled, Header = loc["inching.enabled"] };

        var width = new NumberBox
        {
            Header = loc["inching.width"],
            Value = widthMs,
            Minimum = DiyClient.MinPulseWidthMs,
            Maximum = DiyClient.MaxPulseWidthMs,
            SmallChange = DiyClient.PulseWidthStepMs,
            LargeChange = DiyClient.PulseWidthStepMs * 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };

        var panel = DialogFactory.Panel();
        panel.Children.Add(toggle);
        panel.Children.Add(width);
        panel.Children.Add(new TextBlock
        {
            Text = loc["inching.widthHint"],
            Opacity = 0.7,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = DialogFactory.Create(root, loc["inching.title"], panel, primary: loc["action.apply"]);
        dialog.CloseButtonText = loc["action.cancel"];

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        // Le firmware n'accepte qu'un multiple du pas : la valeur saisie est arrondie
        // plutôt que rejetée.
        var rounded = (int)Math.Round(width.Value / DiyClient.PulseWidthStepMs) * DiyClient.PulseWidthStepMs;
        rounded = Math.Clamp(rounded, DiyClient.MinPulseWidthMs, DiyClient.MaxPulseWidthMs);

        return (toggle.IsOn, rounded);
    }
}
