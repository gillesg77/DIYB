using DIYB.Cli;
using DIYB.Core;
using DIYB.Core.Devices;
using DIYB.Core.Diagnostics;
using DIYB.Core.Discovery;
using DIYB.Core.Fleet;
using DIYB.Core.Mdns;
using DIYB.Core.Ota;
using DIYB.Core.Protocol;
using DIYB.Core.Storage;
using DIYB.Localization;

return await CliHost.RunAsync(args);

namespace DIYB.Cli
{
    internal static class CliHost
    {
        private const int ExitOk = 0;
        private const int ExitFailure = 1;
        private const int ExitUsage = 2;
        private const int ExitNoDevice = 3;

        public static async Task<int> RunAsync(string[] rawArguments)
        {
            var arguments = CliArguments.Parse(rawArguments);

            if (arguments.Has("version") || arguments.Command is "version")
            {
                Console.WriteLine($"diyb-cli {AppVersion.Display}");
                return ExitOk;
            }

            if (arguments.Has("help") || arguments.Command is "" or "help")
            {
                PrintUsage();
                return arguments.Command is "" && !arguments.Has("help") ? ExitUsage : ExitOk;
            }

            // Sans quoi la console Windows rend les accents en points d'interrogation.
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            AppPaths.EnsureRoot();
            Localizer.Current.Load(AppPaths.Languages, arguments.Value("lang"));

            var book = new DeviceBook();
            await book.LoadAsync().ConfigureAwait(false);

            var log = new ApiLog();
            using var mdns = new MdnsClient();
            using var registry = new DeviceRegistry(mdns, log);
            using var transport = new HttpDiyTransport();
            var client = new DiyClient(transport, log);
            var fleet = new FleetOperations(client, registry);

            registry.Start();
            await WaitForDiscoveryAsync(registry, Math.Clamp(arguments.Int("wait", 12), 1, 120)).ConfigureAwait(false);

            var targets = DeviceSelector.Resolve(registry.Devices, book, arguments.Operands, arguments);
            var json = arguments.Flag("json");

            try
            {
                // Diagnostic avant tout : sans un seul datagramme reçu, l'absence
                // d'appareils s'explique par le pare-feu, pas par le parc.
                if (registry.InboundBlocked && !json)
                    Console.Error.WriteLine(FirewallMessage(registry.Firewall));

                var code = await DispatchAsync(arguments, targets, registry, book, client, fleet, json).ConfigureAwait(false);

                if (arguments.Flag("verbose"))
                {
                    Console.Error.WriteLine(
                        $"mDNS: {registry.PacketsReceived} datagramme(s) reçu(s), {registry.Devices.Count} appareil(s) assemblé(s), pare-feu: {registry.Firewall.Verdict}"
                        + (registry.Firewall.RuleName is { } nom ? $" (« {nom} »)" : string.Empty));
                    DumpLog(log);
                }

                return code;
            }
            catch (DiyException error)
            {
                Console.Error.WriteLine(Localizer.Current.Describe(error));
                return ExitFailure;
            }
        }

        /// <summary>Rend la main dès que l'inventaire se stabilise, sans attendre le
        /// délai maximal. La résolution d'un appareil enchaîne plusieurs requêtes, une
        /// attente fixe trop courte le manquerait.</summary>
        private static async Task WaitForDiscoveryAsync(DeviceRegistry registry, int maxSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(maxSeconds);
            var lastCount = -1;
            var stableSince = DateTime.UtcNow;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(400).ConfigureAwait(false);

                var count = registry.Devices.Count;
                if (count != lastCount)
                {
                    lastCount = count;
                    stableSince = DateTime.UtcNow;
                    continue;
                }

                if (count > 0 && DateTime.UtcNow - stableSince > TimeSpan.FromSeconds(3))
                    return;
            }
        }

