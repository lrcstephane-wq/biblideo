# Architecture fonctionnelle V0

## Principe de sécurité

Les services de la V0 n'exposent aucune méthode de copie, déplacement, suppression ou compression. Ils produisent uniquement un `SimulationPlan`, ensuite transformé en rapport HTML.

## Flux package produit

1. Choix exclusif de la variante SC ou EP.
2. Lecture de la source paramétrée correspondante.
3. Reconstruction `bibliothèque > famille > type > composant`.
4. Sélection d'un ou plusieurs `.top` dans plusieurs emplacements.
5. Résolution des fichiers associés.
6. Ajout virtuel des icônes de chaque bibliothèque concernée.
7. Contrôles du modèle et du nom du package.
8. Affichage du rapport HTML sans écriture métier.

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
