# Jellyfin 12 modernization — 11 octobre 2026

Les changements sont appliqués dans `codex/jellyfin12-modernization`, créée depuis `origin/main`. Le README comportait déjà des changements locaux, conservés et adaptés. La version candidate est **2.1.0.0**, distincte des releases historiques. Aucun push, aucune publication, aucune modification de données de production.

## Dépendances et cible

| Dépendance | Avant | Après |
| --- | --- | --- |
| Jellyfin.Controller | 12.0.0-rc5 / 10.11.11 selon la cible | 12.2.0 stable |
| Jellyfin.Model | 12.0.0-rc5 / 10.11.11 selon la cible | 12.2.0 stable |
| Microsoft.NET.Test.Sdk | 17.14.1 | 18.10.1 stable |
| xunit.runner.visualstudio | 3.1.4 | 4.0.1 stable |
| xunit | 2.9.3 | 2.9.3, dernière version stable de la branche v2 |
| Runtime / projets / outil rescheck | .NET 9 et 10 | .NET 10 uniquement |

Versions vérifiées avec le feed officiel NuGet et un restore réel. Les [packages Jellyfin 12.2](https://www.nuget.org/packages/Jellyfin.Controller/12.2.0) ciblent .NET 10. Le GUID `e5a2f2ed-7c2a-4a9f-b1d3-5e6f7a8b9c0d` et l'assembly `Jellyfin.Plugin.JellyTrack` restent identiques.

**Limite de l'objectif zéro prerelease :** le graphe transitif imposé par `Jellyfin.Extensions 12.2.0` contient `ICU4N.Transliterator 60.1.0-alpha.356`, puis `ICU4N 60.1.0-alpha.356`. Les index NuGet de ces deux bibliothèques ne proposent aucune version stable. Aucune dépendance directe du plugin ou des tests n'est prerelease. Ces DLL ne sont pas incluses dans l'archive et ne figurent pas dans son `.deps.json` : Jellyfin fournit ses propres assemblies. Supprimer toute résolution prerelease nécessiterait un changement amont Jellyfin ; aucun remplacement incompatible n'a été forcé. Preuve locale : `artifacts/dependencies.json`. [ICU4N sur NuGet](https://www.nuget.org/packages/ICU4N/).

## Compatibilité vérifiée

La DLL candidate référence `MediaBrowser.Common`, `MediaBrowser.Controller` et `MediaBrowser.Model` en **12.2.0.0**. Même si une partie des signatures existe dans les versions antérieures, cette archive ne peut pas annoncer un ABI inférieur. `meta.json`, `build.yaml` et le manifeste candidat utilisent **12.2.0.0**.

| Assemblies stables | Essai avec la DLL candidate exacte | Serveur Jellyfin réel |
| --- | --- | --- |
| 12.0.0 | Échec de chargement : `MediaBrowser.Controller, Version=12.2.0.0` introuvable | Non testé |
| 12.1.0 | Même échec de chargement | Non testé |
| 12.2.0 | Chargement et enregistrement de 13 services réussis | Non testé |
| Versions ultérieures | Non testées ; doivent conserver les API/ABI nécessaires | Non testé |

Le minimum retenu est **Jellyfin 12.2 / .NET 10**, validé par compilation, tests et enregistrement isolé des services. Cela ne constitue pas une certification du fonctionnement sur un serveur Jellyfin. Aucun serveur Jellyfin n'était disponible dans l'environnement Docker inspecté ; aucun serveur ni bibliothèque de production n'a été utilisé.

Reproduction après publication locale des fichiers avec `dotnet publish` :

```powershell
python scripts/verify_abi.py
```

Le script génère des petits exécutables temporaires, référence la DLL publiée exacte et charge les packages officiels 12.0/12.1/12.2. Il écrit `artifacts/abi-checks.json`. Il ne démarre pas Jellyfin et ne simule pas une lecture réelle.

## Fiabilité et fonctionnalités

- Les notifiers Start, Progress, Stop et SessionEnded mettent les événements en file sans attendre les requêtes HTTP. Un service hébergé unique assure la livraison et la reconnexion, même en l'absence de nouvel événement.
- Une file unique, protégée par verrou, conserve l'événement en échec avant les événements suivants. Seuls les progressions adjacentes du même média et de la même session sont fusionnées : une progression n'est pas déplacée au-delà d'un arrêt, d'une pause ou d'un redémarrage de session.
- La limite configurée de la file inclut l'événement en cours. Lorsqu'elle est pleine, les progressions en attente sont éliminées avant les événements de cycle de lecture ; en dernier recours, l'événement en attente le plus ancien est perdu et journalisé.
- Une panne HTTP/réseau, un 5xx, 401, 403, 408 ou 429 est retenté après 10 secondes. Les autres erreurs 4xx sont abandonnées pour éviter qu'un payload invalide bloque toute la file. Le délai fixe ne suit pas actuellement `Retry-After`.
- Le paramètre de lot reste utilisé : pause de 100 ms après le nombre configuré de livraisons. Le paramètre ne multiplie pas le nombre de workers.
- Les observations de session sont sérialisées pour éviter que `ConcurrentDictionary.AddOrUpdate` n'émette plusieurs transitions lors de rappels concurrents. La session est initialisée au PlaybackStart pour détecter une pause avant la première progression.
- Pause/reprise et changements audio/sous-titres bypassent la cadence des progressions. La détection du seek compare le déplacement avec l'avancement attendu, et détecte aussi un retour arrière après un long intervalle.
- Le PlaybackStart conserve la position annoncée par l'événement Jellyfin, utile pour une reprise. Les métadonnées, langues, codecs, utilisateurs, types de média et sessions sont conservés.
- Le heartbeat reste immédiat au démarrage puis périodique (600 s par défaut), avec synchronisation des utilisateurs et métriques de file. Les nouveaux déclencheurs automatiques de la tâche planifiée sont supprimés ; son exécution manuelle reste disponible. Le journal parle désormais de heartbeat **mis en file**, pas de livraison réussie.
- La bibliothèque conserve sa temporisation de 30 s, déduplique les IDs et borne strictement son lot à 1 000 éléments, même avec des notifications concurrentes. L'enrichissement du lot est déplacé hors du callback et les erreurs sont capturées.
- La persistance de migration de configuration utilise directement `UpdateConfiguration`, sans réflexion historique. La réflexion qui cherchait cinq noms de propriété de vitesse a été supprimée : aucun de ces membres n'est exposé par les classes stables SessionInfo/PlayerStateInfo. Les champs optionnels de vitesse du contrat restent disponibles, sans valeur inventée.
- Les protections SSRF/URL, l'absence de redirections, les capacités de caches, le plafond de réponse de 4 096 caractères et les timeouts sont conservés. La lecture HTTP utilise `ResponseHeadersRead` et un token de timeout de 5 s couvrant aussi le corps, afin de ne pas précharger une réponse illimitée. Une clé renvoyée dans le texte de réponse est masquée.

Les files restent en mémoire : un redémarrage perd les événements non livrés. Les limites portent sur le nombre d'événements et d'éléments, pas sur un budget global d'octets. Une livraison exactement une fois ne peut pas être garantie si le serveur traite l'événement puis que sa réponse HTTP est perdue.

## Contrat JellyTrack Go

Source consultée : branche [develop](https://github.com/maelmoreau21/JellyTrack/tree/develop), commit `e484c4ead796f154f99a5b2deacce34af5678230`, `internal/plugin/events.go` et tests associés. Le clone d'analyse sous `scratch/backend-contract` est ignoré par Git. Aucune source JellyTrack n'a été éditée ; la validation complémentaire utilise l'option Go `-overlay` et des bases SQLite temporaires.

Le plugin envoie schema v3, les GUID canoniques des utilisateurs/médias, des ticks (10 000 ticks = 1 ms), des dates UTC, `Authorization: Bearer` et `X-Api-Key`. Le **SystemId réel de Jellyfin** est maintenant présent sous `serverId` sur tous les événements, avec le nom du serveur. Cette correction permet l'authentification par clé `jts4` liée au serveur, qui pouvait auparavant échouer en 403 lorsque le backend utilisait une identité de secours.

Les JSON sérialisés par les modèles .NET ont été envoyés au véritable handler Go en test : Heartbeat, LibraryChanged, PlaybackStart, PlaybackProgress, cinq PlaybackStateChanged (pause/reprise/seek/audio/sous-titres), PlaybackStop, reprise par PlaybackStart, puis SessionEnded. Les 12 requêtes sont acceptées. Une relecture SQLite confirme une seule ligne d'historique après reprise, un utilisateur et un média, les compteurs pause/seek/audio/sous-titres à 1 et zéro stream actif après la fermeture. Ce sont des événements synthétiques ; ils ne prouvent pas la réception de callbacks d'un client Jellyfin réel.

Reproduction, en remplaçant le dernier chemin par un checkout **develop** du backend :

```powershell
$env:JELLYTRACK_CONTRACT_OUTPUT = Join-Path (Get-Location) 'artifacts/go-contract-events.json'
dotnet test -c Release
python scripts/verify_go_contract.py C:/chemin/vers/JellyTrack artifacts/go-contract-events.json
```

### Suivis nécessaires côté backend, non appliqués

1. `serverIdentity` remplace un `serverUrl` absent par la valeur d'environnement, puis `http://localhost`. `upsertServer` écrase ensuite l'URL existante. Le plugin n'invente pas une adresse Jellyfin que le backend pourrait utiliser : **le backend doit préserver l'URL déjà enregistrée lorsque le payload ne fournit pas d'URL**. Avec plusieurs serveurs, une valeur globale d'environnement peut également être incorrecte. Cette correction est à traiter avant déploiement en production.
2. `sourceEventId` déduplique actuellement les téléchargements ; les compteurs de PlaybackStateChanged ne sont pas idempotents sur retransmission. Un identifiant d'événement accepté et dédupliqué atomiquement par le backend serait nécessaire pour garantir l'absence de doublons après une réponse perdue. Le plugin évite les duplications locales observées, sans annoncer une garantie distribuée.
3. Les TelemetryEvent de changements d'état utilisent l'heure de réception comme `createdAt`. Lors d'une livraison différée, le backend devrait utiliser la date UTC de l'observation si la chronologie historique doit refléter exactement le moment de la lecture.

## Validation exécutée

| Contrôle | Résultat |
| --- | --- |
| `dotnet restore` | Réussi |
| `dotnet build -c Release` | Réussi, 0 avertissement, 0 erreur |
| `dotnet test -c Release` | **98 tests réussis**, 0 échec, 0 ignoré |
| Compilation de `tools/rescheck` en Release / .NET 10 | Réussie |
| Tentative de compilation plugin en net9.0 | Échec attendu avec message explicite .NET 10 requis |
| Tests Python du manifeste | **3 réussis** ; un seul ZIP, filtrage des ABI historiques, checksum, répétition sans doublon, refus net9/ABI ancien et archive manquante |
| Tests Go existants `go test ./internal/plugin -count=1` | Réussis |
| Contrat JSON .NET → handler Go + relecture SQLite | Réussi |
| Publication locale et préparation ZIP/manifeste | Réussies ; aucun upload/release |
| CPU/RAM d'un vrai serveur et clients Jellyfin | Non mesurés |

Le test de charge local traite **100 000 observations pour 100 sessions**, avec exactement **20 000 progressions retenues** à la cadence de 5 s. Un passage a pris environ **0,62 s** et alloué environ **86,5 Mo** sur le thread de test, allocations cumulées incluant le test, et non RAM résidente. Ces nombres dépendent de la machine et n'indiquent ni CPU ni RAM d'un serveur réel. Preuve : `artifacts/telemetry-load.json`.

Les tests ciblent aussi : HTTP bloqué sans blocage de la mise en file, annulation à l'arrêt, reconnexion sans nouveaux événements, ordre start/stop, limites concurrentes de file/bibliothèque, pause immédiate après start, reprise, pistes, seek arrière, anti-doublons locaux et masquage d'une clé renvoyée par HTTP. Le fichier TRX est `artifacts/test-results/modernization.trx`.

## Préparation de publication

Une seule archive locale : `artifacts/Jellyfin.Plugin.JellyTrack-2.1.0.0.zip`, avec `meta.json`, la DLL, `.deps.json`, `.pdb` et l'image. Le checksum du manifeste candidat est calculé sur cette archive. Les assemblies Jellyfin et leurs dépendances transitives ne sont pas embarquées.

Le workflow release n'a qu'un déclenchement manuel, des permissions en lecture et produit un artifact GitHub Actions. Il ne possède plus d'étape de release, d'upload vers une release existante ou de push vers main. Le manifeste racine filtre les builds d'ABI inférieur à 12 et corrige les anciennes URLs du dépôt, en conservant les checksums des packages historiques Jellyfin 12. **Le manifeste candidat séparé** ajoute 2.1.0.0/12.2.0.0 avec une URL prévue pour la future publication ; ne pas le déployer avant l'upload manuel de l'archive.

Avant publication : résoudre/prendre en compte les suivis backend et les transitifs alpha imposés par Jellyfin, puis tester chargement, sauvegarde de configuration, lecture directe/transcodée, pause/reprise/seek/pistes, arrêt/déconnexion, scans de bibliothèque, panne/reconnexion et consommation sur un vrai serveur Jellyfin 12.2 avec des clients réels. Les versions futures restent à vérifier séparément.

## Fichiers modifiés ou ajoutés

- `.github/workflows/build.yml`
- `.github/workflows/release.yml`
- `JellyTrack.Plugin.Tests/JellyTrack.Plugin.Tests.csproj`
- `JellyTrack.Plugin.Tests/PlaybackSessionTelemetryStateTests.cs`
- `JellyTrack.Plugin/Api/JellyTrackController.cs`
- `JellyTrack.Plugin/JellyTrack.Plugin.csproj`
- `JellyTrack.Plugin/Models/HeartbeatEvent.cs`
- `JellyTrack.Plugin/Models/PluginEvent.cs`
- `JellyTrack.Plugin/Notifiers/PlaybackProgressNotifier.cs`
- `JellyTrack.Plugin/Notifiers/PlaybackStartNotifier.cs`
- `JellyTrack.Plugin/PluginServiceRegistrator.cs`
- `JellyTrack.Plugin/Services/HeartbeatService.cs`
- `JellyTrack.Plugin/Services/JellyTrackApiClient.cs`
- `JellyTrack.Plugin/Services/LibraryChangeNotifier.cs`
- `JellyTrack.Plugin/Services/PlaybackSessionTelemetryState.cs`
- `JellyTrack.Plugin/build.yaml`
- `README.md`
- `manifest.json`
- `meta.json`
- `scripts/update_manifest.py`
- `tools/rescheck/rescheck.csproj`
- `JellyTrack.Plugin.Tests/EventDeliveryTests.cs`
- `JellyTrack.Plugin.Tests/GoContractFixtureTests.cs`
- `JellyTrack.Plugin.Tests/LibraryChangeNotifierTests.cs`
- `JellyTrack.Plugin.Tests/TelemetryLoadTests.cs`
- `scripts/go_contract_test.go.txt`
- `scripts/package_release.py`
- `scripts/test_update_manifest.py`
- `scripts/verify_abi.py`
- `scripts/verify_go_contract.py`
- `docs/JELLYFIN12_MODERNIZATION.md` (ce rapport)
