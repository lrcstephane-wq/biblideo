# Architecture cible et trajectoire de migration

## Dossier partagé et racine par poste

Le dossier de paramétrage Nexus et les données nécessaires à la consultation sont partagés sur le réseau du bureau. Ce dossier est synchronisé automatiquement vers Nextcloud. Au bureau, chaque poste utilise le partage réseau. En déplacement, il utilise le dossier Nextcloud synchronisé localement, sans VPN. Toute référence persistée utilise un chemin relatif, identique dans les deux emplacements. Seule la racine, enregistrée localement sur le poste, change.

Ce fonctionnement couvre l’accès aux fichiers synchronisés. Le besoin de créer ou modifier les clients, licences et distributions depuis l’extérieur reste à préciser. Si la consultation suffit, le dossier partagé peut recevoir des exports cohérents et datés en lecture seule. Si l’écriture doit être possible à distance, il faudra un accès aux opérations du stockage de référence, à choisir séparément de la synchronisation Nextcloud.

## Stockage à valider

| Solution | Conséquences pour trois personnes |
| --- | --- |
| Fichier SQLite ou JSON actif répliqué par Nextcloud | Plusieurs copies modifiables, conflits et absence de transaction commune. L’écriture du fichier actif depuis plusieurs postes et sa synchronisation par Nextcloud sont incompatibles avec une référence unique. |
| Service .NET interne + SQLite sur disque local du serveur | Un seul service écrit. Transactions et sauvegardes cohérentes, installation limitée à un service et un fichier local. L’exploitation doit inclure suivi et restauration. C’est l’option recommandée si le bureau possède un hôte fiable et un volume local sauvegardé. |
| Service .NET interne + PostgreSQL | Transactions et gestion de charge plus extensibles. Exploitation d’un serveur de base supplémentaire. À privilégier si PostgreSQL est déjà exploité au bureau ou si la charge mesurée dépasse le cas d’usage actuel. |

