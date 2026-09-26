using DIYB.Localization;
using DIYB.App.ViewModels;
using DIYB.Core.Devices;
using DIYB.Core.Protocol;
using DIYB.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DIYB.App.Dialogs;

/// <summary>Édition du profil de référence et aperçu des écarts avant application.</summary>
public static class ProfileDialog
{
    public static async Task ShowAsync(XamlRoot root, MainViewModel viewModel)
    {
        var loc = Localizer.Current;
        var current = viewModel.ActiveProfile;

        var name = new TextBox { Header = loc["profile.name"], Text = current?.Name ?? "" };

        var startup = OptionBox(loc["profile.startup"], new[]
        {
            loc["profile.notGoverned"], loc["startup.on"], loc["startup.keep"], loc["startup.off"],
        }, current?.Startup switch
        {
            StartupMode.On => 1,
            StartupMode.Keep => 2,
            StartupMode.Off => 3,
            _ => 0,
        });

        var pulse = OptionBox(loc["profile.pulse"], new[]
        {
            loc["profile.notGoverned"], loc["state.on"], loc["state.off"],
        }, current?.PulseEnabled switch
        {
            true => 1,
            false => 2,
            _ => 0,
        });

        var width = new NumberBox
        {
            Header = loc["profile.pulseWidth"],
            Value = current?.PulseWidthMs ?? double.NaN,
            Minimum = DiyClient.MinPulseWidthMs,
            Maximum = DiyClient.MaxPulseWidthMs,
            SmallChange = DiyClient.PulseWidthStepMs,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };

        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12 };
        var previewButton = new Button { Content = loc["profile.dryRun"] };

        var panel = DialogFactory.Panel();
        panel.Children.Add(name);
        panel.Children.Add(startup);
        panel.Children.Add(pulse);
        panel.Children.Add(width);
        panel.Children.Add(previewButton);
        panel.Children.Add(preview);

        ConfigProfile Build() => new()
        {
            Name = string.IsNullOrWhiteSpace(name.Text) ? "profil" : name.Text.Trim(),
            Startup = startup.SelectedIndex switch
            {
                1 => StartupMode.On,
                2 => StartupMode.Keep,
                3 => StartupMode.Off,
                _ => null,
            },
            PulseEnabled = pulse.SelectedIndex switch
            {
                1 => true,
                2 => false,
                _ => null,
            },
            PulseWidthMs = double.IsNaN(width.Value) ? null : (int)width.Value,
        };

        previewButton.Click += (_, _) =>
        {
            var changes = viewModel.PreviewProfile(Build());
            if (changes.Count == 0)
            {
                preview.Text = loc["profile.noChange"];
                return;
            }

            var devices = changes.Select(c => c.DeviceId).Distinct().Count();
            var lines = changes.Take(20).Select(c =>
                $"{viewModel.Book.DisplayName(c.DeviceId)} · {c.Property} [{c.Outlet}] : {c.From} → {c.To}");

            preview.Text = loc.Format("profile.changeCount", changes.Count, devices)
                + Environment.NewLine
                + string.Join(Environment.NewLine, lines)
                + (changes.Count > 20 ? Environment.NewLine + "…" : string.Empty);
        };

        var dialog = DialogFactory.Create(root, loc["profile.title"], panel,
            primary: loc["profile.apply"], secondary: loc["action.save"]);
        dialog.CloseButtonText = loc["action.cancel"];
        dialog.DefaultButton = ContentDialogButton.Close;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return;

        var profile = Build();
        viewModel.Profiles.Save(profile);
        await viewModel.Profiles.PersistAsync();
        viewModel.ActiveProfile = profile;

        if (result == ContentDialogResult.Primary)
            await viewModel.ApplyProfileAsync(profile);
    }

    private static ComboBox OptionBox(string header, IReadOnlyList<string> options, int selected) => new()
    {
        Header = header,
        ItemsSource = options,
        SelectedIndex = selected,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
}
