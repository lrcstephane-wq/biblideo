# Design et recette Idéo Nexus

## Première version de l’interface sur la branche

- La fenêtre principale conserve le cadre Windows natif. La navigation repliable regroupe les vues métier et l’atelier Biblidéo. Les libellés sont disponibles pour l’accessibilité et en infobulle quand le bandeau est replié.
- Accueil ouvre le tableau de bord existant. La consultation des licences depuis cette page utilise le cache s’il existe sans réécrire son index. Les indicateurs restent ceux de Biblidéo.
- Applications donne accès à l’atelier et aux licences de Biblidéo. La fiche Atlas annonce les fonctions d’administration à développer dans Nexus, à savoir la gestion et la distribution des licences ainsi que la distribution des mises à jour. Atlas reste une application distincte. Cette vue correspond à un répertoire initial, avec fiches éditables à développer.
- Les styles de navigation propres à Nexus sont dans `Themes/NexusWorkspace.xaml`. Les vues héritées partagent déjà `App.xaml` et ses styles d’ascenseurs dans les deux sens. Les listes et cartes virtualisées continuent d’utiliser les contrôles existants.

## Design retenu pour la suite

- **Navigation et espace de travail.** Ardoise pour les zones persistantes, surfaces de travail bleu-gris et accents réservés aux actions importantes, au focus et aux états. Segoe UI et composants communs WPF. Garder les intitulés métier et la densité des tableaux. Consolider les palettes et styles existants au fil des vues pour éviter deux thèmes incohérents.
- **Fiches et onglets.** Entrées racines dans la barre latérale. Onglet de travail unique par type d’entité et identifiant stable, avec fermeture explicite, conservation de la sélection et retour à la liste filtrée. Les sous-onglets affichent les détails de la fiche. En détachant une vue, transférer le contexte dans une seule fenêtre éditable, puis la réintégrer à la fermeture.
- **Fenêtres Windows.** Conserver cadre, ancrage, clavier et changements d’échelle natifs. Prévoir une persistance locale du cadre restauré, corrigée à partir des zones visibles quand un moniteur disparaît. Les dialogues gardent un propriétaire explicite et rétablissent le focus. Réserver la modalité aux confirmations ou aux saisies devant aboutir avant de poursuivre.
- **Listes et défilement.** Une zone de défilement dominante par panneau. Le défilement horizontal sert les tableaux larges. Inspecter les sous-contrôles des menus, champs et calendriers dans la recette. Conserver le recyclage des vues listes et cartes et éviter d’envelopper une liste virtualisée dans un ascenseur qui lui donne une hauteur infinie.
- **États.** Présenter chargement, données indisponibles, erreur, mise à jour réussie et actions sensibles avec des messages explicites. En mode hors connexion, afficher la date des données et désactiver les mutations au niveau de l’interface et des commandes. Afficher les priorités commerciales ou techniques suivant les permissions.

## Vérifications exécutées

Dans l’environnement Linux .NET SDK 8.0.419, la suite métier initiale du dépôt et la suite enrichie ont passé. La solution, y compris le projet WPF ciblant Windows, a compilé en Release avec 0 erreur et 0 avertissement. La suite vérifie désormais qu’un historique mixte ancien/nouveau est lisible et qu’un nouvel événement occupe une seule ligne.

```powershell
dotnet restore Biblideo.sln
dotnet build Biblideo.sln -c Release --no-restore
dotnet run --project tests/Biblideo.Core.SmokeTests -c Release --no-build
```

## Recette Windows à réaliser avec données fictives

| Scénario | Vérification attendue |
| --- | --- |
| Démarrage et navigation | Accueil, raccourcis, état actif, navigation au clavier avec bandeau replié et retour aux modules. Dépôt GitHub privé injoignable affiché comme vérification indisponible. |
| Atelier, clients et licences | Opérations Biblidéo sur copies, affichage Liste/Cartes, journal mixte, Outlook bureau, expiration et distribution vérifiée. Examiner les journaux et les archives résultantes. |
| Fenêtres et défilement | Windows 10/11, 100–200 % DPI, petite résolution et deux moniteurs de densités différentes. Vérifier focus des dialogues, barres dans les tableaux et menus, fluidité des listes et absence de contenu coupé. Ajouter détachement, restauration et moniteur débranché à la recette quand ces fonctions seront intégrées. |
| Futur stockage | Trois utilisateurs simultanés, conflit de modification affiché, écriture en ligne hors du bureau sans VPN, perte d’Internet ou du service, lecture seule datée depuis le dossier Nextcloud local sans écritures en attente, restauration et reprise avec résultat vérifiable. |
| Futures opérations | Fixtures couvrant import et retour arrière, chemins de bureau et Nextcloud, licences Atlas, suspensions, renouvellements, MRR/ARR, publications pilotes et partielles et réédition d’archives. |

Utiliser un compte Windows de test ou une VM sans accès aux partages de production. La branche reprend les emplacements de configuration de Biblidéo pour assurer sa compatibilité. Éviter d’exécuter l’artefact de recette sous un profil Windows où Biblidéo est déjà connecté aux dossiers clients.

## Aperçu autonome pour Windows

L’aperçu se compile avec la propriété `NexusPreview=true`. Il ouvre l’interface Nexus et les parcours Biblidéo existants sur trois clients et des composants fictifs, dans le dossier `%LOCALAPPDATA%\Ideo Solutions\Nexus Preview`. Les paramètres de cet aperçu sont consultables en lecture seule. Les mises à jour via GitHub et les notifications Outlook sont désactivées dans cette variante. Les opérations d’atelier utilisent les chemins de démonstration. Les fichiers TopSolid fictifs servent à la simulation des packages.

```powershell
dotnet publish src/Biblideo.App -c Release -r win-x64 --self-contained true -p:NexusPreview=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/NexusPreview
```

Le fichier produit `Biblideo.exe` peut être renommé en `Ideo_Nexus_Apercu.exe` pour un essai côte à côte. Le nouveau registre SQLite et la copie de consultation Nextcloud restent à raccorder à l’interface Windows.
