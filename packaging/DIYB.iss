; Installeur DIYB — Inno Setup 6.
; Compilé par tools/build-installer.ps1, qui publie d'abord les deux exécutables.

#define AppName "DIYB"
#define AppVersion "0.1.0"
#define AppPublisher "gillesg77"
#define AppUrl "https://github.com/gillesg77/DIYB"
#define AppExe "DIYB.exe"
#define CliExe "diyb-cli.exe"

[Setup]
AppId={{7C4F9E62-2B3D-4A18-9F5C-1D6E8A0B4C73}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\publish
OutputBaseFilename=DIYB-{#AppVersion}-setup
SetupIconFile=..\src\DIYB.App\Assets\DIYB.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Les règles de pare-feu et l'écriture dans Program Files réclament l'élévation.
PrivilegesRequired=admin

; L'application est publiée pour win-x64 uniquement.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
french.DesktopIcon=Créer un raccourci sur le bureau
french.AddToPath=Ajouter diyb-cli au PATH (ligne de commande)
french.FirewallRule=Autoriser la découverte des modules dans le pare-feu Windows
french.LaunchApp=Lancer {#AppName}
english.DesktopIcon=Create a desktop shortcut
english.AddToPath=Add diyb-cli to PATH (command line)
english.FirewallRule=Allow device discovery through Windows Firewall
english.LaunchApp=Launch {#AppName}

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "addtopath"; Description: "{cm:AddToPath}"
; Sans règle entrante, les réponses mDNS n'arrivent jamais et la liste reste vide :
; cochée par défaut, mais l'utilisateur garde la main.
Name: "firewall"; Description: "{cm:FirewallRule}"

[Files]
Source: "..\publish\DIYB-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\diyb-cli-win-x64\*"; DestDir: "{app}\cli"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.fr.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Le PATH machine est étendu seulement si la tâche est cochée.
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\cli"; \
    Check: NeedsPathEntry(ExpandConstant('{app}\cli')); Tasks: addtopath

[Run]
; Les règles portent sur le chemin de l'exécutable : une installation ailleurs
; en exige de nouvelles, d'où leur création ici plutôt qu'un port global.
; profile=any et non privé/domaine : Windows classe couramment un réseau filaire
; d'entreprise en « Public », et la découverte échouerait alors en silence.
Filename: "{sys}\netsh.exe"; \
    Parameters: "advfirewall firewall add rule name=""DIYB - découverte mDNS"" dir=in action=allow program=""{app}\{#AppExe}"" protocol=udp profile=any enable=yes"; \
    Flags: runhidden waituntilterminated; Tasks: firewall
Filename: "{sys}\netsh.exe"; \
    Parameters: "advfirewall firewall add rule name=""DIYB - découverte mDNS (ligne de commande)"" dir=in action=allow program=""{app}\cli\{#CliExe}"" protocol=udp profile=any enable=yes"; \
    Flags: runhidden waituntilterminated; Tasks: firewall

Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Répertoires créés à l'exécution par le Windows App SDK.
Type: filesandordirs; Name: "{app}"

[UninstallRun]
Filename: "{sys}\netsh.exe"; \
    Parameters: "advfirewall firewall delete rule name=""DIYB - découverte mDNS"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallApp"
Filename: "{sys}\netsh.exe"; \
    Parameters: "advfirewall firewall delete rule name=""DIYB - découverte mDNS (ligne de commande)"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallCli"

; Windows ajoute ses propres règles, sous le seul nom de l'exécutable, quand
; l'utilisateur répond à l'invite au premier lancement : elles survivraient au
; désinstalleur si on ne visait que les nôtres.
Filename: "{sys}\netsh.exe"; \
    Parameters: "advfirewall firewall delete rule name=all program=""{app}\{#AppExe}"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallAppAuto"
Filename: "{sys}\netsh.exe"; \
    Parameters: "advfirewall firewall delete rule name=all program=""{app}\cli\{#CliExe}"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallCliAuto"

[Code]
{ Retire l'entrée du PATH machine à la désinstallation : ajoutée sans être
  reprise, elle laissait un chemin mort derrière chaque suppression. }
procedure RemoveFromPath(Entry: string);
var
  Existing: string;
  Position: Integer;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Existing) then
    exit;

  Position := Pos(Uppercase(';' + Entry), Uppercase(Existing));
  if Position = 0 then
  begin
    Position := Pos(Uppercase(Entry + ';'), Uppercase(Existing));
    if Position = 0 then exit;
    Delete(Existing, Position, Length(Entry) + 1);
  end
  else
    Delete(Existing, Position, Length(Entry) + 1);

  RegWriteExpandStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Existing);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveFromPath(ExpandConstant('{app}\cli'));
end;

{ Évite de rallonger le PATH à chaque réinstallation. }
function NeedsPathEntry(Param: string): Boolean;
var
  Existing: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Existing) then
  begin
    Result := True;
    exit;
  end;

  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(Existing) + ';') = 0;
end;
