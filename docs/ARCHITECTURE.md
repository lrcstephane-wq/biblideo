# Architecture fonctionnelle V0

## Principe de sécurité

Les services de la V0 n'exposent aucune méthode de copie, déplacement, suppression ou compression. Ils produisent uniquement un `SimulationPlan`, ensuite transformé en rapport HTML.

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

Le passage en mosaïque est limité à 750 composants visibles. Au-delà, l’interface conserve la liste virtualisée et invite l’utilisateur à filtrer afin d’éviter le chargement simultané de milliers de miniatures.

## Configuration partagée

- fichier JSON réseau par défaut : `C:\Cloud\02_Idéo\99_Application\Biblideo\Config\biblideo.settings.json` ;
- emplacement paramétrable depuis l’interface ;
- fichier local `bootstrap.json` contenant uniquement le chemin réseau choisi ;
- copie locale de secours utilisée en lecture seule si le partage est indisponible ;
- écriture atomique et sauvegarde horodatée du fichier précédent.

## Distribution

- les compilations de branche produisent une archive Windows portable de test ;
- les tags `vX.Y.Z` produisent une Release GitHub Velopack installable ;
- une installation Velopack recherche les mises à jour du dépôt public au démarrage ;
- téléchargement et redémarrage restent soumis à confirmation de l’utilisateur.

## Règles d'association

- `.top.png` : nom exact du `.top` suivi de `.png`.
- `.cat` : même clé `Type + V + I`, sans tenir compte de `R` et `C`.
- `.bmp` et `icn*` : tous ceux de chaque bibliothèque contenant au moins un `.top` sélectionné.
- `.bak` et `.cfg` : ignorés.
- sans extension : ignorés et signalés.
- `.top` mal nommé : classé dans `Non classés`, mais sélectionnable.

## Extensions prévues

- réouverture des packages produit grâce à un manifeste hors de `Bibliotheque` ;
- création simulée puis réelle des packs mensuels SFX ;
- gestion des clients et abonnements ;
- déploiement avec précontrôle, transaction et rapport ;
- attribution automatique des licences.
