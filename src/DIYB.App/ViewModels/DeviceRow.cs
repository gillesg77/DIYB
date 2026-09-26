using System.Collections.ObjectModel;
using DIYB.Localization;
using DIYB.App.Mvvm;
using DIYB.Core.Devices;
using DIYB.Core.Firmware;
using DIYB.Core.Storage;

namespace DIYB.App.ViewModels;

/// <summary>Une ligne de la liste : l'appareil, ses métadonnées locales et les
/// verdicts calculés (conformité, mise à jour).</summary>
public sealed class DeviceRow : ObservableObject
{
    private const int SignalHistoryLength = 40;

    private DiyDevice _device;
    private DeviceEntry _entry;
    private FirmwareAdvice? _advice;
    private ComplianceState _compliance = ComplianceState.Unknown;
    private bool _isSelected;
    private bool _isBusy;

    public DeviceRow(DiyDevice device, DeviceEntry entry)
    {
        _device = device;
        _entry = entry;
        PushSignal(device.State?.Rssi);
    }

    public Localizer Loc => Localizer.Current;

    public DiyDevice Device => _device;

    public string DeviceId => _device.DeviceId;

    public string Address => _device.Address.ToString();

    public bool IsSimulated => _device.IsSimulated;

    public ObservableCollection<int> SignalHistory { get; } = new();

    public string Name
    {
        get => string.IsNullOrWhiteSpace(_entry.Name) ? _device.DeviceId : _entry.Name!;
        set => NameEdited?.Invoke(this, value);
    }

    /// <summary>Émis à la saisie ; c'est la vue-modèle principale qui persiste le nom.</summary>
    public event EventHandler<string>? NameEdited;

    public bool HasCustomName => !string.IsNullOrWhiteSpace(_entry.Name);

    public IReadOnlyList<string> Tags => _entry.Tags;

