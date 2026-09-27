# DIYB

[English](README.md) · **Français**

Outil de mise en service et de maintenance des modules SONOFF / eWeLink en mode DIY.
WinUI 3 (non empaqueté) sur .NET 8, interface traduite en 53 langues.

Remplace l'outil « DIY mode tool » d'ITEAD : même protocole, découverte continue au
lieu d'un scan manuel, profils de configuration, contrôles avant flash et journal des
échanges.

## Installation

Téléchargez `DIYB-x.y.z-setup.exe` depuis la page des
[releases](https://github.com/gillesg77/DIYB/releases). L'installeur pose
l'interface, la ligne de commande, les raccourcis et — surtout — les règles de
pare-feu.

Sans installeur, les archives `.zip` se décompressent n'importe où et s'exécutent
telles quelles ; il faudra alors accepter l'invite du pare-feu au premier lancement.

### Le pare-feu conditionne la découverte

La découverte mDNS repose sur la **réception** de datagrammes UDP. Le pare-feu
Windows autorise par chemin d'exécutable : un binaire déplacé, recompilé ailleurs ou
recopié dans un autre dossier redevient un programme inconnu, et une règle de blocage
suffit à vider la liste sans le moindre message d'erreur.

L'installeur crée les deux règles nécessaires, profils privé et domaine seulement.
Pour les poser à la main :

```bash
netsh advfirewall firewall add rule name="DIYB - découverte mDNS" dir=in action=allow program="C:\chemin\vers\DIYB.exe" protocol=udp profile=private,domain enable=yes
```

L'application lit ses propres règles de pare-feu via `INetFwPolicy2` et le dit
explicitement quand rien ne l'autorise sur le profil réseau courant, en nommant la
règle fautive. Un pare-feu qui bloque et un site sans appareils ne se ressemblent
plus.

Compter les paquets reçus ne suffisait pas : Windows embarque une règle
`mDNS (UDP-Entrée)` qui admet le multicast pour n'importe quel programme, si bien
que le trafic continue d'arriver alors même que le programme est bloqué — mesuré
ici à 95 datagrammes pour zéro appareil.

Pour vérifier ce qui bloque :

```bash
netsh advfirewall firewall show rule name=all dir=in | findstr /i diyb
```

## Prérequis

Le SDK .NET 8 suffit :

```bash
winget install Microsoft.DotNet.SDK.8
```

Visual Studio n'est pas nécessaire. Le Windows App SDK arrive par NuGet et
`WindowsAppSDKSelfContained` l'embarque dans la sortie, donc rien à installer sur les
postes cibles non plus.

## Compiler et lancer

```bash
dotnet build DIYB.sln
```

```bash
dotnet run --project src/DIYB.App
```

```bash
dotnet test DIYB.sln
```

Pour produire l'installeur et les archives, avec
[Inno Setup 6](https://jrsoftware.org/isinfo.php) installé
(`winget install JRSoftware.InnoSetup`) :

```bash
powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
```

Le script publie les deux exécutables, vérifie que les XAML compilés et les
traductions sont bien présents, puis compile `publish/DIYB-x.y.z-setup.exe`.

L'icône se régénère depuis son tracé, en huit résolutions de 16 à 256 px :

```bash
powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
```

## Mettre un module en mode DIY

Le mode DIY se sélectionne sur le module lui-même (cavalier ou séquence d'appuis selon
le modèle) et coupe la liaison au cloud eWeLink. Le module rejoint alors le réseau
Wi-Fi configuré, annonce le service mDNS `_ewelink._tcp` et expose son API REST sur le
port 8081. Le poste qui exécute DIYB doit être sur le même sous-réseau : le multicast
ne franchit pas les routeurs, et un réseau invité isolant les clients entre eux
empêchera toute découverte.

## Organisation

| Projet | Rôle |
| --- | --- |
| `src/DIYB.Core` | Protocole, découverte, OTA, persistance. Aucune dépendance Windows, aucune chaîne destinée à l'utilisateur. |
| `src/DIYB.Localization` | Traductions partagées par l'interface et la ligne de commande. |
| `src/DIYB.App` | Interface WinUI 3. |
| `src/DIYB.Cli` | `diyb-cli`, pilotage scriptable. |
| `tests/DIYB.Core.Tests` | Tests du coeur et de la ligne de commande, sans matériel ni réseau. |

Le coeur ne formate aucun message : il lève des `DiyException` portant un `ErrorCode`
et des paramètres nommés, que la couche interface traduit. C'est ce qui permet
d'ajouter une langue sans toucher au protocole.

### Découverte

`MdnsClient` implémente le strict nécessaire de mDNS plutôt que de dépendre d'une
bibliothèque : il faut un accès brut aux enregistrements TXT, dont les modules
éclatent leur état sur quatre segments `data1`..`data4`, et une écoute permanente du
groupe multicast.

Les modules réannoncent spontanément leur TXT à chaque changement d'état. L'écoute
passive donne donc l'état en quasi temps réel, y compris quand quelqu'un actionne le
bouton physique, sans interrogation périodique. Une requête PTR est tout de même émise
toutes les vingt secondes pour rattraper les annonces perdues, et les interfaces sont
reconstruites sur `NetworkAddressChanged` — un portable qui change de Wi-Fi ne perd
pas la liste.

Un PTR à TTL nul retire immédiatement l'appareil ; à défaut, il disparaît après trois
minutes de silence.

Deux détails conditionnent le fonctionnement, l'un comme l'autre vérifiés sur
matériel :

- **Les modules ne répondent à la requête de service que par le PTR.** Adresse et état
  réclament des requêtes SRV, TXT puis A de suivi, espacées par nom et par type pour
  ne pas inonder le réseau. Sans elles, les modules apparaissent dans les annonces
  mais aucun appareil n'est jamais assemblé.
- **Les sockets d'émission sont liées au port 5353.** Une requête émise depuis un port
  éphémère est traitée comme une requête unicast héritée : le répondeur répond alors
  directement à ce port plutôt qu'au groupe multicast, et la réponse est perdue si
  seul le port 5353 est écouté. Les sockets d'émission sont écoutées elles aussi, au
  cas où le port 5353 serait déjà pris.

### Modules en mode cloud

Un module encore appairé au cloud eWeLink s'annonce sur `_ewelink._tcp` comme un
module DIY, mais avec `type=plug`, `encrypt=true` et une charge `data1` chiffrée en
AES. Son API locale exige la clé d'appairage du compte, que cet outil n'a pas.

Ces modules sont donc affichés avec un badge `CLOUD`, leurs contrôles désactivés, et
ils sont exclus des actions groupées — sans quoi chaque commande attendrait trois
secondes d'expiration par module. La barre d'état indique combien ont été ignorés.

### Mono et multi-canaux

Un appareil mono-canal expose un canal unique d'indice 0, ce qui évite deux chemins de
code dans toute la couche au-dessus. `DiyClient` choisit la forme de charge utile
d'après l'état connu :

| Opération | Mono-canal | Multi-canaux |
| --- | --- | --- |
| Relais | `/zeroconf/switch` · `switch` | `/zeroconf/switches` · `switches[]` |
| Démarrage | `/zeroconf/startup` · `startup` | `/zeroconf/startup` · `configure[]` |
| Impulsion | `/zeroconf/pulse` · `pulse` | `/zeroconf/pulses` · `pulses[]` |

Le mode KEEP de l'interface correspond à la valeur `stay` du firmware.

### Flash OTA

Le module télécharge le firmware depuis une URL que l'outil lui fournit. `FirmwareServer`
est bâti sur `TcpListener` et non sur `HttpListener`, qui réclamerait une réservation
d'URL administrateur pour écouter ailleurs que sur localhost.

Séquence : sauvegarde de la configuration, contrôle du signal, calcul du SHA-256,
`ota_unlock`, `ota_flash`, suivi des octets servis, puis attente du retour du module et
relecture de sa version.

Garde-fous :

- refus si le signal est sous **-70 dBm**, seuil contournable explicitement ;
- l'adresse locale publiée est celle du sous-réseau du module, pas la première venue ;
- flash séquentiel et non parallèle, plusieurs modules téléchargeant en même temps
  saturant le point d'accès.

### Vérification des versions

Il n'existe pas d'API publique de catalogue chez ITEAD : la documentation officielle
indique que la mise à jour passe par l'application eWeLink, et le dépôt
`itead/Sonoff_Devices_DIY_Tools` ne publie ni binaires ni liste de versions.
`IFirmwareCatalog` accepte donc plusieurs sources :

- `JsonFirmwareCatalog` — fichier local ou URL maîtrisée ;
- `GithubReleaseCatalog` — publications d'un dépôt de firmware alternatif ;
- à défaut de catalogue, comparaison au reste du parc, qui signale les modules en
  retard sur leurs homologues sans aucun accès extérieur.

Format du catalogue JSON :

```json
{
  "releases": [
    {
      "model": "diy_plug",
      "version": "3.7.2",
      "url": "http://serveur/firmware/diy_plug-3.7.2.bin",
      "sha256": "…",
      "channel": "release",
      "notes": "Correctif de reconnexion Wi-Fi"
    }
  ]
}
```

`model` vaut `*` pour s'appliquer à tout appareil, sinon il est comparé au champ `type`
du TXT mDNS.

## Fonctionnement

### Profils de configuration

Un profil fixe une configuration de référence — état au démarrage, impulsion, durée —
et laisse non gouvernée toute propriété nulle. Chaque appareil porte alors un badge
« conforme » ou « dérive », et le bouton d'aperçu liste les changements avant
application. Un profil peut être restreint à certaines étiquettes.

### Localiser un module

Le bouton en bout de ligne fait battre le relais trois fois puis rétablit l'état
initial, y compris si l'opération est interrompue. Utile pour identifier physiquement
un module parmi des prises identiques.

### Simulateur

Le menu `⋯` ajoute des appareils factices, un sur trois étant multi-canaux. Ils
répondent au protocole complet en mémoire, ce qui permet de démontrer l'outil et de
travailler sur l'interface sans matériel. Ils portent un badge `SIM`.

### Journal

Chaque échange est enregistré avec sa requête, sa réponse et sa durée ; cliquer une
ligne affiche le JSON brut. Les clés Wi-Fi sont masquées avant enregistrement, un
journal exporté pour diagnostic n'en contient donc pas.

## Ligne de commande

`diyb-cli` partage le coeur et les traductions de l'interface, et sert à scripter un
déploiement.

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

`diyb-cli --help` détaille commandes, cibles et options. Les cibles se désignent par
identifiant, par nom local, par adresse, par `--tag`, ou globalement par `--all` ;
sans cible, toutes les machines trouvées sont visées. `--all-modes` inclut les modules
restés sur le cloud, exclus par défaut.

La découverte rend la main dès que l'inventaire se stabilise, `--wait` ne fixant qu'un
plafond : la résolution d'un appareil enchaîne plusieurs requêtes, une attente fixe
trop courte le manquerait.

Codes de sortie : `0` succès, `1` au moins un échec, `2` erreur d'usage, `3` aucun
appareil ne correspond. Le texte d'aide reste en anglais, par convention pour un
outil en ligne de commande ; les messages d'exécution suivent la langue choisie.

## Données locales

Sous `%LOCALAPPDATA%\DIYB` :

| Fichier | Contenu |
| --- | --- |
| `devices.json` | Noms, étiquettes et notes, indexés par identifiant de module. |
| `profiles.json` | Profils de configuration. |
| `settings.json` | Langue, profil actif, catalogues de firmware. |
| `snapshots/` | Sauvegardes de configuration, dont celles prises avant flash. |

Les écritures passent par un fichier temporaire suivi d'un remplacement : une coupure
en cours d'enregistrement laisse l'ancien fichier intact.

Pour brancher un catalogue, ajouter à `settings.json` :

```json
{ "firmwareCatalogs": ["https://serveur/firmwares.json"] }
```

## Langues

L'application est livrée en **53 langues** : toutes les langues d'Europe hors turc,
plus le russe, plus les principales langues d'Asie et du Moyen-Orient. Le changement
est immédiat, sans redémarrage, et le réglage est retenu.

L'arabe, l'hébreu, le persan et l'ourdou basculent l'interface entière en écriture de
droite à gauche via `FlowDirection`, pas seulement les libellés.

Ces traductions n'ont pas été relues par des locuteurs natifs. Pour une diffusion
publique, les langues de vos marchés mériteraient une relecture.

### Ajouter ou corriger une langue

Les traductions sont des fichiers JSON plats dans `Strings`, à côté de l'exécutable.
Copier `en.json`, le renommer avec le code ISO de la langue, traduire les valeurs. Le
fichier est détecté au démarrage et la langue apparaît dans le sélecteur. La clé
`language.name` porte le nom affiché dans ce sélecteur.

Une clé absente retombe sur l'anglais : une traduction partielle reste exploitable.

Le contrôle de cohérence vérifie que chaque fichier est du JSON valide, porte les
mêmes clés que la référence anglaise, et conserve les marqueurs de substitution
(`{0}`, `{deviceId}`…) — un marqueur perdu casse le formatage à l'exécution :

```bash
python tools/check-translations.py
```

Les mêmes contrôles tournent dans la suite de tests, donc une traduction incomplète
fait échouer la compilation d'intégration.

Les termes du protocole — ON, OFF, KEEP, SSID, OTA, firmware — restent inchangés dans
toutes les langues.

## Pièges rencontrés

`ImmutableArray` compare ses instances par référence. Sans redéfinition explicite de
l'égalité sur `DeviceState`, deux états de contenu identique passaient pour différents
et l'interface se rafraîchissait en boucle.

Un `Window` WinUI 3 n'est pas un `FrameworkElement` : une liaison compilée utilisant un
convertisseur `StaticResource` dans un `DataTemplate` ne peut pas y résoudre ses
ressources, et le compilateur XAML échoue sans message. L'interface vit donc dans
`MainPage`, un `UserControl` que la fenêtre héberge.

`dotnet publish` n'emporte pas les XAML compilés (`.xbf`) : l'application se fermait
alors en silence au démarrage, code de sortie 0, sans la moindre trace. Une cible
`PublishCompiledXaml` du csproj les ajoute, et le script d'installeur refuse de
compiler s'ils manquent.

Les liaisons compilées n'acceptent pas un indexeur à clé pointée. Les libellés passent
par `Localizer.Get('clé')`, un appel de méthode à argument littéral, réévalué quand la
propriété `Loc` change.

## Licence

GPL-3.0. Voir [LICENSE](LICENSE).
