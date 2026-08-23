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
7. Ajout virtuel des icônes de chaque bibliothèque concernée.
8. Contrôles du modèle et du nom du package.
9. Affichage du rapport HTML sans écriture métier.

## Expérience de constitution

- parcours guidé en quatre étapes, avec variante et nom du package toujours visibles ;
- catalogue filtrable par bibliothèque, famille et type ;
- lignes visuelles avec miniature TopSolid chargée uniquement lorsqu’elle devient visible ;
- sélection persistante multi-bibliothèques avec `Ctrl` et `Maj` ;
- panier escamotable, annulation de la dernière sélection et aperçu des fichiers liés ;
- synthèse exacte issue du plan de simulation avant l’ouverture du rapport HTML.

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