        private static async Task<int> DispatchAsync(
            CliArguments arguments,
            IReadOnlyList<DiyDevice> targets,
            DeviceRegistry registry,
            DeviceBook book,
            DiyClient client,
            FleetOperations fleet,
            bool json)
        {
            switch (arguments.Command.ToLowerInvariant())
            {
                case "list":
                    return List(targets, book, json);

                case "info":
                    return await InfoAsync(targets, registry, book, client, json).ConfigureAwait(false);

                case "on":
                    return Report(await fleet.SetSwitchAsync(targets, true).ConfigureAwait(false), json);

                case "off":
                    return Report(await fleet.SetSwitchAsync(targets, false).ConfigureAwait(false), json);

                case "startup":
                    return await StartupAsync(arguments, targets, fleet, json).ConfigureAwait(false);

                case "pulse":
                    return await PulseAsync(arguments, targets, fleet, json).ConfigureAwait(false);

                case "signal":
                    return await SignalAsync(targets, book, client, json).ConfigureAwait(false);

                case "identify":
                    return await IdentifyAsync(targets, fleet).ConfigureAwait(false);

                case "wifi":
                    return await WifiAsync(arguments, targets, client, json).ConfigureAwait(false);

                case "flash":
                    return await FlashAsync(arguments, targets, client, registry, book, json).ConfigureAwait(false);

                case "profile":
                    return await ProfileAsync(arguments, targets, book, fleet, json).ConfigureAwait(false);

                case "snapshot":
                    return await SnapshotAsync(targets, json).ConfigureAwait(false);

                default:
                    Console.Error.WriteLine($"Unknown command: {arguments.Command}");
                    PrintUsage();
                    return ExitUsage;
            }
        }

        private static int List(IReadOnlyList<DiyDevice> targets, DeviceBook book, bool json)
        {
            var views = targets.Select(d => DeviceView.From(d, book)).ToArray();

            if (json)
                OutputWriter.WriteJson(views);
            else
                OutputWriter.WriteTable(views);

            return views.Length == 0 ? ExitNoDevice : ExitOk;
        }

        private static async Task<int> InfoAsync(
            IReadOnlyList<DiyDevice> targets, DeviceRegistry registry, DeviceBook book, DiyClient client, bool json)
        {
            if (targets.Count == 0)
                return NoDevice(json);

            // Relecture directe : l'annonce mDNS peut dater de plusieurs secondes.
            foreach (var device in targets)
            {
                try
                {
                    registry.ApplyState(device.DeviceId, await client.GetInfoAsync(device).ConfigureAwait(false));
                }
                catch (DiyException error)
                {
                    Console.Error.WriteLine(Localizer.Current.Describe(error));
                }
            }

            var refreshed = targets
                .Select(d => registry.Find(d.DeviceId) ?? d)
                .Select(d => DeviceView.From(d, book))
                .ToArray();

            if (json)
                OutputWriter.WriteJson(refreshed);
            else
                OutputWriter.WriteTable(refreshed);

            return ExitOk;
        }

        private static async Task<int> StartupAsync(CliArguments arguments, IReadOnlyList<DiyDevice> targets, FleetOperations fleet, bool json)
        {
            var mode = ProtocolEnums.ToStartupMode(arguments.Operands.FirstOrDefault());
            if (mode == StartupMode.Unknown)
            {
                Console.Error.WriteLine("startup expects: on | keep | off");
                return ExitUsage;
            }

            // Le premier opérande est le mode, il ne désigne pas un appareil.
            var devices = Rescope(targets, arguments, skip: 1);
            return Report(await fleet.SetStartupAsync(devices, mode).ConfigureAwait(false), json);
        }

        private static async Task<int> PulseAsync(CliArguments arguments, IReadOnlyList<DiyDevice> targets, FleetOperations fleet, bool json)
        {
            var wanted = arguments.Operands.FirstOrDefault()?.ToLowerInvariant();
            if (wanted is not ("on" or "off"))
            {
                Console.Error.WriteLine("pulse expects: on | off");
                return ExitUsage;
            }

            var width = arguments.Int("width", 500);
            var devices = Rescope(targets, arguments, skip: 1);
            return Report(await fleet.SetPulseAsync(devices, wanted == "on", width).ConfigureAwait(false), json);
        }

        private static async Task<int> SignalAsync(IReadOnlyList<DiyDevice> targets, DeviceBook book, DiyClient client, bool json)
        {
            if (targets.Count == 0)
                return NoDevice(json);

            var readings = new List<object>();
            var failed = false;

            foreach (var device in targets)
            {
                try
                {
                    var rssi = await client.GetSignalStrengthAsync(device).ConfigureAwait(false);
                    readings.Add(new { id = device.DeviceId, name = book.DisplayName(device.DeviceId), rssi });

                    if (!json)
                        Console.WriteLine($"{book.DisplayName(device.DeviceId),-24} {rssi,5} dBm");
                }
                catch (DiyException error)
                {
                    failed = true;
                    Console.Error.WriteLine(Localizer.Current.Describe(error));
                }
            }

            if (json)
                OutputWriter.WriteJson(readings);

            return failed ? ExitFailure : ExitOk;
        }

