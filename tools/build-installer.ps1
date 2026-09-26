# Publie les deux exécutables puis compile l'installeur.
#
#   powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
#
# Prérequis : SDK .NET 8 et Inno Setup 6 (winget install JRSoftware.InnoSetup).

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "publish"

function Find-Iscc {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }

    throw "ISCC.exe introuvable. Installez Inno Setup 6 : winget install JRSoftware.InnoSetup"
}

$iscc = Find-Iscc
Write-Host "Inno Setup : $iscc"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "`nPublication de l'interface…"
dotnet publish (Join-Path $root "src\DIYB.App\DIYB.App.csproj") `
    -c Release -r win-x64 --self-contained false `
    -p:DebugType=none -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true `
    -o (Join-Path $publish "DIYB-win-x64") --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication de l'interface." }

Write-Host "`nPublication de la ligne de commande…"
dotnet publish (Join-Path $root "src\DIYB.Cli\DIYB.Cli.csproj") `
    -c Release -r win-x64 --self-contained false `
    -p:DebugType=none -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true `
    -o (Join-Path $publish "diyb-cli-win-x64") --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication de la ligne de commande." }

# Les XAML compilés conditionnent le démarrage : sans eux l'application se ferme
# en silence. Le csproj les ajoute à la publication, on le vérifie ici.
$xbf = @(Get-ChildItem (Join-Path $publish "DIYB-win-x64") -Filter *.xbf)
if ($xbf.Count -eq 0) { throw "Aucun XAML compilé dans la publication : l'application ne démarrerait pas." }
Write-Host "XAML compilés : $($xbf.Count)"

$langues = @(Get-ChildItem (Join-Path $publish "DIYB-win-x64\Strings") -Filter *.json)
Write-Host "Langues livrées : $($langues.Count)"

# Un chemin de la machine de compilation dans un binaire public serait une fuite
# discrète : on refuse de packager plutôt que de la laisser passer.
# Contrôle positif : on vérifie que les assemblies portent bien l'auteur attendu,
# plutôt que de lister les termes à bannir — une liste noire finirait par publier
# dans ce script même ce qu'elle cherche à masquer.
$auteurAttendu = "gillesg77"
$assemblies = @("DIYB.exe", "DIYB.dll", "DIYB.Core.dll", "DIYB.Localization.dll", "diyb-cli.dll")

$suspects = Get-ChildItem $publish -Recurse -Include $assemblies | ForEach-Object {
    $societe = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).CompanyName
    if ($societe -and $societe -ne $auteurAttendu) { "$($_.Name) : « $societe »" }
}
if ($suspects) {
    $suspects | ForEach-Object { Write-Host "  métadonnée inattendue — $_" }
    throw "Les assemblies ne portent pas l'auteur attendu."
}

# Aucun chemin de la machine de compilation ne doit filtrer dans un binaire public.
$octets = [System.Text.Encoding]::GetEncoding(28591)
$fuites = Get-ChildItem $publish -Recurse -Include *.dll, *.exe, *.pdb, *.json | Where-Object {
    $octets.GetString([System.IO.File]::ReadAllBytes($_.FullName)).Contains($root)
}
if ($fuites) {
    $fuites | ForEach-Object { Write-Host "  fuite : $($_.Name)" }
    throw "Des chemins locaux subsistent dans les binaires."
}
Write-Host "Binaires vérifiés : auteur conforme, aucun chemin local."

Write-Host "`nCompilation de l'installeur…"
& $iscc (Join-Path $root "packaging\DIYB.iss") | Select-Object -Last 5
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation de l'installeur." }

# Archives sans installation, pour qui préfère décompresser. La GPL impose que
# la licence accompagne toute distribution binaire, les deux README suivent.
Write-Host "`nArchives…"
$joints = @("README.md", "README.fr.md", "LICENSE") | ForEach-Object { Join-Path $root $_ }

foreach ($dossier in @("DIYB-win-x64", "diyb-cli-win-x64")) {
    Copy-Item $joints (Join-Path $publish $dossier) -Force
}

Compress-Archive -Path (Join-Path $publish "DIYB-win-x64\*") `
    -DestinationPath (Join-Path $publish "DIYB-0.1.0-win-x64.zip") -Force
Compress-Archive -Path (Join-Path $publish "diyb-cli-win-x64\*") `
    -DestinationPath (Join-Path $publish "diyb-cli-0.1.0-win-x64.zip") -Force

Write-Host "`nArtefacts :"
Get-ChildItem $publish -File | ForEach-Object {
    "  {0,-34} {1,8:N1} Mo" -f $_.Name, ($_.Length / 1MB)
}
