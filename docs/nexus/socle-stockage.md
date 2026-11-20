# Premier lot du stockage Nexus

Le service de développement `src/Nexus.Service` contient le premier parcours du registre clients. Il liste, crée et renomme des clients par API locale. Un client reçoit un identifiant stable à sa création. Les modifications demandent la révision lue par l’utilisateur. Si deux postes soumettent la même révision, une seule modification est acceptée et l’autre reçoit HTTP 409 et la fiche courante. L’état et son historique sont enregistrés dans la même transaction SQLite.

Le service crée automatiquement `clients.snapshot.json` dans son dossier de développement. La copie comprend la révision du registre, l’heure UTC de l’export et les fiches cohérentes à cette révision. Le fichier est d’abord écrit à côté de la destination puis remplacé en une opération. Ce premier lot vérifie que le fichier reste consultable sans le service. L’ouverture de la copie dans l’interface Windows et la synchronisation Nextcloud n’ont pas été intégrées dans ce lot.

## Exécuter les vérifications

Installer le SDK .NET 10 et Python 3, puis lancer depuis la racine du dépôt.

```powershell
dotnet build src/Nexus.Service -c Release
python tests/Nexus.Service.SmokeTests/test_registry.py
```

Le test démarre réellement le service sur la boucle locale avec une base temporaire. Il vérifie la validation du nom, la stabilité de l’identifiant, une modification concurrente, l’historique, l’export et la persistance après redémarrage. La base et la copie utilisées par le test sont fictives. Un test manuel du service est possible à l’adresse `http://127.0.0.1:5078/clients` après lancement de `dotnet run --project src/Nexus.Service`.

## Limites du lot

- Le service écoute uniquement sur la boucle locale. Le client Windows existant garde son propre parcours Biblidéo et cible toujours .NET 8. Le service utilise .NET 10 LTS et se compile séparément de `Biblideo.sln`.
- Les fiches de cette étape contiennent l’identifiant, le nom et la révision. L’identité de l’auteur, les permissions, les établissements, les licences et l’import Biblidéo seront raccordés après ce socle.
- L’accès distant authentifié et la publication des copies autorisées vers le dossier partagé restent à concevoir. Le fichier SQLite actif doit être installé sur un disque local du service. Les copies de consultation peuvent rejoindre le dossier Nextcloud lorsque leur périmètre et leurs permissions sont définis.
