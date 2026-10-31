# État du projet — CrowdSec Manager

État établi le 31 octobre 2026 à partir de `readme.md`, `workload.md`, du code source et de la documentation officielle CrowdSec.

## Implémenté dans le code

- [x] Application ASP.NET Core / Blazor Server ciblant .NET 8.
- [x] Authentification ASP.NET Identity avec stockage SQLite, rôle `Administrator` et création initiale du compte administrateur depuis `ADMIN_USER` / `ADMIN_PASSWORD`.
- [x] Tableau de bord avec statistiques CrowdSec, actions Hub et sauvegarde de configuration.
- [x] Gestion des décisions : liste, ajout, suppression et suppression globale.
- [x] Consultation et suppression des alertes.
- [x] Gestion Hub : liste, mise à jour, mise à niveau, installation et suppression d’éléments.
- [x] Mise à niveau ciblée d’un élément Hub avec la commande correspondant à son type.
- [x] Gestion des bouncers et consultation des machines.
- [x] Éditeur de configuration YAML avec liste blanche, validation YAML et sauvegarde tar.
- [x] Services d’intégration `cscli` et LAPI, carte des menaces avec SignalR, historique SQLite et géolocalisation MaxMind avec solution de repli.
- [x] Endpoint `/health` et fichiers de déploiement Quadlet / scripts d’installation présents.
- [x] Référence du projet corrigée dans la solution Visual Studio.
- [x] Dépendances NuGet MaxMind et SignalR corrigées/ajoutées ; erreurs C# de compilation corrigées.
- [x] Tests unitaires xUnit ajoutés pour la génération de commandes `cscli`, l’éditeur YAML et le bus de la carte.
- [x] L’éditeur détecte les YAML existants dans `acquis.d`, `notifications`, `parsers`, `scenarios`, `postoverflows`, `collections`, `contexts` et les dossiers AppSec ; il écrit de manière atomique et avertit avant d’éditer des éléments Hub.
- [x] Une action explicite permet d’envoyer `SIGHUP` au processus CrowdSec du même conteneur après des changements.
- [x] L’éditeur peut demander au moteur CrowdSec de valider la configuration sauvegardée avec `crowdsec -t -c <configDir>/config.yaml`.
- [x] Script d’hôte ajouté pour reconstruire avec `crowdsecurity/crowdsec:latest` (`podman build --pull=always`) et redémarrer le service Quadlet.
- [x] Client LAPI utilise les routes versionnées `/v1/decisions`; les alertes utilisent `cscli`, car une clé bouncer ne donne pas accès à la recherche d’alertes.
- [x] Suppression de décision par ID utilise `--id`; suppression d’alerte utilise `alerts delete --id`.
- [x] Ajout de décision choisit `--range` pour une valeur CIDR et `--ip` pour une IP seule.
- [x] La carte ne marque plus comme traitées les alertes qui dépassent la limite de 20 événements d’un cycle.

## Vérifié dans cet environnement

- [x] `dotnet build src/CrowdSecManager.sln` réussit sans erreur après restauration NuGet.
- [x] Le build s’effectue sans avertissement après correction de l’annotation nullable.
- [x] `dotnet test src/CrowdSecManager.sln --no-restore` : 23 tests réussis, 0 échoué, 0 ignoré ; projet applicatif compilé.
- [x] Dépôt Git local initialisé et premier commit créé (voir l’historique Git).

## À faire ou à valider en environnement de déploiement

