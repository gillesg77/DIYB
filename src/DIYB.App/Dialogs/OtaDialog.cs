using DIYB.Localization;
using DIYB.App.ViewModels;
using DIYB.Core.Diagnostics;
using DIYB.Core.Ota;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DIYB.App.Dialogs;

/// <summary>Flash séquentiel des appareils sélectionnés. Séquentiel et non parallèle :
/// plusieurs modules téléchargeant en même temps saturent le point d'accès, et un
/// transfert interrompu peut laisser un module inutilisable.</summary>
public static class OtaDialog
{
    public static async Task RunAsync(XamlRoot root, MainViewModel viewModel, IReadOnlyList<DeviceRow> targets, string firmwarePath)
    {
        var loc = Localizer.Current;
        var sha256 = await OtaFlasher.ComputeSha256Async(firmwarePath);

        var ignoreSignal = new CheckBox { Content = loc["ota.ignoreSignal"] };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0 };

        var panel = DialogFactory.Panel();
        panel.Spacing = 10;
        panel.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Message = loc["ota.warning"],
        });
        panel.Children.Add(DialogFactory.Field(loc["ota.file"], Path.GetFileName(firmwarePath)));
        panel.Children.Add(DialogFactory.Field(loc["ota.checksum"], sha256));
        panel.Children.Add(DialogFactory.Field(loc["devices.selected"], string.Join(", ", targets.Select(t => t.Name))));
        panel.Children.Add(new TextBlock { Text = loc["ota.snapshotFirst"], Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(ignoreSignal);
        panel.Children.Add(bar);
        panel.Children.Add(status);

        var dialog = DialogFactory.Create(root, loc["ota.title"], panel, primary: loc["ota.start"]);
        dialog.DefaultButton = ContentDialogButton.Close;

        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            // Le report empêche la fermeture du dialogue pendant le transfert.
            args.Cancel = true;
            var deferral = args.GetDeferral();
            sender.IsPrimaryButtonEnabled = false;

            try
            {
                var options = new OtaOptions { IgnoreSignalCheck = ignoreSignal.IsChecked == true };

                foreach (var row in targets)
                {
                    await viewModel.Snapshots.CaptureAsync(row.Device, "before-ota");

                    var progress = new Progress<OtaProgress>(p =>
                    {
                        bar.Value = p.Ratio;
                        status.Text = $"{row.Name} · {PhaseLabel(p.Phase)}";
                    });

                    try
                    {
                        await viewModel.Ota.FlashAsync(row.Device, firmwarePath, progress, options);
                    }
                    catch (DiyException error)
                    {
                        status.Text = $"{row.Name} · {PhaseLabel(OtaPhase.Failed)} — {Localizer.Current.Describe(error)}";
                        sender.IsPrimaryButtonEnabled = true;
                        return;
                    }
                }

                status.Text = PhaseLabel(OtaPhase.Completed);
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private static string PhaseLabel(OtaPhase phase) => Localizer.Current[phase switch
    {
        OtaPhase.Checking => "ota.phaseChecking",
        OtaPhase.Unlocking => "ota.phaseUnlocking",
        OtaPhase.Serving => "ota.phaseServing",
        OtaPhase.Downloading => "ota.phaseDownloading",
        OtaPhase.Rebooting => "ota.phaseRebooting",
        OtaPhase.Verifying => "ota.phaseVerifying",
        OtaPhase.Completed => "ota.phaseCompleted",
        _ => "ota.phaseFailed",
    }];
}