        private static async Task<int> IdentifyAsync(IReadOnlyList<DiyDevice> targets, FleetOperations fleet)
        {
            if (targets.Count == 0)
                return NoDevice(false);

            foreach (var device in targets)
                await fleet.IdentifyAsync(device).ConfigureAwait(false);

            return ExitOk;
        }

        private static async Task<int> WifiAsync(CliArguments arguments, IReadOnlyList<DiyDevice> targets, DiyClient client, bool json)
        {
            var ssid = arguments.Value("ssid");
            var password = arguments.Value("password");

            if (ssid is null || password is null)
            {
                Console.Error.WriteLine("wifi expects --ssid and --password");
                return ExitUsage;
            }

            if (targets.Count == 0)
                return NoDevice(json);

            var failed = false;
            foreach (var device in targets)
            {
                try
                {
                    await client.SetWifiAsync(device, ssid, password).ConfigureAwait(false);
                    Console.WriteLine($"{device.DeviceId}: ok");
                }
                catch (DiyException error)
                {
                    failed = true;
                    Console.Error.WriteLine(Localizer.Current.Describe(error));
                }
            }

            return failed ? ExitFailure : ExitOk;
        }

        private static async Task<int> FlashAsync(
            CliArguments arguments, IReadOnlyList<DiyDevice> targets, DiyClient client,
            DeviceRegistry registry, DeviceBook book, bool json)
        {
            var file = arguments.Value("file");
            if (file is null)
            {
                Console.Error.WriteLine("flash expects --file <firmware.bin>");
                return ExitUsage;
            }

            if (targets.Count == 0)
                return NoDevice(json);

            var log = new ApiLog();
            var flasher = new OtaFlasher(client, log, registry);
            var snapshots = new SnapshotStore();
            var options = new OtaOptions { IgnoreSignalCheck = arguments.Flag("force-signal") };
            var failed = false;

            // Séquentiel : plusieurs modules téléchargeant en parallèle saturent le
            // point d'accès, et un transfert interrompu peut rendre un module inerte.
            foreach (var device in targets)
            {
                await snapshots.CaptureAsync(device, "before-ota").ConfigureAwait(false);
                var name = book.DisplayName(device.DeviceId);
                var lastPhase = string.Empty;

                var progress = new Progress<OtaProgress>(p =>
                {
                    var phase = p.Phase.ToString();
                    if (phase == lastPhase)
                        return;

                    lastPhase = phase;
                    Console.WriteLine($"{name}: {phase.ToLowerInvariant()}");
                });

                try
                {
                    await flasher.FlashAsync(device, file, progress, options).ConfigureAwait(false);
                    Console.WriteLine($"{name}: ok");
                }
                catch (DiyException error)
                {
                    failed = true;
                    Console.Error.WriteLine($"{name}: {Localizer.Current.Describe(error)}");
                }
            }

            return failed ? ExitFailure : ExitOk;
        }

        private static async Task<int> ProfileAsync(
            CliArguments arguments, IReadOnlyList<DiyDevice> targets, DeviceBook book, FleetOperations fleet, bool json)
        {
            var profile = new ConfigProfile
            {
                Name = arguments.Value("name", "cli")!,
                Startup = arguments.Value("startup") is { } s ? ProtocolEnums.ToStartupMode(s) : null,
                PulseEnabled = arguments.Value("pulse") is { } p ? p.Equals("on", StringComparison.OrdinalIgnoreCase) : null,
                PulseWidthMs = arguments.Has("width") ? arguments.Int("width", 500) : null,
            };

            if (profile.Startup == StartupMode.Unknown)
            {
                Console.Error.WriteLine("--startup expects: on | keep | off");
                return ExitUsage;
            }

            if (targets.Count == 0)
                return NoDevice(json);

            var changes = fleet.PlanProfile(targets, profile);

            if (arguments.Flag("dry-run"))
            {
                if (json)
                    OutputWriter.WriteJson(changes);
                else if (changes.Count == 0)
                    Console.WriteLine(Localizer.Current["profile.noChange"]);
                else
                    OutputWriter.WriteChanges(changes, book);

                return ExitOk;
            }

            return Report(await fleet.ApplyProfileAsync(targets, profile).ConfigureAwait(false), json);
        }

