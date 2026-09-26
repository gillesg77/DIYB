using System.Text;
using DIYB.App.Dialogs;
using DIYB.Localization;
using DIYB.App.ViewModels;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace DIYB.App;

public sealed partial class MainPage : UserControl
{
    private bool _suppressLanguageEvent;

    public MainPage()
    {
        ViewModel = new MainViewModel();
        InitializeComponent();

        Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Fenêtre hôte, nécessaire aux sélecteurs de fichiers qui réclament un
    /// HWND en déploiement non empaqueté.</summary>
    public Window? Host { get; set; }

    public async Task ShutdownAsync()
    {
        await ViewModel.SaveSettingsAsync();
        await ViewModel.Book.SaveAsync();
        ViewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        RefreshStaticLabels();
        Localizer.Current.LanguageChanged += (_, _) => RefreshStaticLabels();
    }

    /// <summary>Libellés hors liaisons compilées : éléments de menu et sélecteur de
    /// langue, reconstruits à chaque changement de langue.</summary>
    private void RefreshStaticLabels()
    {
        var loc = Localizer.Current;

        SnapshotItem.Text = loc["action.snapshot"];
        ExportItem.Text = loc["action.export"];
        ImportItem.Text = loc["action.import"];
        ShowCloudItem.Text = loc["filter.showCloud"];
        ShowCloudItem.IsChecked = ViewModel.ShowCloudDevices;
        AddSimulatedItem.Text = loc["simulator.add"];
        ClearSimulatedItem.Text = loc["simulator.clear"];

        // L'arabe, l'hébreu, le persan et l'ourdou inversent la disposition entière.
        FlowDirection = loc.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        _suppressLanguageEvent = true;
        LanguageBox.ItemsSource = loc.Languages;
        LanguageBox.SelectedItem = loc.Languages.FirstOrDefault(l => l.Code == loc.Language);
        _suppressLanguageEvent = false;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvent || LanguageBox.SelectedItem is not LanguageInfo language)
            return;

        Localizer.Current.Language = language.Code;
    }

    // ----- Actions de masse -----

    private async void OnSwitchOn(object sender, RoutedEventArgs e) => await ViewModel.SetSwitchAsync(true);

    private async void OnSwitchOff(object sender, RoutedEventArgs e) => await ViewModel.SetSwitchAsync(false);

    private async void OnStartupOn(object sender, RoutedEventArgs e) => await ViewModel.SetStartupAsync(StartupMode.On);

    private async void OnStartupKeep(object sender, RoutedEventArgs e) => await ViewModel.SetStartupAsync(StartupMode.Keep);

    private async void OnStartupOff(object sender, RoutedEventArgs e) => await ViewModel.SetStartupAsync(StartupMode.Off);

    private async void OnInching(object sender, RoutedEventArgs e)
    {
        var current = ViewModel.Selection.FirstOrDefault()?.Device.State?.Channels.FirstOrDefault();
        var result = await InchingDialog.ShowAsync(XamlRoot, current?.PulseEnabled ?? false, current?.PulseWidthMs ?? 500);
        if (result is null)
            return;

        await ViewModel.SetPulseAsync(result.Value.Enabled, result.Value.WidthMs);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await ViewModel.RefreshSelectionAsync();

    private void OnRescan(object sender, RoutedEventArgs e) => ViewModel.Rescan();

    private async void OnWifi(object sender, RoutedEventArgs e)
    {
        var credentials = await WifiDialog.ShowAsync(XamlRoot);
        if (credentials is null)
            return;

        foreach (var device in ViewModel.ActionTargets)
        {
            try
            {
                await ViewModel.Client.SetWifiAsync(device, credentials.Value.Ssid, credentials.Value.Password);
            }
            catch (DiyException error)
            {
                await MessageAsync(Localizer.Current["wifi.title"], Localizer.Current.Describe(error));
                return;
            }
        }
    }

    private async void OnFlash(object sender, RoutedEventArgs e)
    {
        var targets = ViewModel.Selection.Where(r => r.IsControllable).ToArray();
        if (targets.Length == 0)
            return;

        var path = await PickFileAsync(".bin");
        if (path is null)
            return;

        await OtaDialog.RunAsync(XamlRoot, ViewModel, targets, path);
    }

    private async void OnProfile(object sender, RoutedEventArgs e) => await ProfileDialog.ShowAsync(XamlRoot, ViewModel);

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        var failures = await ViewModel.CheckUpdatesAsync();
        if (failures.Count > 0)
            await MessageAsync(Localizer.Current["action.checkUpdates"], string.Join("\n", failures.Select(f => f.Message)));
    }