Voir [les recommandations de SQLite pour les accès réseau](https://www.sqlite.org/useovernet.html), [l’API de sauvegarde cohérente](https://sqlite.org/backup.html) et [la gestion des conflits de fichiers Nextcloud](https://docs.nextcloud.com/server/stable/user_manual/fr/desktop/conflicts.html). La combinaison recommandée garde Nextcloud pour les documents et, si nécessaire, la diffusion de clichés cohérents et immuables autorisés. Le fichier actif reste local au service. Préférer des clichés locaux par utilisateur, produits à partir des vues autorisées du service, avec horodatage de la dernière récupération. Hors connexion, la lecture reste accessible et les écritures sont refusées. Une opération interrompue est vérifiée par son identifiant avant réessai en ligne. Aucun rattrapage automatique d’écritures hors ligne.

## Organisation

- **Client Windows WPF**. Navigation, présentation, accès aux chemins locaux et consultation du cache, commandes métier adressées au service.
- **Service interne .NET**. Une API et un processus, accès LAN au bureau, authentification et autorisations centralisées. Chaque changement sensible combine état métier et trace horodatée dans la même transaction. Contrôle de révision pour éviter les modifications concurrentes silencieuses. Permissions suivant le périmètre autorisé. Vérifier d’abord si l’infrastructure d’identité Windows de l’entreprise peut être réutilisée.
- **Métier commun**. Clients, applications et versions, commercial, droits techniques, publications, documents, partenaires et journal. Une fiche application définit le mode de licence, les offres et le profil de distribution. Les comportements spécialisés sont appelés aux points où une règle varie. Deux modules concrets suffisent au départ.
- **Biblidéo**. Conserver le scanner, les règles TopSolid, les services de packages, les rapports et le flux de licence reçu. Les catalogues suivent des fiches communes et la préparation SC/EP reste dans le module Biblidéo.
- **Administration d’Atlas**. Nexus suit les droits, les postes et les échéances, prépare les renouvellements et distribue les licences et les mises à jour Atlas. Atlas reste une application distincte avec son installation, Horizon, Forge et son catalogue. Nexus distribue les fichiers de mise à jour Atlas déjà préparés. Si Nexus déclenche l’émission de licences, il la délègue au mécanisme compatible Atlas avec accès restreint au signataire. La validation dans Atlas reste locale. Prévisualiser les lots de renouvellement et afficher un résultat par poste.
- **Documents et fichiers**. Enregistrer `identifiant de racine + chemin relatif`. Mapper la racine sur UNC au bureau ou sur le dossier Nextcloud local en déplacement. Refuser les sorties de racine et les redirections imprévues. Afficher « racine indisponible » ou « document introuvable » selon le cas. Le référentiel des documents conserve des liens. Les droits Nextcloud continuent d’être administrés séparément.

## Relations métier

| Entité | Relations et règles |
| --- | --- |
| Client et établissement | GUID stable, plusieurs établissements par entreprise, alias vers les dossiers d’origine, contacts et documents. |
| Application, offre, version | Identifiant technique stable, offres commerciales versionnées et profil de distribution. Biblidéo et Atlas sont des applications. |
| Contrat, ligne, abonnement | Un contrat par client, plusieurs contrats possibles. Chaque ligne lie une application et les droits couverts, avec prix HT, quantité, périodicité, remises et dates d’effet. Un historique des conditions conserve les versions précédentes. |
| Droit, licence et poste | Le droit appartient au titulaire. La licence Biblidéo est associée à un site. Une licence Atlas associe un droit à un poste et une échéance. Le site est facultatif. Un transfert clôt l’affectation précédente et ouvre la nouvelle, en conservant le droit et l’échéance. |
| Événements d’abonnement | Activation, résiliation programmée puis effective, suspension datée, reprise et archivage. Une suspension enregistre la reprise prévue, la décision de facturation et toute prolongation explicite. Les actions restent manuelles en V1. |
| Partenaire | Distributeur facultatif lié au produit acquis ou au contrat via une relation dédiée, avec utilisateur final et titulaire du droit identifiés séparément. |
| Document et événement | Le document référence un emplacement et ses objets métier. L’événement conserve acteur, date, opération, objet et anciennes/nouvelles valeurs pertinentes. |

**Revenus.** Calcul proposé à une date donnée sur les lignes récurrentes effectives, ramenées au mois, après application des remises commerciales. Une suspension avec maintien de facturation contribue au MRR actif. Si la facturation est interrompue, sa valeur contribue au MRR suspendu. L’ARR annualisé vaut douze fois le MRR actif. Conserver les valeurs contractuelles et la chronologie pour recalculer les indicateurs passés. Reste à valider la répartition et les arrondis des remises globales sur des lignes à périodicités mixtes.

**Publications.** Version en préparation, prête et publiée. La simulation fige l’inventaire par cible et les empreintes, qu’il s’agisse d’un pilote, d’une sélection ou de tous les clients éligibles. Après confirmation par un utilisateur autorisé, revalider l’éligibilité et les fichiers, verrouiller l’opération en cours, vérifier SHA‑256 et consigner les résultats par destinataire. Toute collision inattendue doit bloquer la cible et préserver ses fichiers. Conserver les anciennes versions et leurs manifestes. Une publication partielle reste identifiable et reprenable. Consigner « déposé et vérifié » lorsque la copie est contrôlée. En V1, la confirmation de synchronisation Nextcloud est indisponible depuis Nexus.

## Migration

1. Inventorier sur copies le registre clients et leurs GUID, la structure et les variantes SC/EP/SC+EP, les archives, les journaux, les dossiers de licences `.msg`, les packages et manifestes, ainsi que les liens documentaires existants. Régénérer l’index des licences si nécessaire. Rapprocher séparément les données Atlas et les contrats, sans exporter les clés ni les codes vers les journaux.
2. Préparer une simulation répétable. Produire les nombres par type, les correspondances par identifiant, les dossiers sans métadonnées, les doublons de GUID ou d’alias, les chemins introuvables et les champs inconnus. Conserver les GUID valides. Attribuer une nouvelle identité une seule fois pour les dossiers rapprochés sans GUID, via une table de correspondance persistante. Laisser les regroupements entreprise/site ambigus à valider.
3. Tester l’import isolé et répétable. Prendre des sauvegardes du registre, des réglages et de la base cible. Comparer les comptes, les références, les historiques et les statuts reconstruits. Essayer la restauration de la copie de travail. Ne déplacer ni document ni dossier client pendant l’import.
4. Après validation, organiser un gel court des écritures héritées, refaire le relevé de référence, puis vérifier la cohérence avant autorisation d’écrire dans Nexus. Éviter la double écriture. Les données anciennes restent consultables pendant la transition.
5. En cas de retour avant ouverture des écritures Nexus, remettre en service l’application d’origine et restaurer la cible si nécessaire. Après ouverture, arrêter les écritures, exporter et rapprocher les changements Nexus avant tout retour. Aucun écrasement automatique des registres ni des dossiers clients.

La décision de stockage, les modalités d’authentification, la grille finale des permissions et la bascule relèvent de la validation de Stéphane. Les étapes de l’import et du retour arrière restent à implémenter. Le premier lot est indépendant de ces choix.
