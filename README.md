# DIYB

**English** · [Français](README.fr.md)

Setup and maintenance tool for SONOFF / eWeLink devices running in DIY mode.
WinUI 3 (unpackaged) on .NET 8, interface translated into 53 languages.

Replaces ITEAD's own "DIY mode tool": same protocol, continuous discovery instead of
manual scans, configuration profiles, guarded firmware flashing, and a full log of the
exchanges.

## Installation

Download `DIYB-x.y.z-setup.exe` from the
[releases](https://github.com/gillesg77/DIYB/releases) page. The installer sets up the
interface, the command line, the shortcuts and — above all — the firewall rules.

The `.zip` archives run as-is from anywhere without installing; you will then have to
accept the firewall prompt yourself on first launch.

### Discovery depends on the firewall

mDNS discovery relies on **receiving** UDP datagrams. Windows Firewall authorises per
executable path: a binary that is moved, rebuilt elsewhere or copied into another
folder becomes an unknown program again, and a single block rule is enough to leave
the device list empty with no error message whatsoever.

The installer creates the two required rules, for the private and domain profiles
only. To add one by hand:

```bash
netsh advfirewall firewall add rule name="DIYB - mDNS discovery" dir=in action=allow program="C:\path\to\DIYB.exe" protocol=udp profile=private,domain enable=yes
```

To see what is blocking:

```bash
netsh advfirewall firewall show rule name=all dir=in | findstr /i diyb
```

## Prerequisites

The .NET 8 SDK is all you need:

```bash
winget install Microsoft.DotNet.SDK.8
```

Visual Studio is not required. The Windows App SDK comes from NuGet and
`WindowsAppSDKSelfContained` embeds it in the output, so there is nothing to install
on target machines either.

## Build and run

```bash
dotnet build DIYB.sln
```

```bash
dotnet run --project src/DIYB.App
```

```bash
dotnet test DIYB.sln
```

To produce the installer and the archives, with
[Inno Setup 6](https://jrsoftware.org/isinfo.php) installed
(`winget install JRSoftware.InnoSetup`):

```bash
powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
```

The script publishes both executables, checks that the compiled XAML and the
translations are present, then compiles `publish/DIYB-x.y.z-setup.exe`.

The icon is regenerated from its drawing code, in eight sizes from 16 to 256 px:

```bash
powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
```

## Putting a device into DIY mode

DIY mode is selected on the device itself (a jumper or a button sequence depending on
the model) and severs the link to the eWeLink cloud. The device then joins the
configured Wi-Fi network, announces the `_ewelink._tcp` mDNS service and exposes its
REST API on port 8081. The machine running DIYB must be on the same subnet: multicast
does not cross routers, and a guest network isolating clients from each other will
prevent discovery entirely.

## Layout

| Project | Role |
| --- | --- |
| `src/DIYB.Core` | Protocol, discovery, OTA, persistence. No Windows dependency, no user-facing strings. |
| `src/DIYB.Localization` | Translations shared by the interface and the command line. |
| `src/DIYB.App` | WinUI 3 interface. |
| `src/DIYB.Cli` | `diyb-cli`, scriptable control. |
| `tests/DIYB.Core.Tests` | Tests for the core and the command line, with no hardware and no network. |

The core formats no messages: it raises `DiyException` carrying an `ErrorCode` and
named parameters, which the interface layer translates. That is what makes it
possible to add a language without touching the protocol.

### Discovery

`MdnsClient` implements the strict minimum of mDNS rather than depending on a library:
raw access to TXT records is required, since devices split their state across four
`data1`..`data4` segments, as is permanent listening on the multicast group.

Devices spontaneously re-announce their TXT record on every state change. Passive
listening therefore gives near real-time state, including when someone presses the
physical button, with no polling. A PTR query is still issued every twenty seconds to
catch missed announcements, and the interfaces are rebuilt on `NetworkAddressChanged`
— a laptop switching Wi-Fi networks does not lose the list.

A PTR with a zero TTL removes the device immediately; otherwise it disappears after
three minutes of silence.

Two details govern whether any of this works, both verified against real hardware:

- **Devices answer the service query with the PTR record alone.** Address and state
  require follow-up SRV, TXT and then A queries, throttled per name and per type so as
  not to flood the network. Without them the devices show up in the announcements but
  no device is ever assembled.
- **The sending sockets are bound to port 5353.** A query sent from an ephemeral port
  is treated as a legacy unicast query: the responder then answers directly to that
  port rather than to the multicast group, and the answer is lost if only port 5353 is
  being listened to. The sending sockets are listened to as well, in case port 5353 is
  already taken.

### Devices still in cloud mode

A device still paired with the eWeLink cloud announces itself on `_ewelink._tcp` just
like a DIY one, but with `type=plug`, `encrypt=true` and an AES-encrypted `data1`
payload. Its local API requires the account pairing key, which this tool does not
have.

Such devices are therefore shown with a `CLOUD` badge, their controls disabled, and
they are excluded from bulk actions — otherwise every command would wait three seconds
for each of them to time out. The status bar reports how many were skipped.

### Single and multi-channel

A single-channel device exposes exactly one channel at index 0, which avoids two code
paths throughout the layers above. `DiyClient` picks the payload shape from the known
state:

| Operation | Single-channel | Multi-channel |
| --- | --- | --- |
| Relay | `/zeroconf/switch` · `switch` | `/zeroconf/switches` · `switches[]` |
| Power-on | `/zeroconf/startup` · `startup` | `/zeroconf/startup` · `configure[]` |
| Inching | `/zeroconf/pulse` · `pulse` | `/zeroconf/pulses` · `pulses[]` |

The interface's KEEP mode corresponds to the firmware's `stay` value.

### OTA flashing

The device downloads the firmware from a URL the tool provides. `FirmwareServer` is
built on `TcpListener` rather than `HttpListener`, which would require an
administrator URL reservation to listen anywhere other than localhost.

Sequence: save the configuration, check the signal, compute the SHA-256, `ota_unlock`,
`ota_flash`, track the bytes served, then wait for the device to come back and read
its version again.

Safeguards:

- refuses when the signal is below **-70 dBm**, a threshold that can be overridden
  explicitly;
- the local address published is the one on the device's subnet, not the first one
  found;
- flashing is sequential rather than parallel, since several devices downloading at
  once saturate the access point.

### Checking firmware versions

There is no public catalogue API at ITEAD: the official documentation states that
updates go through the eWeLink app, and the `itead/Sonoff_Devices_DIY_Tools`
repository publishes neither binaries nor a version list. `IFirmwareCatalog` therefore
accepts several sources:

- `JsonFirmwareCatalog` — a local file or a URL you control;
- `GithubReleaseCatalog` — releases of an alternative firmware repository;
- failing any catalogue, comparison against the rest of the fleet, which flags devices
  lagging behind their peers with no outside access at all.

JSON catalogue format:

```json
{
  "releases": [
    {
      "model": "diy_plug",
      "version": "3.7.2",
      "url": "http://server/firmware/diy_plug-3.7.2.bin",
      "sha256": "…",
      "channel": "release",
      "notes": "Wi-Fi reconnection fix"
    }
  ]
}
```

`model` is `*` to apply to any device, otherwise it is compared against the `type`
field of the mDNS TXT record.

## Usage

### Configuration profiles

A profile pins a reference configuration — power-on state, inching, duration — and
leaves any null property ungoverned. Each device then carries a "compliant" or "drift"
badge, and the preview button lists the changes before they are applied. A profile can
be restricted to certain tags.

### Locating a device

The button at the end of the row pulses the relay three times then restores the
original state, even if the operation is interrupted. Useful for physically
identifying one device among identical-looking plugs.

### Simulator

The `⋯` menu adds fake devices, one in three of them multi-channel. They answer the
full protocol in memory, which makes it possible to demonstrate the tool and to work
on the interface with no hardware. They carry a `SIM` badge.

### Log

Every exchange is recorded with its request, its response and its duration; clicking a
row shows the raw JSON. Wi-Fi keys are redacted before recording, so a log exported for
diagnosis contains none.

## Command line

`diyb-cli` shares the core and the translations of the interface, and serves to script
a deployment.

```bash
dotnet run --project src/DIYB.Cli -- list
```

```bash
diyb-cli list --json
```

```bash
diyb-cli startup off 1001abcdef 1001fedcba
```

```bash
diyb-cli pulse on --width 1000 --tag garage
```

```bash
diyb-cli profile --startup off --dry-run --all
```

`diyb-cli --help` details commands, targets and options. Targets are given by device
id, by local name, by address, by `--tag`, or globally with `--all`; with no target,
every device found is addressed. `--all-modes` includes the devices still bound to the
cloud, excluded by default.

Discovery returns as soon as the inventory settles, `--wait` only setting a ceiling:
resolving a device chains several queries, and a fixed wait that is too short would
miss it.

Exit codes: `0` success, `1` at least one failure, `2` usage error, `3` no device
matched. The help text stays in English, by convention for a command-line tool; the
runtime messages follow the chosen language.

## Local data

Under `%LOCALAPPDATA%\DIYB`:

| File | Contents |
| --- | --- |
| `devices.json` | Names, tags and notes, indexed by device id. |
| `profiles.json` | Configuration profiles. |
| `settings.json` | Language, active profile, firmware catalogues. |
| `snapshots/` | Configuration backups, including those taken before flashing. |

Writes go through a temporary file followed by a replace: an interruption mid-save
leaves the previous file intact.

To wire in a catalogue, add to `settings.json`:

```json
{ "firmwareCatalogs": ["https://server/firmwares.json"] }
```

## Languages

The application ships in **53 languages**: every European language except Turkish,
plus Russian, plus the main languages of Asia and the Middle East. Switching is
immediate, with no restart, and the setting is remembered.

Arabic, Hebrew, Persian and Urdu flip the entire interface to right-to-left through
`FlowDirection`, not just the labels.

These translations have not been reviewed by native speakers. For public distribution,
the languages of your markets would deserve a review.

### Adding or fixing a language

Translations are flat JSON files in `Strings`, next to the executable. Copy `en.json`,
rename it with the language's ISO code, translate the values. The file is detected at
startup and the language appears in the selector. The `language.name` key carries the
name shown in that selector.

A missing key falls back to English: a partial translation remains usable.

The consistency check verifies that each file is valid JSON, carries the same keys as
the English reference, and preserves the substitution markers (`{0}`, `{deviceId}`…) —
a lost marker breaks formatting at runtime:

```bash
python tools/check-translations.py
```

The same checks run in the test suite, so an incomplete translation fails the
integration build.

Protocol terms — ON, OFF, KEEP, SSID, OTA, firmware — are left unchanged in every
language.

## Pitfalls encountered

`ImmutableArray` compares its instances by reference. Without an explicit equality
override on `DeviceState`, two states with identical contents counted as different and
the interface refreshed in a loop.

A WinUI 3 `Window` is not a `FrameworkElement`: a compiled binding using a
`StaticResource` converter inside a `DataTemplate` cannot resolve its resources there,
and the XAML compiler fails with no message. The interface therefore lives in
`MainPage`, a `UserControl` hosted by the window.

`dotnet publish` does not carry the compiled XAML (`.xbf`): the application closed
silently at startup, exit code 0, with no trace at all. A `PublishCompiledXaml` target
in the csproj adds them, and the installer script refuses to build if they are
missing.

Compiled bindings do not accept an indexer with a dotted key. Labels go through
`Localizer.Get('key')`, a method call with a literal argument, re-evaluated when the
`Loc` property changes.

## Licence

GPL-3.0. See [LICENSE](LICENSE).
