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
- [x] Client LAPI utilise les routes versionnées `/v1/decisions`; les alertes utilisent `cscli`, car une clé bouncer ne donne pas accès à la recherche d’alertes.
- [x] Suppression de décision par ID utilise `--id`; suppression d’alerte utilise `alerts delete --id`.
- [x] Ajout de décision choisit `--range` pour une valeur CIDR et `--ip` pour une IP seule.
- [x] La carte ne marque plus comme traitées les alertes qui dépassent la limite de 20 événements d’un cycle.

## Vérifié dans cet environnement

- [x] `dotnet build src/CrowdSecManager.sln` réussit sans erreur après restauration NuGet.
- [x] Le build s’effectue sans avertissement après correction de l’annotation nullable.
- [x] Dépôt Git local initialisé et premier commit créé (voir l’historique Git).

## À faire ou à valider en environnement de déploiement

- [ ] Construire l’image avec Podman et valider l’installation du runtime .NET dans l’image CrowdSec.
- [ ] Effectuer le smoke test du `workload.md` sur un hôte Linux avec systemd / Podman et vérifier l’état du service Quadlet.
- [ ] Valider l’amorçage de CrowdSec, la disponibilité LAPI et les commandes `cscli` avec une instance réelle.
- [ ] Tester les versions réelles de `cscli hub list -o json` et les commandes Hub install/remove/upgrade avec les types retournés par la version CrowdSec déployée.
- [ ] Tester les routes `/health` et `/v1/decisions` avec une vraie clé bouncer et vérifier la lecture de décisions.
- [ ] Tester la connexion, le rôle administrateur, les opérations CRUD et la persistance SQLite en exécution.
- [ ] Tester l’éditeur et la restauration de sauvegarde avec les fichiers de configuration CrowdSec réels.
- [ ] Tester la carte temps réel avec des alertes réelles et une base GeoLite2-City.mmdb valide.
- [ ] Ajouter une suite de tests automatisés ; aucune suite de tests n’est présente dans le dépôt à ce jour.
- [ ] Examiner l’incohérence documentaire sur le port HTTP local : le README indique `localhost:5000`, tandis que le conteneur écoute sur `8081`.
- [ ] Implémenter la mise à jour du moteur CrowdSec (paquet/binaire ou image conteneur). L’interface actuelle ne met à jour que l’index et les éléments du Hub.
- [ ] Élargir et tester l’éditeur YAML : la liste blanche ne contient qu’un sous-ensemble de fichiers CrowdSec et la validation actuelle vérifie la syntaxe YAML, pas le schéma sémantique CrowdSec.
- [ ] Ajouter une action explicite de rechargement/redémarrage CrowdSec après modification YAML ou mise à niveau Hub, et vérifier la bonne commande selon systemd/Podman.
- [ ] Protéger le hub SignalR `/hubs/threatmap` avec l’authentification appropriée. La page Blazor est protégée, mais l’endpoint Hub n’a pas `[Authorize]` et diffuse les événements aux clients connectés.
- [ ] Compléter les détails de la carte : le flux est interrogé périodiquement (8 s par défaut), la géolocalisation approximative est utilisée sans GeoLite2, et le rendu n’est pas une copie de la Threat Map Check Point.
- [ ] Décider si les fonctions de consultation/édition supplémentaires de `cscli` (AppSec, allowlists, simulation, machines, credentials, etc.) entrent dans le périmètre produit ; elles ne sont pas toutes exposées dans l’interface.

## Comparaison avec CrowdSec officiel

La référence CrowdSec distingue les opérations `cscli` locales, l’API LAPI et les clés bouncer. Une clé bouncer est en lecture seule sur les décisions ; la consultation des alertes relève de l’authentification machine. Les éléments existants sont donc reliés à `cscli` pour les alertes, et à `/v1/decisions` pour la lecture LAPI des décisions.

| Besoin | État constaté |
| --- | --- |
| Actualiser l’index Hub (`cscli hub update`) | Implémenté ; reste à valider sur CrowdSec réel. |
| Installer/supprimer et mettre à niveau les collections, parsers, scénarios et postoverflows | Implémenté via `cscli`; test runtime restant. |
| Mise à jour automatique du moteur CrowdSec lui-même | Non implémentée ; ajoutée à faire. |
| Éditer le YAML | Implémenté pour les fichiers de la liste blanche ; validation syntaxique seulement et application après reload/restart. |
| Ajouter/supprimer une décision IP | Implémenté via `cscli`; suppression par ID et CIDR corrigés, test runtime restant. |
| Afficher les alertes | Implémenté via `cscli` (les bouncers n’ont pas les droits LAPI de recherche d’alertes). |
| Carte animée temps réel | Implémentée avec polling, SignalR, canvas et GeoIP optionnel ; pas de flux natif push CrowdSec, ni d’essai live ici. |
| Menaces réellement bloquées sur le réseau | Dépend d’un bouncer CrowdSec installé et configuré ; une décision seule ne bloque pas le trafic. |

## Limites connues à suivre

- La compilation confirme la cohérence de la solution, pas le fonctionnement de l’application avec un CrowdSec réel.
- Sans base GeoLite2, la carte utilise la localisation de repli décrite dans le workload.
- La base applicative est créée avec `EnsureCreated()` ; le README recommande des migrations EF Core avant une évolution du schéma en production.
- Les instructions de restauration NuGet nécessitent l’accès à `api.nuget.org`.