    public string TagsText => string.Join(", ", _entry.Tags);

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => Set(ref _isBusy, value);
    }

    /// <summary>Faux pour un module resté en mode cloud : son TXT est chiffré et son
    /// API locale refuse toute commande.</summary>
    public bool IsControllable => _device.State?.IsControllable ?? true;

    public bool RequiresKey => !IsControllable;

    public string ModeHint => IsControllable ? string.Empty : Loc["device.cloudModeHint"];

    public bool IsMultiChannel => _device.State?.IsMultiChannel ?? false;

    public int ChannelCount => _device.State?.Channels.Length ?? 0;

    public SwitchState Switch => _device.State?.Channels.FirstOrDefault()?.Switch ?? SwitchState.Unknown;

    /// <summary>Sur un appareil multi-canaux, l'état global n'est vrai que si tous les
    /// canaux sont fermés ; l'affichage mixte évite de laisser croire à un état unique.</summary>
    public string StateText
    {
        get
        {
            var channels = _device.State?.Channels ?? default;
            if (channels.IsDefaultOrEmpty)
                return Loc["state.unknown"];

            if (channels.All(c => c.Switch == SwitchState.On))
                return Loc["state.on"];

            if (channels.All(c => c.Switch == SwitchState.Off))
                return Loc["state.off"];

            return string.Join(" / ", channels.Select(c => Loc[c.Switch switch
            {
                SwitchState.On => "state.on",
                SwitchState.Off => "state.off",
                _ => "state.unknown",
            }]));
        }
    }

    public bool IsOn => _device.State?.Channels.Any(c => c.Switch == SwitchState.On) ?? false;

    public string StartupText => Uniform(c => c.Startup, startup => Loc[startup switch
    {
        StartupMode.On => "startup.on",
        StartupMode.Off => "startup.off",
        StartupMode.Keep => "startup.keep",
        _ => "startup.unknown",
    }]);

    private static readonly StartupMode[] StartupOrder = { StartupMode.On, StartupMode.Keep, StartupMode.Off };

    public IReadOnlyList<string> StartupOptions =>
        new[] { Loc["startup.on"], Loc["startup.keep"], Loc["startup.off"] };

    /// <summary>Index dans <see cref="StartupOptions"/>, ou -1 si les canaux diffèrent.</summary>
    public int StartupIndex
    {
        get
        {
            var channels = _device.State?.Channels ?? default;
            if (channels.IsDefaultOrEmpty)
                return -1;

            var modes = channels.Select(c => c.Startup).Distinct().ToArray();
            return modes.Length == 1 ? Array.IndexOf(StartupOrder, modes[0]) : -1;
        }

        set
        {
            if (value < 0 || value >= StartupOrder.Length || value == StartupIndex)
                return;

            StartupRequested?.Invoke(this, StartupOrder[value]);
        }
    }

    public event EventHandler<StartupMode>? StartupRequested;

    public string InchingText
    {
        get
        {
            var channels = _device.State?.Channels ?? default;
            if (channels.IsDefaultOrEmpty)
                return Loc["state.unknown"];

            return string.Join(" / ", channels.Select(c => c.PulseEnabled ? FormatWidth(c.PulseWidthMs) : Loc["state.off"]));
        }
    }

    public int? Rssi => _device.State?.Rssi;

    public string RssiText => Rssi is { } value ? $"{value} dBm" : Loc["state.unknown"];

    /// <summary>Quatre paliers usuels du Wi-Fi, pour colorer la cellule sans imposer
    /// une lecture du nombre.</summary>
    public int SignalBars => Rssi switch
    {
        null => 0,
        >= -55 => 4,
        >= -67 => 3,
        >= -75 => 2,
        _ => 1,
    };

    public string FirmwareText => _device.State?.FirmwareVersion ?? Loc["state.unknown"];

    public FirmwareAdvice? Advice
    {
        get => _advice;
        set
        {
            _advice = value;
            Raise(nameof(UpdateText));
            Raise(nameof(HasUpdate));
        }
    }

    public bool HasUpdate => _advice?.Status == UpdateStatus.UpdateAvailable;

    public string UpdateText => _advice?.Status switch
    {
        UpdateStatus.UpToDate => Loc["firmware.upToDate"],
        UpdateStatus.UpdateAvailable when _advice.FromFleetOnly => Loc["firmware.fleetOnly"],
        UpdateStatus.UpdateAvailable => Loc["firmware.updateAvailable"],
        UpdateStatus.Ahead => Loc["firmware.ahead"],
        _ => Loc["firmware.unknown"],
    };

    public ComplianceState Compliance
    {
        get => _compliance;
        set
        {
            if (!Set(ref _compliance, value))
                return;

            Raise(nameof(ComplianceText));
            Raise(nameof(IsDrifted));
        }
    }

    public bool IsDrifted => _compliance == ComplianceState.Drifted;

    public string ComplianceText => Loc[_compliance switch
    {
        ComplianceState.Compliant => "compliance.compliant",
        ComplianceState.Drifted => "compliance.drifted",
        _ => "compliance.unknown",
    }];

    public string LastSeenText => _device.LastSeen.ToLocalTime().ToString("HH:mm:ss");

    public void Update(DiyDevice device)
    {
        var previousRssi = _device.State?.Rssi;
        _device = device;

        if (device.State?.Rssi != previousRssi)
            PushSignal(device.State?.Rssi);

        RaiseAll();
    }

    public void Update(DeviceEntry entry)
    {
        _entry = entry;
        Raise(nameof(Name));
        Raise(nameof(HasCustomName));
        Raise(nameof(Tags));
        Raise(nameof(TagsText));
    }

    /// <summary>Rafraîchit tous les libellés, notamment après un changement de langue.</summary>
    public void RaiseAll()
    {
        Raise(nameof(Loc));
        Raise(nameof(Name));
        Raise(nameof(IsControllable));
        Raise(nameof(RequiresKey));
        Raise(nameof(ModeHint));
        Raise(nameof(Address));
        Raise(nameof(StateText));
        Raise(nameof(IsOn));
        Raise(nameof(Switch));
        Raise(nameof(StartupText));
        Raise(nameof(StartupOptions));
        Raise(nameof(StartupIndex));
        Raise(nameof(InchingText));
        Raise(nameof(Rssi));
        Raise(nameof(RssiText));
        Raise(nameof(SignalBars));
        Raise(nameof(FirmwareText));
        Raise(nameof(UpdateText));
        Raise(nameof(HasUpdate));
        Raise(nameof(ComplianceText));
        Raise(nameof(LastSeenText));
        Raise(nameof(IsMultiChannel));
        Raise(nameof(ChannelCount));
        Raise(nameof(TagsText));
    }

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || DeviceId.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Address.Contains(query, StringComparison.Ordinal)
            || _entry.Tags.Any(t => t.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void PushSignal(int? rssi)
    {
        if (rssi is not { } value)
            return;

        SignalHistory.Add(value);
        while (SignalHistory.Count > SignalHistoryLength)
            SignalHistory.RemoveAt(0);
    }

    private string Uniform<T>(Func<ChannelState, T> selector, Func<T, string> format)
    {
        var channels = _device.State?.Channels ?? default;
        if (channels.IsDefaultOrEmpty)
            return Loc["state.unknown"];

        var values = channels.Select(selector).Distinct().ToArray();
        return values.Length == 1 ? format(values[0]) : string.Join(" / ", channels.Select(c => format(selector(c))));
    }

    private static string FormatWidth(int milliseconds) =>
        milliseconds >= 1000 ? $"{milliseconds / 1000.0:0.#} s" : $"{milliseconds} ms";
}
