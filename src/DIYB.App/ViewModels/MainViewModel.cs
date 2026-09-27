using System.Collections.ObjectModel;
using DIYB.Localization;
using DIYB.App.Mvvm;
using DIYB.Core;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;
using DIYB.Core.Discovery;
using DIYB.Core.Firmware;
using DIYB.Core.Fleet;
using DIYB.Core.Mdns;
using DIYB.Core.Ota;
using DIYB.Core.Protocol;
using DIYB.Core.Simulation;
using DIYB.Core.Storage;
using Microsoft.UI.Dispatching;

namespace DIYB.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, DeviceRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    private readonly MdnsClient _mdns = new();
    private readonly HttpDiyTransport _network = new();
    private readonly SimulatedDeviceHost _simulator = new();

    private string _searchText = string.Empty;
    private string _status = string.Empty;
    private bool _isBusy;
    private ConfigProfile? _activeProfile;
    private int _simulatorCount;
    private bool _showCloudDevices;

    public MainViewModel()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Devices.CollectionChanged += (_, _) =>
        {
            Raise(nameof(IsEmpty));
            Raise(nameof(EmptyText));
        };

        Log = new ApiLog();
        Registry = new DeviceRegistry(_mdns, Log);
        Client = new DiyClient(new RoutingTransport(_network, _simulator), Log);
        Book = new DeviceBook();
        Profiles = new ProfileStore();
        Snapshots = new SnapshotStore();
        Fleet = new FleetOperations(Client, Registry);
        Ota = new OtaFlasher(Client, Log, Registry);
        Advisor = new FirmwareAdvisor(Array.Empty<IFirmwareCatalog>());

        Registry.DeviceAdded += (_, device) => OnUi(() => AddOrUpdate(device));
        Registry.DeviceUpdated += (_, device) => OnUi(() => AddOrUpdate(device));
        Registry.DeviceRemoved += (_, id) => OnUi(() => RemoveRow(id));
        Log.EntryAdded += (_, entry) => OnUi(() => AppendLog(entry));
        Localizer.Current.LanguageChanged += (_, _) => OnUi(RefreshLabels);

        _simulator.StateChanged += (_, deviceId) => OnUi(() => RefreshSimulated(deviceId));
    }

    public Localizer Loc => Localizer.Current;

    public string Version => AppVersion.Display;

    public ApiLog Log { get; }

    public DeviceRegistry Registry { get; }

    public DiyClient Client { get; }

    public DeviceBook Book { get; }

    public ProfileStore Profiles { get; }

    public SnapshotStore Snapshots { get; }

    public FleetOperations Fleet { get; }

    public OtaFlasher Ota { get; }

    public FirmwareAdvisor Advisor { get; private set; }

    public ObservableCollection<DeviceRow> Devices { get; } = new();

    public ObservableCollection<LogRow> LogEntries { get; } = new();

    public bool IsEmpty => Devices.Count == 0;

    /// <summary>Texte de l'état vide. Un pare-feu qui bloque et un parc absent
    /// donnent la même liste vide ; seule la présence de trafic mDNS les sépare.</summary>
    public string EmptyText
    {
        get
        {
            if (!Registry.InboundBlocked)
                return Loc["devices.empty"];

            var message = Loc["devices.emptyFirewall"];
            return Registry.Firewall.RuleName is { } nom ? $"{message} (« {nom} »)" : message;
        }
    }

    /// <summary>Les modules restés en mode cloud s'annoncent sur le même service mDNS
    /// que les modules DIY. Ils sont masqués par défaut : rien n'est pilotable dessus
    /// et leur présence brouille la liste.</summary>
    public bool ShowCloudDevices
    {
        get => _showCloudDevices;
        set
        {
            if (Set(ref _showCloudDevices, value))
                ApplyFilter();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
                ApplyFilter();
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public ConfigProfile? ActiveProfile
    {
        get => _activeProfile;
        set
        {
            if (Set(ref _activeProfile, value))
                RefreshCompliance();
        }
    }

    public IReadOnlyList<DeviceRow> Selection =>
        Devices.Where(d => d.IsSelected).ToArray() is { Length: > 0 } selected ? selected : Devices.ToArray();

    public IReadOnlyList<DiyDevice> SelectedDevices => Selection.Select(r => r.Device).ToArray();

    /// <summary>Sélection restreinte aux modules en mode DIY. Un module resté en mode
    /// cloud ferait expirer chaque requête au bout de trois secondes.</summary>
    public IReadOnlyList<DiyDevice> ActionTargets =>
        Selection.Where(r => r.IsControllable).Select(r => r.Device).ToArray();

    public int SkippedCount => Selection.Count(r => !r.IsControllable);

    public async Task InitializeAsync()
    {
        AppPaths.EnsureRoot();

        var settings = await JsonStore.LoadAsync<AppSettings>(AppPaths.Settings).ConfigureAwait(true)
            ?? new AppSettings();

        Localizer.Current.Load(AppPaths.Languages, settings.Language);

        await Book.LoadAsync().ConfigureAwait(true);
        await Profiles.LoadAsync().ConfigureAwait(true);

        Advisor = new FirmwareAdvisor(BuildCatalogs(settings));
        ActiveProfile = settings.ActiveProfile is null ? null : Profiles.Find(settings.ActiveProfile);
        ShowCloudDevices = settings.ShowCloudDevices;

        Book.EntryChanged += (_, entry) => OnUi(() =>
        {
            if (_rows.TryGetValue(entry.DeviceId, out var row))
                row.Update(entry);
        });

        Registry.Start();
        Status = Loc["devices.scanning"];
    }

    public Task SaveSettingsAsync() => JsonStore.SaveAsync(AppPaths.Settings, new AppSettings
    {
        Language = Localizer.Current.Language,
        ActiveProfile = ActiveProfile?.Name,
        ShowCloudDevices = ShowCloudDevices,
    });

    public void Rename(DeviceRow row, string name)
    {
        Book.SetName(row.DeviceId, name);
        _ = Book.SaveAsync();
    }

    public void SelectAll(bool selected)
    {
        foreach (var row in Devices)
            row.IsSelected = selected;
    }

    public void InvertSelection()
    {
        foreach (var row in Devices)
            row.IsSelected = !row.IsSelected;
    }

    public Task<IReadOnlyList<OperationResult>> SetSwitchAsync(bool on) =>
        RunAsync(progress => Fleet.SetSwitchAsync(ActionTargets, on, progress));

    public Task<IReadOnlyList<OperationResult>> SetStartupAsync(StartupMode mode) =>
        RunAsync(progress => Fleet.SetStartupAsync(ActionTargets, mode, progress));

    public Task<IReadOnlyList<OperationResult>> SetPulseAsync(bool enabled, int widthMs) =>
        RunAsync(progress => Fleet.SetPulseAsync(ActionTargets, enabled, widthMs, progress));

    public Task<IReadOnlyList<OperationResult>> RefreshSelectionAsync() =>
        RunAsync(progress => Fleet.RefreshAsync(ActionTargets, progress));

    public Task<IReadOnlyList<OperationResult>> ApplyProfileAsync(ConfigProfile profile) =>
        RunAsync(progress => Fleet.ApplyProfileAsync(ActionTargets, profile, progress));

    public IReadOnlyList<PlannedChange> PreviewProfile(ConfigProfile profile) =>
        Fleet.PlanProfile(ActionTargets, profile);

    public void Rescan()
    {
        Registry.Refresh();
        Status = Loc["devices.scanning"];
        Raise(nameof(EmptyText));
    }

    /// <summary>Actions ciblant une seule ligne, déclenchées depuis la liste.</summary>
    public Task SetSwitchAsync(DeviceRow row, bool on) =>
        RunOneAsync(row, token => Client.SetSwitchAsync(row.Device, null, on, token));

    public Task SetStartupAsync(DeviceRow row, StartupMode mode) =>
        RunOneAsync(row, token => Client.SetStartupAsync(row.Device, null, mode, token));

    public Task SetPulseAsync(DeviceRow row, bool enabled, int widthMs) =>
        RunOneAsync(row, token => Client.SetPulseAsync(row.Device, null, enabled, widthMs, token));

    private async Task RunOneAsync(DeviceRow row, Func<CancellationToken, Task> action)
    {
        row.IsBusy = true;
        try
        {
            await action(CancellationToken.None).ConfigureAwait(true);

            var state = await Client.GetInfoAsync(row.Device).ConfigureAwait(true);
            Registry.ApplyState(row.DeviceId, state);
        }
        catch (DiyException e)
        {
            Status = Loc.Describe(e);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    public async Task IdentifyAsync(DeviceRow row)
    {
        row.IsBusy = true;
        try
        {
            await Fleet.IdentifyAsync(row.Device).ConfigureAwait(true);
        }
        catch (DiyException e)
        {
            Status = Loc.Describe(e);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    public async Task<IReadOnlyList<Exception>> CheckUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            var failures = await Advisor.RefreshAsync().ConfigureAwait(true);
            RefreshAdvice();
            return failures;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void AddSimulatedDevice()
    {
        // Un appareil sur trois est multi-canaux, de quoi exercer les deux chemins.
        _simulatorCount++;
        var channels = _simulatorCount % 3 == 0 ? 4 : 1;
        var id = $"1001sim{_simulatorCount:0000}";

        var device = _simulator.Create(id, channels, channels > 1 ? "strip" : "diy_plug");
        Registry.Upsert(device);
    }

    public void ClearSimulatedDevices()
    {
        foreach (var row in Devices.Where(r => r.IsSimulated).ToArray())
            Registry.Remove(row.DeviceId);
    }

    public void ClearLog()
    {
        Log.Clear();
        LogEntries.Clear();
    }

    private static IReadOnlyList<IFirmwareCatalog> BuildCatalogs(AppSettings settings)
    {
        var catalogs = new List<IFirmwareCatalog>();
        foreach (var location in settings.FirmwareCatalogs)
            catalogs.Add(new JsonFirmwareCatalog(location));

        return catalogs;
    }

    private async Task<IReadOnlyList<OperationResult>> RunAsync(Func<IProgress<FleetProgress>, Task<IReadOnlyList<OperationResult>>> action)
    {
        IsBusy = true;
        var skipped = SkippedCount;
        var progress = new Progress<FleetProgress>(p => Status = $"{p.Completed}/{p.Total}");

        try
        {
            var results = await action(progress).ConfigureAwait(true);
            var failures = results.Count(r => !r.Success);

            var parts = new List<string> { Loc.Format("result.success", results.Count - failures) };
            if (failures > 0)
                parts.Add(Loc.Format("result.failure", failures));

            if (skipped > 0)
                parts.Add(Loc.Format("devices.skipped", skipped));

            Status = string.Join(" · ", parts);
            return results;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddOrUpdate(DiyDevice device)
    {
        if (_rows.TryGetValue(device.DeviceId, out var row))
        {
            row.Update(device);
        }
        else
        {
            row = new DeviceRow(device, Book.Get(device.DeviceId));
            row.NameEdited += (sender, name) => Rename((DeviceRow)sender!, name);
            row.StartupRequested += (sender, mode) => _ = SetStartupAsync((DeviceRow)sender!, mode);
            _rows[device.DeviceId] = row;

            if (IsVisible(row))
                InsertSorted(row);
        }

        UpdateVerdicts(row);
        UpdateCount();
    }

    private void RemoveRow(string deviceId)
    {
        if (!_rows.Remove(deviceId, out var row))
            return;

        Devices.Remove(row);
        UpdateCount();
    }

    private void RefreshSimulated(string deviceId)
    {
        var device = Registry.Find(deviceId);
        if (device is null)
            return;

        // Le simulateur ne diffuse pas d'annonce mDNS : l'état est relu explicitement.
        _ = RefreshOneAsync(device);
    }

    private async Task RefreshOneAsync(DiyDevice device)
    {
        try
        {
            var state = await Client.GetInfoAsync(device).ConfigureAwait(true);
            Registry.ApplyState(device.DeviceId, state);
        }
        catch (DiyException)
        {
        }
    }

    private void InsertSorted(DeviceRow row)
    {
        var index = 0;
        while (index < Devices.Count
            && string.Compare(Devices[index].Name, row.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
        {
            index++;
        }

        Devices.Insert(index, row);
    }

    private void ApplyFilter()
    {
        Devices.Clear();
        foreach (var row in _rows.Values.Where(IsVisible).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
            Devices.Add(row);

        UpdateCount();
    }

    private void UpdateVerdicts(DeviceRow row)
    {
        if (ActiveProfile is { } profile)
            row.Compliance = profile.Evaluate(row.Device);

        var advice = Advisor.Advise(new[] { row.Device }).FirstOrDefault();
        if (advice is not null)
            row.Advice = advice;
    }

    private void RefreshCompliance()
    {
        foreach (var row in _rows.Values)
            row.Compliance = ActiveProfile?.Evaluate(row.Device) ?? ComplianceState.Unknown;
    }

    private void RefreshAdvice()
    {
        var advice = Advisor.Advise(_rows.Values.Select(r => r.Device)).ToDictionary(a => a.DeviceId, StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows.Values)
        {
            if (advice.TryGetValue(row.DeviceId, out var entry))
                row.Advice = entry;
        }
    }

    private void RefreshLabels()
    {
        foreach (var row in _rows.Values)
            row.RaiseAll();

        UpdateCount();
        Raise(nameof(Loc));
        Raise(nameof(EmptyText));
    }

    private void AppendLog(LogEntry entry)
    {
        LogEntries.Insert(0, new LogRow(entry));

        while (LogEntries.Count > 500)
            LogEntries.RemoveAt(LogEntries.Count - 1);
    }

    private bool IsVisible(DeviceRow row) =>
        (ShowCloudDevices || row.IsControllable) && row.Matches(SearchText);

    private void UpdateCount()
    {
        var hidden = _rows.Values.Count(r => !r.IsControllable && !ShowCloudDevices);
        Status = hidden == 0
            ? Loc.Format("devices.count", Devices.Count)
            : $"{Loc.Format("devices.count", Devices.Count)} · {Loc.Format("devices.hidden", hidden)}";
    }

    private void OnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }

    public void Dispose()
    {
        Registry.Dispose();
        _mdns.Dispose();
        _network.Dispose();
    }
}

public sealed record AppSettings
{
    public string? Language { get; init; }

    public string? ActiveProfile { get; init; }

    public bool ShowCloudDevices { get; init; }

    /// <summary>Catalogues de firmware interrogés au démarrage : chemins locaux ou URL.</summary>
    public IReadOnlyList<string> FirmwareCatalogs { get; init; } = Array.Empty<string>();
}
