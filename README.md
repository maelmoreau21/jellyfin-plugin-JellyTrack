<p align="center">
  <img src="assets/banner.png" alt="JellyTrack Plugin Banner">
</p>

<p align="center">
  <img src="logo.svg" width="64" height="64" alt="JellyTrack Logo">
</p>

<h1 align="center">JellyTrack Plugin</h1>

<p align="center">
  <img src="https://img.shields.io/github/v/release/maelmoreau21/JellyTrack.Plugin" alt="GitHub Release">
  <img src="https://img.shields.io/github/license/maelmoreau21/JellyTrack.Plugin" alt="License">
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
   - **URL** : `https://raw.githubusercontent.com/maelmoreau21/JellyTrack.Plugin/main/manifest.json`

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
- **Telemetrie** : le profil `Tres precis` est le defaut de la version Jellyfin 12 beta, avec progression toutes les `5s` en lecture et toutes les `30s` en pause.

> [!TIP]
> Utilisez le bouton **Tester la connexion** pour verifier que le plugin communique correctement avec votre serveur avant d'enregistrer.

---

## Build Jellyfin 12 beta

La branche courante du plugin cible Jellyfin 12 beta uniquement pour la prochaine version `1.3.0.0`.
Les paquets beta Jellyfin sont restaures depuis GitHub Packages, en plus de NuGet.org.

```powershell
dotnet restore .\plugin-jellytrack.sln `
  -p:JellyfinPackageVersion=12.0.0-20260523021143 `
  --source https://api.nuget.org/v3/index.json `
  --source https://nuget.pkg.github.com/jellyfin/index.json

dotnet build .\plugin-jellytrack.sln -c Release --no-restore `
  -p:JellyfinPackageVersion=12.0.0-20260523021143 `
  -warnaserror:CS0618
```

Si GitHub Packages demande une authentification, configurez une source NuGet `jellyfin` avec un token GitHub autorise a lire les packages, puis relancez les memes commandes.

Pour publier la version `1.3.0.0`, creez `JellyTrack.Plugin.zip` depuis le build Jellyfin 12 beta, puis lancez `scripts/update_manifest.py` avec `REPO=maelmoreau21/JellyTrack.Plugin` et `RELEASE_TAG=v1.3.0.0` afin de calculer le checksum du vrai zip.

---

## Installation manuelle

Si vous ne pouvez pas utiliser le depot :

1. Telechargez le fichier `JellyTrack.Plugin.dll` depuis les [Releases](https://github.com/maelmoreau21/JellyTrack.Plugin/releases).
2. Creez un dossier `JellyTrack` dans votre repertoire `plugins` Jellyfin.
3. Copiez le fichier `.dll` dedans et redemarrez Jellyfin.

---

## Licence

Distribue sous licence **MIT**.
