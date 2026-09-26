using DIYB.App.ViewModels;
using DIYB.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DIYB.App.Dialogs;

public static class DetailsDialog
{
    public static async Task ShowAsync(XamlRoot root, MainViewModel viewModel, DeviceRow row)
    {
        var loc = Localizer.Current;
        var state = row.Device.State;

        var tags = new TextBox { Header = loc["details.tags"], Text = row.TagsText, PlaceholderText = "atelier, rdc" };
        var notes = new TextBox
        {
            Header = loc["details.notes"],
            Text = viewModel.Book.Get(row.DeviceId).Notes ?? string.Empty,
            AcceptsReturn = true,
            Height = 80,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = DialogFactory.Panel();
        panel.Spacing = 8;
        panel.Children.Add(DialogFactory.Field(loc["column.id"], row.DeviceId));
        panel.Children.Add(DialogFactory.Field(loc["column.address"], $"{row.Address}:{row.Device.Port}"));
        panel.Children.Add(DialogFactory.Field(loc["details.type"], state?.DeviceType ?? "—"));
        panel.Children.Add(DialogFactory.Field(loc["details.firmware"], row.FirmwareText));
        panel.Children.Add(DialogFactory.Field(loc["details.ssid"], state?.Ssid ?? "—"));
        panel.Children.Add(DialogFactory.Field(loc["details.mac"], state?.MacAddress ?? "—"));
        panel.Children.Add(DialogFactory.Field(loc["details.channels"], row.ChannelCount.ToString()));
        panel.Children.Add(DialogFactory.Field(loc["column.signal"], row.RssiText));
        panel.Children.Add(DialogFactory.Field(loc["details.lastSeen"], row.LastSeenText));
        panel.Children.Add(tags);
        panel.Children.Add(notes);

        var dialog = DialogFactory.Create(root, row.Name, panel, primary: loc["action.save"]);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        viewModel.Book.SetTags(row.DeviceId, tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries));
        viewModel.Book.Update(row.DeviceId, e => e with { Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text });
        await viewModel.Book.SaveAsync();
    }
}
