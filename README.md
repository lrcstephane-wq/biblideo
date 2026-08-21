# Biblidéo — Post-production

Application Windows destinée à sécuriser et accélérer la post-production Biblidéo après validation des composants TopSolid.

## État de la V0

La V0 fonctionne en **simulation stricte** :

- lecture des bibliothèques SC ou EP ;
- sélection persistante de plusieurs composants `.top` ;
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

## Chemins

Tous les chemins sont modifiables depuis **Paramètres**. Les valeurs sont enregistrées localement dans :

`%LOCALAPPDATA%\Ideo Solutions\Biblideo\settings.json`

Le bouton de test vérifie uniquement l'existence et l'accessibilité en lecture.

## Compiler localement

Prérequis : Windows et SDK .NET 8.

```powershell
dotnet restore Biblideo.sln
dotnet build Biblideo.sln -c Release
dotnet run --project src/Biblideo.App/Biblideo.App.csproj
```

## Récupérer l'exécutable GitHub

Après chaque compilation réussie, l'onglet **Actions** du dépôt fournit l'artefact `Biblideo-win-x64`.
