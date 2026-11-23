# Licences Atlas dans Idéo Nexus

La création de clients passe par l’écran **Clients** et reprend le fonctionnement Biblidéo avec création des dossiers. Cette même fiche indique les mises à jour Biblidéo et les postes Atlas, leur offre, leur échéance et leur statut. Le registre Atlas du NAS enregistre les licences et leurs événements par identifiant de fiche. Les coordonnées et les dossiers clients restent gérés dans Biblidéo.

## Générer et distribuer une licence

1. Dans **Licences Atlas**, choisir une fiche client existante, saisir le code du poste affiché par Atlas et choisir Standard ou Créateur. La date de fin est inclusive.
2. Nexus recherche la clé privée installée par Atlas sur la session Windows. Le bouton **Choisir la clé Idéo** accepte un PEM déjà conservé sur le poste. La signature est contrôlée avec la clé publique d’Atlas. La clé privée reste sur le poste.
3. Confirmer la génération. La dernière licence enregistrée peut être copiée sous forme de code ou exportée en `Licence.fautpastoucher`.

Le renouvellement prolonge l’échéance sans changer de poste. Le passage de Standard à Créateur conserve le poste et la date. Le transfert change de poste en préservant le droit et la date. Si l’ancien poste reste hors ligne, la licence qu’il possède reste utilisable jusqu’à sa propre échéance.

Une ancienne licence créée pour une fiche Nexus séparée est signalée **Fiche à rattacher**. Choisir la fiche Biblidéo, puis **Rattacher à une fiche** pour signer le changement de bénéficiaire avec le même identifiant de licence, poste, offre et échéance. Le changement est inscrit dans l’historique. Le registre des licences et la copie cloud suivent les réglages du [registre NAS](registre-nas.md). L’accès administrateur Idéo et Forge reste géré dans Atlas.

## Recette automatisée

```powershell
dotnet build Biblideo.sln -c Release
dotnet run --project tests/Biblideo.Core.SmokeTests -c Release --no-build
```

Les tests utilisent des fiches et une paire de clés fictives. Ils vérifient la source client commune, la reprise d’une licence du format précédent, son rattachement, la concurrence et la lecture de la copie cloud.