        private static async Task<int> SnapshotAsync(IReadOnlyList<DiyDevice> targets, bool json)
        {
            if (targets.Count == 0)
                return NoDevice(json);

            var store = new SnapshotStore();
            foreach (var device in targets)
                Console.WriteLine(await store.CaptureAsync(device).ConfigureAwait(false));

            return ExitOk;
        }

        /// <summary>Recalcule la cible en ignorant les opérandes consommés par la
        /// commande elle-même.</summary>
        private static IReadOnlyList<DiyDevice> Rescope(IReadOnlyList<DiyDevice> targets, CliArguments arguments, int skip)
        {
            if (arguments.Flag("all") || arguments.Has("tag") || arguments.Operands.Count <= skip)
                return targets;

            var wanted = arguments.Operands.Skip(skip).ToArray();
            return targets.Where(d => wanted.Any(w => string.Equals(w, d.DeviceId, StringComparison.OrdinalIgnoreCase))).ToArray();
        }

        private static int Report(IReadOnlyList<OperationResult> results, bool json)
        {
            if (json)
            {
                OutputWriter.WriteJson(results.Select(r => new
                {
                    id = r.DeviceId,
                    ok = r.Success,
                    error = r.Error?.ToString(),
                }));
            }
            else
            {
                foreach (var result in results)
                    Console.WriteLine($"{result.DeviceId}: {(result.Success ? "ok" : result.Error?.ToString() ?? "failed")}");
            }

            if (results.Count == 0)
                return ExitNoDevice;

            return results.Any(r => !r.Success) ? ExitFailure : ExitOk;
        }

        /// <summary>Le nom de la règle fautive est ajouté au message traduit : il
        /// évite de chercher laquelle, parmi la dizaine que Windows accumule.</summary>
        private static string FirewallMessage(FirewallStatus status)
        {
            var message = Localizer.Current["devices.emptyFirewall"];
            return status.RuleName is { } nom ? $"{message} (« {nom} »)" : message;
        }

        private static int NoDevice(bool json)
        {
            if (json)
                OutputWriter.WriteJson(Array.Empty<object>());
            else
                Console.Error.WriteLine(Localizer.Current["devices.empty"]);

            return ExitNoDevice;
        }

        private static void DumpLog(ApiLog log)
        {
            foreach (var entry in log.Snapshot())
            {
                Console.Error.WriteLine(
                    $"{entry.Timestamp:HH:mm:ss.fff} {entry.Level,-7} {entry.DeviceId,-12} {entry.Endpoint ?? entry.Message}");

                if (entry.Request is not null)
                    Console.Error.WriteLine($"  > {entry.Request}");

                if (entry.Response is not null)
                    Console.Error.WriteLine($"  < {entry.Response}");
            }
        }

        private static void PrintUsage() => Console.WriteLine(
            $"""
            diyb-cli {AppVersion.Display} — SONOFF / eWeLink DIY mode control

            USAGE
              diyb-cli <command> [targets] [options]

            COMMANDS
              list                        List discovered devices
              info                        Read live state from each device
              on | off                    Switch every channel
              startup <on|keep|off>       Set the power-on state
              pulse <on|off> [--width ms] Set inching (auto-release) mode
              signal                      Read signal strength
              identify                    Blink the relay to locate a device
              snapshot                    Save each device configuration
              wifi --ssid S --password P  Move devices to another network (leaves DIY mode)
              flash --file f.bin          Flash firmware over the air
              profile [--startup m] [--pulse on|off] [--width ms] [--dry-run]
                                          Apply a reference configuration

            TARGETS
              <id|name|ip>...             One or more devices
              --all                       Every device found (default when none given)
              --tag <tag>                 Devices carrying that tag
              --all-modes                 Include devices still bound to the eWeLink cloud

            OPTIONS
              --wait <s>                  Discovery deadline, default 12, max 120
              --json                      Machine-readable output
              --lang <code>               Message language, e.g. fr or en
              --force-signal              Flash despite a weak signal
              --verbose                   Dump the protocol exchanges to stderr
              -h, --help                  This help
              --version                   Print the version and exit

            EXIT CODES
              0 success   1 at least one failure   2 usage error   3 no device matched

            EXAMPLES
              diyb-cli list --json
              diyb-cli off --all
              diyb-cli startup off 10011c676a 10011b6176
              diyb-cli pulse on --width 1000 --tag garage
              diyb-cli profile --startup off --dry-run --all
            """);
    }
}