- [ ] Exécuter `sudo bash deploy/update-image.sh` sur un hôte Linux Podman et valider l’image actualisée, le runtime .NET et l’état du Quadlet.
- [ ] Vérifier la syntaxe du script `deploy/update-image.sh` avec Bash sur Linux ; Bash/WSL n’est pas exécutable dans cet environnement Windows.
- [ ] Effectuer le smoke test du `workload.md` sur un hôte Linux avec systemd / Podman et vérifier l’état du service Quadlet.
- [ ] Valider l’amorçage de CrowdSec, la disponibilité LAPI et les commandes `cscli` avec une instance réelle.
- [ ] Tester les versions réelles de `cscli hub list -o json` et les commandes Hub install/remove/upgrade avec les types retournés par la version CrowdSec déployée.
- [ ] Tester les routes `/health` et `/v1/decisions` avec une vraie clé bouncer et vérifier la lecture de décisions.
- [ ] Tester la connexion, le rôle administrateur, les opérations CRUD et la persistance SQLite en exécution.
- [ ] Tester l’éditeur et la restauration de sauvegarde avec les fichiers de configuration CrowdSec réels.
- [ ] Tester la carte temps réel avec des alertes réelles et une base GeoLite2-City.mmdb valide.
- [x] Ajouter des tests SQLite pour la persistance de l’historique et pour l’amorçage/idempotence du rôle et du compte administrateur Identity.
- [x] Tester le traitement des lots de `ThreatFeedService` : persistance SQLite, diffusion, déduplication et traitement par fenêtres de 20 alertes.
- [ ] Tester l’exécution complète du polling `ThreatFeedService` dans l’hôte (récupération LAPI/CLI, cycle périodique et annulation).
- [x] Persister les clés ASP.NET Data Protection dans le répertoire applicatif afin que les cookies d’authentification survivent au redémarrage ; chemin surchargeable avec `DataProtection__KeysPath`.
- [x] Corriger l’URL de développement documentée : HTTP `localhost:57938` en local, `8081` dans le conteneur.
- [x] Ajouter une procédure reproductible de mise à jour du moteur CrowdSec via reconstruction de l’image et redémarrage Quadlet ; l’application Web ne remplace pas la mise à jour privilégiée de l’hôte.
- [ ] Valider sur l’hôte Linux que `crowdsec -t` accepte la configuration de déploiement et présente des erreurs compréhensibles dans l’éditeur.
- [ ] Vérification locale Windows tentée sur CrowdSec 1.6 : le binaire n’a pas pu lire `local_api_credentials.yaml` ni tourner son log (`Access is denied`), y compris après relance ; ce résultat ne valide pas la configuration.
- [ ] Tester le rechargement `SIGHUP` sur le conteneur autonome et documenter/valider l’action de reload manuelle en mode side-by-side.
- [x] Fermer le hub SignalR `/hubs/threatmap` anonyme ; la carte reçoit les événements via un bus interne et les circuits Blazor authentifiés.
- [x] Documenter le fonctionnement et les limites de la carte : interrogation périodique (8 s par défaut), géolocalisation approximative sans GeoLite2 et rendu original inspiré des cartes SOC.
- [ ] Décider si les fonctions de consultation/édition supplémentaires de `cscli` (AppSec, allowlists, simulation, machines, credentials, etc.) entrent dans le périmètre produit ; elles ne sont pas toutes exposées dans l’interface.

## Comparaison avec CrowdSec officiel

La référence CrowdSec distingue les opérations `cscli` locales, l’API LAPI et les clés bouncer. Une clé bouncer est en lecture seule sur les décisions ; la consultation des alertes relève de l’authentification machine. Les éléments existants sont donc reliés à `cscli` pour les alertes, et à `/v1/decisions` pour la lecture LAPI des décisions.

| Besoin | État constaté |
| --- | --- |
| Actualiser l’index Hub (`cscli hub update`) | Implémenté ; reste à valider sur CrowdSec réel. |
| Installer/supprimer et mettre à niveau les collections, parsers, scénarios et postoverflows | Implémenté via `cscli`; test runtime restant. |
| Mise à jour du moteur CrowdSec lui-même | Script d’hôte qui tire la base officielle, reconstruit l’image et redémarre Quadlet ; exécution Linux à valider. |
| Éditer le YAML | Implémenté pour les fichiers YAML des chemins configurés ; écriture atomique, validation syntaxique, vérification optionnelle avec `crowdsec -t` et avertissement Hub. Exécution avec moteur réel à valider. |
| Ajouter/supprimer une décision IP | Implémenté via `cscli`; suppression par ID et CIDR corrigés, test runtime restant. |
| Afficher les alertes | Implémenté via `cscli` (les bouncers n’ont pas les droits LAPI de recherche d’alertes). |
| Carte animée temps réel | Implémentée avec polling, SignalR, canvas et GeoIP optionnel ; pas de flux natif push CrowdSec, ni d’essai live ici. |
| Menaces réellement bloquées sur le réseau | Dépend d’un bouncer CrowdSec installé et configuré ; une décision seule ne bloque pas le trafic. |

## Limites connues à suivre

- La compilation confirme la cohérence de la solution, pas le fonctionnement de l’application avec un CrowdSec réel.
- Sans base GeoLite2, la carte utilise la localisation de repli décrite dans le workload.
- La base applicative est créée avec `EnsureCreated()` ; le README recommande des migrations EF Core avant une évolution du schéma en production.
- Les instructions de restauration NuGet nécessitent l’accès à `api.nuget.org`.
