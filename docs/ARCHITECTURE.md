# Architecture fonctionnelle

## Principe de sécurité

Le flux produit toujours un plan puis un rapport HTML avant toute exécution réelle. La création utilise exclusivement de nouveaux dossiers et des copies contrôlées. La modification agit uniquement dans le package produit explicitement ouvert : toute ancienne version remplacée ou retirée est d’abord sauvegardée dans une corbeille récupérable. Les bibliothèques sources ne sont jamais une destination d’écriture.

## Flux package produit

1. Choix exclusif de la variante SC ou EP.
2. Lecture de la source paramétrée correspondante.
3. Reconstruction `bibliothèque > famille > type > composant`.
4. Sélection native d'un ou plusieurs `.top` avec `Ctrl` ou `Maj`, dans plusieurs emplacements.
5. Conservation de la sélection dans un panier persistant pendant les changements de filtre ou de bibliothèque.
6. Résolution des fichiers associés.
7. Reprise virtuelle de tout le contenu du dossier modèle SC ou EP, avec remplacement de `---` par le compteur du package.
8. Ajout virtuel des composants et de leurs fichiers associés à leur emplacement relatif exact sous `Bibliotheque`.
9. Ajout virtuel des icônes de chaque bibliothèque concernée.
10. Contrôles du modèle et du nom du package.
11. Affichage du rapport HTML sans écriture métier.
12. Après confirmation explicite, création optionnelle du nouveau package produit.
13. Vérification de chaque copie par taille et SHA-256, puis écriture du manifeste et du rapport d’exécution.

## Modification et mise en corbeille d’un package produit

1. Inventaire des enfants directs du dossier `000_Composants_A_Deployers` dont le nom respecte `NNN_Libellé_(SC|EP)`.
2. Verrouillage de l’identité et de la variante du package choisi.
3. Analyse en lecture seule de la bibliothèque SC ou EP correspondante.
4. Présélection des `.top` déjà contenus dans le package, y compris signalement des composants absents de la source actuelle.
5. Construction d’un différentiel : origine, ajouts, retraits, éléments conservés et actions fichier par fichier.
6. Choix utilisateur entre actualisation depuis la source et préservation des copies existantes.
7. Rapport HTML de simulation avant confirmation.
8. Contrôle de l’empreinte du package pour refuser une modification concurrente.
9. Sauvegarde récupérable des fichiers remplacés ou retirés, puis copie vérifiée des ajouts et actualisations.
10. Manifeste de modification et rapport HTML final.

La « suppression » d’un package complet est un déplacement vers la corbeille configurée. Aucun bouton de purge définitive n’est exposé.

## Expérience de constitution

- parcours guidé en quatre étapes, avec variante et nom du package toujours visibles ;
- catalogue filtrable par bibliothèque, famille et type ;
- choix entre une liste compacte virtualisée et une mosaïque de cartes avec miniature TopSolid ;
- sélection persistante multi-bibliothèques avec `Ctrl` et `Maj` ;
- panier escamotable regroupé par bibliothèque, retrait unitaire, annulation de la dernière sélection et aperçu des fichiers liés ;
- contrôle arborescent dépliable montrant le modèle, les bibliothèques, chaque composant et tous les fichiers `.top`, `.png`, `.bmp` et `.cat` prévus ;
- retrait unitaire possible depuis l’étape de vérification ;
- thème gris/bleu Idéo cohérent, y compris pour les ascenseurs verticaux et horizontaux ;
- synthèse exacte issue du plan de simulation avant l’ouverture du rapport HTML, arborescence de destination comprise.

La liste et la mosaïque utilisent toutes deux une virtualisation avec recyclage : seules les lignes ou cartes visibles sont matérialisées. La mosaïque recalcule ses colonnes selon la largeur disponible, n’utilise jamais de défilement horizontal et reste exploitable avec plusieurs milliers de composants.

## Configuration partagée

- fichier JSON local neutre au premier démarrage, puis emplacement réseau choisi depuis l’interface ;
- emplacement paramétrable depuis l’interface ;
- fichier local `bootstrap.json` contenant uniquement le chemin réseau choisi ;
- fichiers locaux de correspondance des chemins, propres à chaque poste (dossier synchronisé, UNC ou lecteur réseau) ;
- copie locale de secours utilisée en lecture seule si le partage est indisponible ;
- écriture atomique et sauvegarde horodatée du fichier précédent.

## Distribution

- les compilations de branche produisent un `Biblideo.exe` Windows autonome de test ;
- les tags `vX.Y.Z` produisent une Release GitHub contenant ce même exécutable unique ;
- l’application compare sa version à la dernière Release publique, télécharge la nouvelle version et contrôle son empreinte SHA-256 lorsqu’elle est fournie ;
- le remplacement est effectué après fermeture, puis Biblidéo redémarre automatiquement ; téléchargement et redémarrage restent soumis à confirmation.

## Règles d'association

- `.top.png` : nom exact du `.top` suivi de `.png`.
- `.cat` : même clé `Type + V + I`, sans tenir compte de `R` et `C`.
- `.bmp` et `icn*` : tous ceux de chaque bibliothèque contenant au moins un `.top` sélectionné.
- `.bak` et `.cfg` : ignorés.
- sans extension : ignorés et signalés.
- `.top` mal nommé : classé dans `Non classés`, mais sélectionnable.

## Extensions prévues

- création simulée puis réelle des packs mensuels SFX ;
- gestion des clients et abonnements ;
- déploiement avec précontrôle, transaction et rapport ;
- attribution automatique des licences.
