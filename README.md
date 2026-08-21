# Biblidéo — Post-production

Application Windows destinée à sécuriser et accélérer la post-production Biblidéo après validation des composants TopSolid.

## État de la V0

La V0 fonctionne en **simulation stricte** :

- lecture des bibliothèques SC ou EP ;
- sélection persistante de plusieurs composants `.top` ;
- sélection native avec `Ctrl` et `Maj`, conservée pendant la navigation entre bibliothèques ;
- filtre actif par bibliothèque, famille ou type, avec panier récapitulatif ;
- association automatique des `.top.png` et `.cat` ;
- intégration prévue des `.bmp` et `icn*` des bibliothèques sélectionnées ;
- exclusion des `.bak` et `.cfg` ;
- signalement des fichiers sans extension et des noms non conformes ;
- calcul du prochain compteur de package produit ;
- aperçu du package `NNN_Libellé_(SC|EP)` ;
- rapport HTML détaillant les opérations qui auraient été réalisées.

La V0 ne crée, ne copie, ne déplace, ne renomme, ne compresse et ne supprime aucun fichier métier.

## Architecture

- `src/Biblideo.Core` : règles métier, analyse TopSolid, nommage et rapport.
- `src/Biblideo.App` : interface WPF Windows.
- `tests/Biblideo.Core.SmokeTests` : contrôles métier sans dépendance externe.
- `.github/workflows/build-windows.yml` : compilation et génération automatique de l'application Windows autonome.
- `.github/workflows/release-windows.yml` : création des installateurs et mises à jour à partir des tags de version.

## Chemins

Tous les chemins sont modifiables depuis **Paramètres**. La configuration métier partagée est enregistrée par défaut dans :

`C:\Cloud\02_Idéo\99_Application\Biblideo\Config\biblideo.settings.json`

Chaque poste ne conserve qu’un pointeur vers ce fichier et une copie locale de secours dans `%LOCALAPPDATA%\Ideo Solutions\Biblideo`. Le chemin du fichier partagé reste lui-même paramétrable. Une sauvegarde horodatée est créée avant chaque remplacement de la configuration existante.

Le bouton de test vérifie l’accessibilité de la configuration, des dossiers métier et de 7-Zip sans modifier les données métier.

## Mise à jour de l’application

L’application vérifie au démarrage les versions publiées dans les Releases GitHub du dépôt public. Lorsqu’une version est disponible, un bouton apparaît dans la barre supérieure et demande confirmation avant téléchargement et redémarrage.

La publication d’un tag `vX.Y.Z` déclenche la création automatique de l’installateur et des packages différentiels Velopack. Ce mécanisme est actif pour les versions installées ; l’archive Windows portable générée par la compilation reste destinée aux essais.

## Compiler localement

Prérequis : Windows et SDK .NET 8.

```powershell
dotnet restore Biblideo.sln
dotnet build Biblideo.sln -c Release
dotnet run --project src/Biblideo.App/Biblideo.App.csproj
```

## Récupérer l'exécutable GitHub

Après chaque compilation réussie, l'onglet **Actions** du dépôt fournit l'artefact `Biblideo-win-x64`.
