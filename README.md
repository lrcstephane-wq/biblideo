# Biblidéo — Post-production

Application Windows destinée à sécuriser et accélérer la post-production Biblidéo après validation des composants TopSolid.

## État actuel

L’application commence toujours par une **simulation stricte** :

- lecture des bibliothèques SC ou EP ;
- sélection persistante de plusieurs composants `.top` ;
- sélection native avec `Ctrl` et `Maj`, conservée pendant la navigation entre bibliothèques ;
- filtre actif par bibliothèque, famille ou type, avec panier récapitulatif ;
- atelier guidé en quatre étapes : identification, sélection visuelle, vérification et simulation ;
- miniatures chargées à la demande, compteurs par bibliothèque et annulation de la dernière sélection ;
- vues ligne enrichie et mosaïque responsive virtualisées pour plusieurs milliers de composants ;
- association automatique des `.top.png` et `.cat` ;
- intégration prévue des `.bmp` et `icn*` des bibliothèques sélectionnées ;
- exclusion des `.bak` et `.cfg` ;
- signalement des fichiers sans extension et des noms non conformes ;
- calcul du prochain compteur de package produit ;
- aperçu du package `NNN_Libellé_(SC|EP)` ;
- rapport HTML détaillant les opérations qui auraient été réalisées.

Après validation de ce rapport, l’application permet de créer un package produit ou de rouvrir un package déjà présent dans `000_Composants_A_Deployers` :

- création d’un nouveau dossier uniquement ;
- copie des fichiers à leur emplacement relatif exact sous `Bibliotheque` ;
- refus immédiat si la destination existe déjà ;
- aucune suppression, aucun déplacement, aucun renommage et aucun écrasement ;
- contrôle SHA-256 de chaque copie ;
- manifeste `biblideo.manifest.json` et rapport HTML d’exécution.

La modification d’un package existant est également contrôlée :

- identité, variante et compteur du package verrouillés pendant l’édition ;
- composants déjà présents automatiquement présélectionnés ;
- comparaison explicite entre contenu d’origine, ajouts, retraits et éléments conservés ;
- choix entre actualiser les fichiers conservés depuis la bibliothèque ou préserver leur copie actuelle ;
- fichiers remplacés ou retirés sauvegardés dans une corbeille récupérable avant intervention ;
- mise en corbeille d’un package complet au lieu d’une suppression définitive ;
- contrôle anti-concurrence, refus des liens de fichiers et vérification SHA-256 des nouvelles copies.

Les sources TopSolid restent toujours en lecture seule. Les fonctions de pack mensuel, de déploiement et de licence restent en simulation tant qu’elles n’ont pas reçu leurs propres protections.

## Architecture

- `src/Biblideo.Core` : règles métier, analyse TopSolid, nommage et rapport.
- `src/Biblideo.App` : interface WPF Windows.
- `tests/Biblideo.Core.SmokeTests` : contrôles métier sans dépendance externe.
- `.github/workflows/build-windows.yml` : compilation et génération automatique de l'application Windows autonome.
- `.github/workflows/release-windows.yml` : publication du fichier autonome mis à jour à partir des tags de version.

## Chemins

Tous les chemins sont modifiables depuis **Paramètres**. La configuration métier partagée est enregistrée par défaut dans :

`C:\Cloud\02_Idéo\99_Application\Biblideo\Config\biblideo.settings.json`

Chaque poste ne conserve qu’un pointeur vers ce fichier et une copie locale de secours dans `%LOCALAPPDATA%\Ideo Solutions\Biblideo`. Le chemin du fichier partagé reste lui-même paramétrable. Une sauvegarde horodatée est créée avant chaque remplacement de la configuration existante.

Le bouton de test vérifie l’accessibilité de la configuration, des dossiers métier et de 7-Zip sans modifier les données métier.

Le dossier des packages à déployer et la corbeille des packages sont eux aussi paramétrables. Par défaut, la corbeille est située dans `C:\Cloud\20_Partage_Clients\Biblidéo\Idéo_Solutions\000_Corbeille_Packages`.

## Mise à jour de l’application

L’application vérifie au démarrage les versions publiées dans les Releases GitHub du dépôt public. Le bouton de la barre supérieure indique si l’application est à jour ; lorsqu’une version est disponible, il demande confirmation avant téléchargement, contrôle d’intégrité, remplacement de `Biblideo.exe` et redémarrage.

La publication d’un tag `vX.Y.Z` déclenche la création d’une Release contenant un seul `Biblideo.exe` autonome. Après l’installation manuelle initiale de cette version, les suivantes se remplacent directement depuis l’application, comme dans Idéo Launcher.

## Compiler localement

Prérequis : Windows et SDK .NET 8.

```powershell
dotnet restore Biblideo.sln
dotnet build Biblideo.sln -c Release
dotnet run --project src/Biblideo.App/Biblideo.App.csproj
```

## Récupérer l'exécutable GitHub

Après chaque compilation réussie, l'onglet **Actions** du dépôt fournit l'artefact `Biblideo-win-x64`.
