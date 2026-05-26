<p align="center">
  <img src="assets/banner.png" alt="JellyTrack Plugin Banner">
</p>

<p align="center">
  <img src="logo.svg" width="64" height="64" alt="JellyTrack Logo">
</p>

<h1 align="center">JellyTrack Plugin</h1>

<p align="center">
  <img src="https://img.shields.io/github/v/release/maelmoreau21/Jellyfin.Plugin.JellyTrack" alt="GitHub Release">
  <img src="https://img.shields.io/github/license/maelmoreau21/Jellyfin.Plugin.JellyTrack" alt="License">
</p>

<p align="center">
  <strong>Plugin Jellyfin pour JellyTrack : capture et envoie les evenements de lecture et metadonnees en temps reel vers votre serveur d'analytics.</strong>
</p>

---

> [!IMPORTANT]
> ### Serveur JellyTrack requis
> Ce plugin ne fonctionne que s'il est connecte a une instance active de **JellyTrack**. Sans serveur, le plugin n'aura aucun effet visible.
>
> [Deployer le serveur JellyTrack](https://github.com/maelmoreau21/JellyTrack)

---

## Installation par depot Jellyfin

Privilegiez l'installation via le depot officiel pour beneficier des mises a jour automatiques directement depuis votre interface Jellyfin.

### 1. Ajouter le depot

1. Dans Jellyfin : **Tableau de bord** > **Plugins** > **Depots**.
2. Cliquez sur le bouton `+` (Ajouter).
3. Remplissez les informations suivantes :
   - **Nom** : `JellyTrack`
   - **URL** : `https://raw.githubusercontent.com/maelmoreau21/Jellyfin.Plugin.JellyTrack/main/manifest.json`

### 2. Installation

1. Allez dans l'onglet **Catalogue**.
2. Recherchez **JellyTrack** et installez-le.
3. **Redemarrez Jellyfin** pour activer le plugin.

---

## Configuration

Une fois installe, rendez-vous dans **Tableau de bord** > **Plugins** > **JellyTrack** pour configurer la connexion :

- **URL JellyTrack** : l'adresse de votre serveur, par exemple `http://192.168.1.100:3000`.
- **Cle API** : la cle generee dans l'interface de JellyTrack, au format `jt_xxxxxxxxxxxx`.
- **Intervalle heartbeat** : frequence de verification de sante, par defaut `600s`.
- **Telemetrie** : le profil `Tres precis` est le defaut, avec progression toutes les `5s` en lecture et toutes les `30s` en pause.

> [!TIP]
> Utilisez le bouton **Tester la connexion** pour verifier que le plugin communique correctement avec votre serveur avant d'enregistrer.

---

## Build Jellyfin 10.11 public

La version `1.6.0.0` cible Jellyfin `10.11.x` public avec `JellyfinPackageVersion=10.11.10` et `targetAbi=10.11.0.0`.

```powershell
dotnet restore .\plugin-jellytrack.sln `
  -p:JellyfinPackageVersion=10.11.10

dotnet build .\plugin-jellytrack.sln -c Release --no-restore `
  -p:JellyfinPackageVersion=10.11.10 `
  -warnaserror:CS0618
```

Pour publier la version `1.6.0.0`, creez `Jellyfin.Plugin.JellyTrack-1.6.0.0.zip` depuis le build Release, puis lancez `scripts/update_manifest.py` avec `REPO=maelmoreau21/Jellyfin.Plugin.JellyTrack` et `RELEASE_TAG=v1.6.0.0` afin de calculer le checksum du vrai zip.

---

## Installation manuelle

Si vous ne pouvez pas utiliser le depot :

1. Telechargez le fichier `Jellyfin.Plugin.JellyTrack-1.6.0.0.zip` depuis les [Releases](https://github.com/maelmoreau21/Jellyfin.Plugin.JellyTrack/releases).
2. Creez un dossier `JellyTrack` dans votre repertoire `plugins` Jellyfin.
3. Extrayez `meta.json`, `Jellyfin.Plugin.JellyTrack.deps.json`, `Jellyfin.Plugin.JellyTrack.dll`, `Jellyfin.Plugin.JellyTrack.pdb` et `Jellyfin.Plugin.JellyTrack.png` dedans, puis redemarrez Jellyfin.

---

## Licence

Distribue sous licence **MIT**.
