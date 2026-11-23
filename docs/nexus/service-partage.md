# Service Nexus partagé

Le mode partagé démarre une API HTTPS authentifiée sur le serveur Nexus. Les postes Windows renseignent l’adresse et leur jeton personnel dans **Connexion Nexus**. Chaque jeton est attribué à une seule personne. Il reste en mémoire sur son poste pendant la session.

| Rôle dans Nexus | Clients Nexus | Licences Atlas |
| --- | --- | --- |
| Lecteur (`Reader`) | Consultation | Consultation et historique |
| Éditeur (`Editor`) | Consultation, création et modification | Consultation et historique |
| Administrateur (`Administrator`) | Accès éditeur et import Biblidéo | Modification et export. La signature demande aussi la clé Atlas locale. |

Le serveur applique ces permissions à chaque requête. Les parcours historiques Biblidéo continuent d’utiliser leurs fichiers et leurs droits existants. Forge et Horizon continuent de gérer leurs propres accès.

## Configuration côté serveur

L’instance partagée et la base de développement sont séparées. Configurer les variables suivantes sur le compte de service.

| Variable | Valeur |
| --- | --- |
| `IDEO_NEXUS_ACCESS_FILE` | Chemin vers le fichier JSON des comptes, accessible au compte de service |
| `IDEO_NEXUS_DATA_DIRECTORY` | Répertoire de données sur un disque local du serveur |
| `IDEO_NEXUS_TLS_PFX` | Certificat PFX et clé privée TLS correspondant au nom DNS utilisé par l’équipe |
| `IDEO_NEXUS_TLS_PASSWORD` | Mot de passe du PFX, si nécessaire |
| `IDEO_NEXUS_PORT` | Port HTTPS, `7443` par défaut |

Le service doit être installé sur un hôte accessible par les postes de l’équipe et présenter un certificat que Windows reconnaît. Le lancement sans fichier de comptes reste réservé au développement sur la boucle locale. Ne pas relayer le port de développement sur le réseau.

Créer un jeton aléatoire de 32 octets par compte, sous forme de 64 caractères hexadécimaux. Le serveur reçoit uniquement le SHA‑256 du texte exact du jeton. Le titulaire reçoit le jeton lui-même par un canal privé. Le JSON contient `accounts`, un tableau d’objets avec les champs `name`, `role` et `tokenSha256`. Inclure au moins un administrateur. Pour changer ou retirer un accès, modifier le fichier puis redémarrer le service. Le fichier de comptes et le PFX doivent rester hors du dépôt.

Le fichier SQLite actif est `nexus.sqlite`. Le mode partagé évite les exports clients ouverts sur le serveur. Le poste conserve la dernière copie clients obtenue via l’API, séparée par adresse et jeton, pour une consultation en lecture seule si le service est inaccessible. Les écrans Atlas restent indisponibles hors connexion. Pour travailler sur les dossiers Biblidéo avec leurs paramètres usuels, compiler le client Windows sans l’option `NexusPreview`. Le mode local de développement du client normal s’active explicitement avec `IDEO_NEXUS_LOCAL_DEV=1`.

## Validation avant remplacement de l’installation

Installer le service sur l’hôte retenu, configurer HTTPS et les comptes, puis valider une connexion depuis Windows. Vérifier la consultation, l’import sur une copie, l’export Atlas, le lancement de Biblidéo et le retour à l’exécutable précédent. Le test automatisé HTTPS démarre un serveur temporaire avec certificat et jetons fictifs et vérifie les refus par rôle.

```powershell
dotnet build src/Nexus.Service -c Release
python tests/Nexus.Service.SmokeTests/test_shared_access.py
```

Le test demande Python et OpenSSL. Aucun compte réel ni aucune clé Atlas de production ne sont nécessaires.
