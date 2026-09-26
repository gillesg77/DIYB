using DIYB.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DIYB.App.Dialogs;

public static class WifiDialog
{
    public static async Task<(string Ssid, string Password)?> ShowAsync(XamlRoot root)
    {
        var loc = Localizer.Current;

        var ssid = new TextBox { Header = loc["wifi.ssid"] };
        var password = new PasswordBox { Header = loc["wifi.password"] };

        var panel = DialogFactory.Panel();
        panel.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Message = loc["wifi.warning"],
        });
        panel.Children.Add(ssid);
        panel.Children.Add(password);

        var dialog = DialogFactory.Create(root, loc["wifi.title"], panel, primary: loc["action.apply"]);
        dialog.CloseButtonText = loc["action.cancel"];
        dialog.DefaultButton = ContentDialogButton.Close;

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? (ssid.Text, password.Password)
            : null;
    }
}
