# Licences Atlas dans Idéo Nexus

L’espace Licences Atlas gère les droits par poste. La vue Licences Biblidéo reste accessible pour le flux Outlook existant. Atlas continue de gérer l’accès Idéo, Forge et Horizon. Le profil Créateur est un droit de licence Atlas et ne confère aucun accès administrateur.

## Essai local

1. Démarrer le service de développement avec le SDK .NET 10 et la commande `dotnet run --project src/Nexus.Service`.
2. Ouvrir Idéo Nexus et créer une fiche fictive dans **Clients Nexus**. En cas de coupure du service, le registre relit la dernière copie locale datée en lecture seule si une consultation a déjà réussi.
3. Dans **Licences Atlas**, choisir le client, saisir le code poste affiché par Atlas, choisir Standard ou Créateur et saisir la date de fin inclusive. Confirmer les informations avant de signer.
4. Au premier usage dans la vue, sélectionner le fichier PEM privé Atlas déjà conservé sur le poste autorisé. Sa signature est vérifiée contre la clé publique embarquée dans Atlas. Nexus n’enregistre ni ne téléverse la clé privée.
5. Sélectionner une licence dans la liste pour exporter `Licence.fautpastoucher` vers un dossier choisi. Le fichier exporté correspond à la dernière version enregistrée. La signature est également vérifiée par le service et lors de l’export.

Les autres opérations disponibles sont le renouvellement à poste identique, le passage de Standard à Créateur et le changement de poste. Le changement de poste conserve la date et le droit et inscrit l’ancien identifiant dans l’historique. Si l’ancien poste est hors ligne, sa précédente licence reste valable jusqu’à sa propre échéance. L’historique de Nexus ne constitue pas une révocation dans Atlas.

## Vérification automatisée

```powershell
dotnet run --project tests/Nexus.Service.SmokeTests/Nexus.Atlas.SmokeTests.csproj -c Release
```

Le test utilise une paire ECDSA et une base SQLite temporaires. Il vérifie les deux offres, le rejet d’une autre clé, les règles de renouvellement, de changement de poste, de conflit et d’historisation. Aucun fichier de clé réel n’est requis.

## Limites avant usage partagé

Le registre fonctionne avec le service de test local sur la boucle locale. Les comptes et autorisations Nexus, l’accès distant, la migration complète des dossiers et historiques, la distribution vers Nextcloud et les renouvellements par lot restent à développer. L’export permet la remise manuelle du fichier. L’adresse du service de développement est `127.0.0.1:5078`. L’utilisation de la clé de production sur une machine de test peut générer un vrai droit Atlas. Pour une simple recette d’interface, conserver des fiches fictives et éviter de signer des licences avec la clé réelle.

## Reprendre les identifiants Biblidéo dans la version normale

Dans **Clients Nexus**, l’action **Importer les fiches Biblidéo** lit le registre déjà configuré et propose de reprendre le nom et l’identifiant des fiches actives. L’import est explicite. Les entrées archivées ou incomplètes sont ignorées. Un identifiant déjà présent dans Nexus est conservé, y compris après un changement de nom. Les dossiers, abonnements, licences, contacts et historiques Biblidéo restent dans leur flux d’origine.