    private async void OnSnapshot(object sender, RoutedEventArgs e)
    {
        var devices = ViewModel.ActionTargets;
        foreach (var device in devices)
            await ViewModel.Snapshots.CaptureAsync(device);

        await MessageAsync(Localizer.Current["action.snapshot"], Localizer.Current.Format("result.success", devices.Count));
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var path = await PickSaveAsync("devices.json");
        if (path is not null)
            await ViewModel.Book.ExportAsync(path);
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(".json");
        if (path is null)
            return;

        var count = await ViewModel.Book.ImportAsync(path);
        await ViewModel.Book.SaveAsync();
        await MessageAsync(Localizer.Current["action.import"], Localizer.Current.Format("result.success", count));
    }

    private void OnToggleCloudDevices(object sender, RoutedEventArgs e) =>
        ViewModel.ShowCloudDevices = ShowCloudItem.IsChecked;

    private void OnAddSimulated(object sender, RoutedEventArgs e) => ViewModel.AddSimulatedDevice();

    private void OnClearSimulated(object sender, RoutedEventArgs e) => ViewModel.ClearSimulatedDevices();

    // ----- Sélection -----

    private void OnToggleAll(object sender, RoutedEventArgs e) => ViewModel.SelectAll(SelectAllBox.IsChecked == true);

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        SelectAllBox.IsChecked = true;
        ViewModel.SelectAll(true);
    }

    private void OnInvertSelection(object sender, RoutedEventArgs e) => ViewModel.InvertSelection();

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        SelectAllBox.IsChecked = false;
        ViewModel.SelectAll(false);
    }

    // ----- Actions par ligne -----

    private void OnNameCommitted(object sender, RoutedEventArgs e) => CommitName(sender);

    private void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        CommitName(sender);
        e.Handled = true;
    }

    private void CommitName(object sender)
    {
        if (sender is TextBox { DataContext: DeviceRow row } box)
            ViewModel.Rename(row, box.Text);
    }

    private async void OnRowToggle(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
            await ViewModel.SetSwitchAsync(row, !row.IsOn);
    }

    private async void OnRowInching(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;

        var channel = row.Device.State?.Channels.FirstOrDefault();
        var result = await InchingDialog.ShowAsync(XamlRoot, channel?.PulseEnabled ?? false, channel?.PulseWidthMs ?? 500);
        if (result is not null)
            await ViewModel.SetPulseAsync(row, result.Value.Enabled, result.Value.WidthMs);
    }

    private async void OnRowIdentify(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
            await ViewModel.IdentifyAsync(row);
    }

    private async void OnRowDetails(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
            await DetailsDialog.ShowAsync(XamlRoot, ViewModel, row);
    }

    private static DeviceRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as DeviceRow;

    // ----- Journal -----

    private void OnClearLog(object sender, RoutedEventArgs e) => ViewModel.ClearLog();

    private async void OnExportLog(object sender, RoutedEventArgs e)
    {
        var path = await PickSaveAsync("diyb-log.txt");
        if (path is null)
            return;

        var builder = new StringBuilder();
        foreach (var entry in ViewModel.LogEntries.Reverse())
        {
            builder.AppendLine($"{entry.Time}\t{entry.Level}\t{entry.DeviceId}\t{entry.Message}\t{entry.Detail}");
            if (entry.HasPayload)
                builder.AppendLine($"\t> {entry.Request}\n\t< {entry.Response}");
        }

        await File.WriteAllTextAsync(path, builder.ToString());
    }

    private async void OnLogSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView { SelectedItem: LogRow row } list || !row.HasPayload)
            return;

        list.SelectedItem = null;
        await MessageAsync(row.Message,
            $"{Localizer.Current["log.request"]}\n{row.Request}\n\n{Localizer.Current["log.response"]}\n{row.Response}");
    }

    // ----- Utilitaires -----

    private async Task MessageAsync(string title, string content)
    {
        var text = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        await DialogFactory.Create(XamlRoot, title, text).ShowAsync();
    }

    private async Task<string?> PickFileAsync(string extension)
    {
        if (Host is null)
            return null;

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(Host));
        picker.FileTypeFilter.Add(extension);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string?> PickSaveAsync(string suggestedName)
    {
        if (Host is null)
            return null;

        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(Host));

        var extension = Path.GetExtension(suggestedName);
        picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant(), new List<string> { extension });

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
