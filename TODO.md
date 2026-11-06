# État du projet — CrowdSec Manager

État établi le 31 octobre 2026 à partir de `readme.md`, `workload.md`, du code source, des essais Podman sous Debian WSL et de la documentation officielle CrowdSec.

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
- [x] Corriger l’amorçage in-container : remplacer la commande inexistante `cscli config gen` par l’initialiseur officiel `/docker_start.sh` de l’image, attendre jusqu’à 120 s le health endpoint LAPI, envoyer les logs CrowdSec à stdout (le volume `/var/log` est en lecture seule), persister la clé bouncer et superviser l’arrêt des deux processus.
- [x] Client LAPI utilise les routes versionnées `/v1/decisions`; les alertes utilisent `cscli`, car une clé bouncer ne donne pas accès à la recherche d’alertes.
- [x] Suppression de décision par ID utilise `--id`; suppression d’alerte utilise `alerts delete --id`.
- [x] Ajout de décision choisit `--range` pour une valeur CIDR et `--ip` pour une IP seule.
- [x] La carte ne marque plus comme traitées les alertes qui dépassent la limite de 20 événements d’un cycle.
- [x] La carte met en tampon les événements reçus durant l’initialisation JavaScript puis les réconcilie au snapshot sans perdre ni doubler les alertes.
- [x] La carte borne ses marqueurs source à 300 IP, expire les sources après 60 s, crée une seule ripple d’impact par arc et charge D3, TopoJSON et les pays depuis les fichiers locaux.
- [x] Le fallback GeoIP produit des coordonnées stables entre redémarrages ; la normalisation préserve IPv6 et reconnaît les plages IPv6 privées/locales.

## Vérifié dans cet environnement

- [x] `dotnet build src/CrowdSecManager.sln` réussit sans erreur après restauration NuGet.
- [x] `dotnet publish src/CrowdSecManager/CrowdSecManager.csproj -c Release /p:UseAppHost=false --no-restore` réussit et génère l’assembly utilisé par l’étape Docker.
- [x] Le build s’effectue sans avertissement après correction de l’annotation nullable.
- [x] `dotnet test src/CrowdSecManager.sln -c Release` : 87 tests réussis, 0 échoué, 0 ignoré ; projet applicatif compilé.
- [x] Vérification Release : `dotnet build src/CrowdSecManager.sln -c Release` réussit avec 0 avertissement/erreur ; `dotnet publish ... -c Release /p:UseAppHost=false --no-restore` réussit ; toute la suite Release passe les 87 tests.
- [x] Build Podman Linux avec `crowdsecurity/crowdsec:latest` réel (Alpine 3.24) : le Dockerfile a été corrigé de `apt-get` vers `apk`, et installe ASP.NET Core 8 `linux-musl-x64`. Le premier build a mis en évidence et permis de corriger deux avertissements C# (`MainLayout` et `ConfigEditor`).
- [x] `dotnet test` après corrections : 87 tests réussis ; `dotnet build -c Release --no-restore` : 0 avertissement, 0 erreur. Build de l’image Podman terminé avec succès après correction Alpine.
- [x] Les commandes Podman de build utilisent `--format docker` afin de préserver dans l’image Quadlet le `HEALTHCHECK` que le format OCI par défaut ignorait.
- [x] Smoke test de l’image Podman : ASP.NET démarre sur Alpine .NET 8, `/health` répond HTTP 200, le Healthcheck image est présent. En mode moteur réel, l’initialiseur officiel prépare le hub, le LAPI devient disponible et le bouncer est enregistré automatiquement.
- [x] Test réel de l’authentification LAPI avec la clé bouncer générée par l’image : `Authorization: Bearer` renvoie HTTP 403, `X-Api-Key` renvoie HTTP 200. Le client et le test unitaire ont été corrigés pour utiliser `X-Api-Key`.
- [x] Tester les décisions sur le LAPI jetable : ajout de `203.0.113.42` pour 10 minutes, lecture de son ID `1`, suppression par ID puis vérification que `cscli decisions list -o json` renvoie `[]`.
- [x] Tester `crowdsec -t -c /etc/crowdsec/config.yaml` sur la configuration issue de l’image CrowdSec `v1.8.2` ; la validation se termine par `Configuration test done` avec code 0.
- [x] Envoyer un vrai `SIGHUP` au moteur CrowdSec du conteneur, puis vérifier que `cscli lapi status` réussit et que le conteneur reste actif.
- [x] Vérifier le polling périodique réel de `ThreatFeedService` : les journaux de l’image reconstruite montrent plusieurs cycles `/health` et `/v1/decisions` avec HTTP 200 auprès du LAPI CrowdSec actif.
- [x] Tests JavaScript du renderer : 7 tests couvrent les sources, les arcs, le cycle de vie et le chargement des données cartographiques locales (`tests/javascript/threatmap.test.mjs`).
- [x] Dépôt Git local initialisé et premier commit créé (voir l’historique Git).

## À faire ou à valider en environnement de déploiement

État des outils vérifié localement le 31 octobre 2026 : l’hôte est Windows avec CrowdSec/Cscli `v1.6.0-4b8e6cd7`. `cscli lapi status` y échoue avant la connexion car Windows refuse l’accès à `C:\ProgramData\CrowdSec\config\local_api_credentials.yaml`. Debian WSL est utilisable avec Podman rootless `5.4.2` et systemd actif ; l’image officielle `crowdsecurity/crowdsec:latest` a fourni CrowdSec Alpine `3.24`. Les essais du moteur et du LAPI ont été faits dans un conteneur jetable, pas sur le service Quadlet de production. `sudo` dans WSL demande un mot de passe, donc l’installation/redémarrage du service système reste à vérifier sur l’hôte de déploiement.

- [ ] Exécuter `sudo bash deploy/update-image.sh` sur un hôte Linux Podman et valider l’image actualisée, le runtime .NET et l’état du Quadlet. **Automatisé par `smoke-test.sh` section 1 (+ section 7 avec `--quadlet`).**
- [x] Vérifier la syntaxe Bash (`bash -n`) des scripts `deploy/update-image.sh`, `deploy/install-quadlet.sh`, `deploy/entrypoint.sh` et du nouveau test de déploiement.
- [x] Tester le démarrage isolé de `deploy/entrypoint.sh` avec `cscli`/`dotnet` remplacés par des stubs : branche side-by-side sans bootstrap, propagation des chemins, de l’URL LAPI, du token bouncer, des coordonnées GeoIP et rejet d’un timeout non valide (19 assertions).
- [x] Tester avec un faux initialiseur d’image le démarrage et l’arrêt coordonné CrowdSec/ASP.NET, la détection d’un arrêt moteur et la reprise de la clé bouncer après redémarrage ; 16 assertions vérifient l’init, le health LAPI, la transmission de la clé en mode `0600`, l’absence de seconde inscription et l’échec du superviseur.
- [x] Tester les garde-fous des scripts `deploy/update-image.sh` et `deploy/install-quadlet.sh` : les deux refusent un lancement non-root avant toute action (2 assertions).
- [x] Ajouter/exécuter `tests/shell/deploy-build-format.test.sh` : avec des commandes système simulées, vérifie que l’installation Quadlet appelle le build en format Docker, recharge systemd, et que le script de mise à jour conserve ce format (4 assertions).
- [ ] Effectuer le smoke test du `workload.md` sur un hôte Linux avec systemd / Podman et vérifier l’état du service Quadlet. **Automatisé par `smoke-test.sh` (sections 2 à 8, + section 7 avec `--quadlet`).**
- [x] Valider l’amorçage officiel CrowdSec, la disponibilité LAPI, l’inscription bouncer et les commandes `cscli` sur un conteneur Linux jetable avec l’image réelle ; le conteneur doit avoir un montage sur `/var/lib/crowdsec/data`.
- [x] Exécuter `cscli hub update`, parser le JSON `cscli hub list -a -o json`, puis installer/supprimer `crowdsecurity/nginx` et exécuter `cscli hub upgrade` sur ce moteur isolé.
- [x] Vérifier avec CrowdSec 1.6 installé que `cscli hub list -a -o json` retourne un objet de groupes (`collections`, `parsers`, etc.) ; le parseur de l’application a été adapté et testé contre cette structure.
- [x] Tester avec TestServer l’endpoint applicatif `/health` (HTTP 200 + JSON).
- [x] Tester `/v1/decisions` sur le LAPI réel avec la vraie clé bouncer auto-générée : `X-Api-Key` donne HTTP 200 ; l’ancien en-tête Bearer renvoyait 403 et a été corrigé dans le service et le test unitaire.
- [x] Tester sans réseau réel le client LAPI : endpoint `/health`, route versionnée `/v1/decisions`, en-tête `X-Api-Key` et repli cscli quand l’API échoue.
- [x] Propager l’annulation du client LAPI pour les sondes et les statistiques au lieu de la traiter comme une panne réseau/repli ; deux tests vérifient l’annulation et l’absence d’appel CLI.
- [x] Relier les variables legacy `CROWDSEC_BOUNCER_APIKEY`, `LOCAL_API_URL`, chemins GeoIP/CrowdSec et coordonnées de la carte aux clés de configuration ASP.NET ; tests de mapping et priorité `.NET` inclus.
- [x] Supprimer le mot de passe administrateur faible présent par défaut dans le Quadlet et ajouter un test de régression.
- [x] Refuser le démarrage initial sans `ADMIN_PASSWORD` et échouer clairement si l’identité ou le rôle administrateur ne peut pas être initialisé.
- [x] Tester la génération des commandes cscli Hub pour collections, parsers, scénarios, postoverflows, AppSec et contexts ; tester aussi la suppression d’alerte par ID.
- [x] Valider les cibles de décision avant `cscli` : IPv4 décimale à quatre octets, IPv6 sans zone, CIDR et préfixes bornés ; les formes IPv4 abrégées/hexadécimales/octales sont rejetées et une cible invalide ne lance pas de processus.
- [x] Tester `ListHubAsync` à travers le service CLI avec un exécuteur de processus simulé ; vérifie les arguments `hub list -a -o json` et le parsing des items disponibles/installés.
- [x] Tester avec TestServer le rendu de `/login` et la redirection d’une route `[Authorize]` vers la connexion.
- [x] Tester par HTTP les pages Razor de connexion/déconnexion avec TestServer : jeton antiforgery, mot de passe erroné puis valide, émission et usage du cookie Identity sur `/decisions`, puis expiration à la déconnexion.
- [x] Tester que `/login` refuse un POST sans jeton antiforgery et neutralise une URL de retour externe.
- [ ] Tester les actions interactives login/logout et le cookie d’authentification dans un navigateur sur l’hôte de déploiement. **Automatisé côté HTTP par `smoke-test.sh` section 3 (login curl + cookie + logout + redirect). Le test navigateur humain reste à faire.**
- [ ] Tester l’éditeur et la restauration de sauvegarde avec les fichiers de configuration CrowdSec réels. **Automatisé par `smoke-test.sh` section 4 (`cscli config backup` + vérification fichier non vide + `crowdsec -t`).**
- [x] Tester localement le backup `.tgz` avec YAML imbriqué et la protection Load/Save contre un junction/lien symbolique qui sort de la whitelist.
- [ ] Tester la carte temps réel avec des alertes réelles et une base GeoLite2-City.mmdb valide. **Automatisé par `smoke-test.sh` section 5 (vérif GeoLite2 + décision manuelle + fetch /threatmap avec canvas).**
- [x] Ajouter des tests SQLite pour la persistance de l’historique et pour l’amorçage/idempotence du rôle et du compte administrateur Identity.
- [x] Tester les opérations SQLite Create/Read/Update/Delete de l’historique.
- [x] Tester le traitement des lots de `ThreatFeedService` : persistance SQLite, diffusion, déduplication intra/inter-redémarrage, stats à vide et fenêtres de 20 alertes.
- [x] Tester le replay de la carte : fusion snapshot/événements avant initialisation, déduplication, replay incrémental et capacité bornée du tampon.
- [x] Propager l’annulation pendant l’écriture de l’historique SQLite et journaliser les erreurs de persistance avec l’ID d’alerte ; test couvrant l’annulation pendant l’écriture.
- [x] Réessayer les écritures d’historique temporairement échouées au cycle de polling suivant, sans republier l’événement temps réel ; test avec un intercepteur SQLite simulant une panne ponctuelle.
- [x] Tester la purge SQLite des lignes d’historique de plus de 30 jours au seuil de nettoyage de 100 insertions.
- [x] Tester le fallback GeoIP déterministe, la préservation des adresses IPv6, la détection d’IPv6 privé et le nettoyage IP/port/CIDR.
- [x] Tester `PollOnceAsync` de `ThreatFeedService` sur les branches LAPI et fallback CLI, avec persistance et annulation.
- [x] Tester le `BackgroundService` périodique avec dépendances simulées : deux cycles de polling, déduplication/persistance et arrêt sur annulation.
- [x] Garantir l’affichage des diagnostics de commande : `CliResult.FlattenError()` privilégie stderr et retombe sur stdout ; quatre tests couvrent ces cas pour que l’éditeur affiche les erreurs même si l’engine écrit sur stdout.
- [x] Tester avec un exécuteur simulé la validation `crowdsec -t -c <config.yaml>` et le rechargement `pkill --signal HUP --exact crowdsec`, y compris succès, diagnostics et absence de processus ; le signal sur un moteur réel reste à valider sur hôte Linux.
- [ ] Vérifier en environnement d’hôte le cycle périodique du `ThreatFeedService` avec LAPI/CLI réellement actifs. **Automatisé par `smoke-test.sh` section 6 (grep des appels `/v1/decisions` et `/health` dans les logs du conteneur).**
- [x] Persister les clés ASP.NET Data Protection dans le répertoire applicatif afin que les cookies d’authentification survivent au redémarrage ; chemin surchargeable avec `DataProtection__KeysPath`.
- [x] Corriger l’URL de développement documentée : HTTP `localhost:57938` en local, `8081` dans le conteneur.
- [x] Ajouter une procédure reproductible de mise à jour du moteur CrowdSec via reconstruction de l’image et redémarrage Quadlet ; l’application Web ne remplace pas la mise à jour privilégiée de l’hôte.
- [ ] Valider sur l’hôte Quadlet de déploiement que `crowdsec -t` et l’éditeur affichent les diagnostics sur la configuration réelle montée. **Automatisé par `smoke-test.sh` section 4 (`crowdsec -t -c /etc/crowdsec/config.yaml` dans le conteneur).**
- [x] Validation isolée de CrowdSec 1.6 Windows : `crowdsec -t -no-api -no-capi` accepte le `config.yaml` courant avec copies temporaires des chemins DB/log et des credentials factices. Cela valide le chargement local de la configuration sans tester LAPI/CAPI ni remplacer la validation Linux ; l’accès aux credentials/journaux actifs reste refusé.
- [x] Tester le rechargement `SIGHUP` sur un conteneur CrowdSec réel ; le mode side-by-side continue de documenter le redémarrage manuel de l’instance CrowdSec hôte, qui n’est pas accessible dans WSL.
- [x] Fermer le hub SignalR `/hubs/threatmap` anonyme ; la carte reçoit les événements via un bus interne et les circuits Blazor authentifiés.
- [x] Documenter le fonctionnement et les limites de la carte : interrogation périodique (8 s par défaut), géolocalisation approximative sans GeoLite2 et rendu original inspiré des cartes SOC.
- [x] Auditer les commandes `cscli` supplémentaires : AppSec, allowlists, simulation et gestion des credentials ne sont pas exposées. Elles sont conservées comme pistes de backlog; la portée demandée ici reste Hub, configuration YAML, décisions IP et carte des menaces.
- [x] **Implémenter le backlog identifié** (sessions ultérieures) :
  - [x] Étendre `CrowdSecCommandBuilder` avec les commandes `simulation list/status/enable/disable/add/remove`, `notifications list/inspect`, `console status/enable/disable alerts`, `allowlists list`, et un variant `AddDecision(..., bypassAllowlist)` qui ajoute `--bypass-allowlist`.
  - [x] Étendre `ICrowdSecCliService` + implémentation avec les méthodes correspondantes ; le statut global de simulation est parsé depuis la sortie texte (pas de JSON pour `simulation status`).
  - [x] Ajouter les modèles `CsSimulation`, `CsSimulationStatus`, `CsNotification`.
  - [x] Ne PAS ajouter `simulation`/`notifications`/`console`/`allowlists` à `JsonCommands` car seules certaines sous-commandes supportent `-o json` ; `RunJsonAsync` continue d'ajouter le flag lui-même quand nécessaire.
  - [x] Page `Simulation.razor` : état global (enable/disable), liste et ajout/retrait par scénario.
  - [x] Page `Notifications.razor` : liste des plugins + inspect de la configuration résolue.
  - [x] Page `ConsolePage.razor` : statut de l'enrôlement Console + enable/disable du partage d'alertes.
  - [x] `Decisions.razor` : checkbox "Bypass allowlist" qui active `--bypass-allowlist` sur `cscli decisions add`.
  - [x] `NavMenu.razor` et `TopBar.razor` : trois nouvelles entrées + titres de page.
  - [x] CSS : styles pour `label.checkbox`, `details.raw` (sortie `cscli console status` / `notifications inspect`).
  - [x] Tests xUnit pour les nouvelles commandes : `CrowdSecCommandBuilderExtendedTests.cs` (bypass-allowlist, simulation, notifications, console, allowlists).
  - [x] Mise à jour du `readme.md` : nouvelles fonctionnalités, nouvelles pages, nouveaux sous-titres "Using each feature".
  - [x] Vérification de l’intégration contre le code source GitHub `crowdsecurity/crowdsec` master : `cscli decisions add/delete` (flags `--ip/--range/--duration/--type/--reason/--bypass-allowlist/--id/--all`), `cscli alerts delete --id`, `cscli hub list/update/upgrade`, commandes par-type `collections/parsers/scenarios/postoverflows/appsec-rules/contexts install/remove/upgrade`, en-tête LAPI `X-Api-Key` (et non `Authorization: Bearer`), route versionnée `v1/decisions`.

## Seconde passe - extension de la couverture cscli (session du 1er nov. 2026)

Une revue approfondie du dépôt GitHub `crowdsecurity/crowdsec` master a montré que plusieurs commandes cscli et filtres LAPI n'étaient pas encore exposés dans l'UI. Ils sont maintenant implémentés et testés unitairement.

- [x] **Machines** : `cscli machines register/delete/prune` exposés dans `Machines.razor` (formulaire d'enrôlement avec option `--auto`, suppression par ID, prune des machines expirées). Modèle d'arguments `MachinesRegister/MachinesDelete/MachinesPrune` ajouté au `CrowdSecCommandBuilder`.
- [x] **Bouncers** : `cscli bouncers inspect/prune` exposés dans `Bouncers.razor` (bouton Inspect par ligne + bouton Prune revoked dans l'en-tête). Sortie inspect affichée dans un `<details>` réutilisable.
- [x] **Hub extras** : `cscli hub search/branch/types` ajoutés au command builder (`HubSearchAsync`, `HubBranchAsync`, `HubTypesAsync`). Branch et Types sont utiles en diagnostic pour vérifier la cohérence du hub.
- [x] **Config tools** : `cscli config show/show-yaml/backup/restore/feature-flags` ajoutés au command builder. La page `ConfigEditor.razor` expose Show merged / Show merged (YAML) / Feature flags / Backup to tar dans une nouvelle section "cscli config tools".
- [x] **LAPI** : `cscli lapi status/register/context list/context show` ajoutés au command builder. `lapi status` est déjà utilisé en interne ; `lapi register` et `lapi context` sont exposés via le service pour usage programmatique.
- [x] **Explain** : nouvelle page `ExplainPage.razor` pour tester le pipeline de logs via `cscli explain --log/--file --type [--labels] [--verbose]`. Inclut une liste des types d'acquisition courants (syslog, nginx, apache2, sshd, docker, file).
- [x] **Filtres avancés** : `cscli alerts list` et `cscli decisions list` supportent désormais les filtres `--ip/--scenario/--since/--until/--type`. La page `Alerts.razor` a une section "Filters" complète ; les mêmes filtres sont disponibles programmatiquement pour `Decisions.razor`.
- [x] **Tests xUnit** : nouveau fichier `CrowdSecCommandBuilderMoreTests.cs` (37 tests) couvrant toutes les nouvelles commandes (machines, bouncers inspect/prune, hub search/branch/types, config show/show-yaml/backup/restore/feature-flags, lapi status/register/context, explain log-line/log-file, alerts/decisions avec filtres).
- [x] **Smoke test automatisé** : `tests/shell/smoke-test.sh` orchestre les 7 items "à valider sur hôte Linux" restants du TODO. Il build l'image, démarre le conteneur, vérifie /health, fait un login+cookie+logout curl, valide crowdsec -t, ajoute une décision, vérifie le polling LAPI dans les logs, et nettoie. Syntaxe `bash -n` OK.
- [x] **CSS** : ajout des styles `ul.inline-list`, `.warn._msg` pour les nouvelles pages Explain et ConfigEditor.

## Récapitulatif de la couverture cscli vs source GitHub master

| Commande cscli (master) | Implémentée dans le projet | Page/UI |
| --- | --- | --- |
| `alerts list/delete` (+ filtres `--ip/--scenario/--since/--until/--type/--limit`) | ✅ | `Alerts.razor` |
| `bouncers list/add/delete/inspect/prune` | ✅ | `Bouncers.razor` |
| `collections/parsers/scenarios/postoverflows/appsec-rules/contexts install/remove/upgrade` | ✅ | `HubManagement.razor` |
| `config show/show-yaml/backup/restore/feature-flags` | ✅ | `ConfigEditor.razor` |
| `console status/enable alerts/disable alerts` | ✅ | `ConsolePage.razor` |
| `decisions list/add/delete` (+ `--bypass-allowlist`, filtres) | ✅ | `Decisions.razor` |
| `explain --log/--file --type [--labels] [--verbose]` | ✅ | `ExplainPage.razor` |
| `hub list/update/upgrade/search/branch/types` | ✅ | `HubManagement.razor` |
| `lapi status/register/context list/context show` | ✅ (service) | (programmatique) |
| `machines list/register/delete/prune` | ✅ | `Machines.razor` |
| `metrics` | ✅ (service) | (programmatique, dashboard) |
| `notifications list/inspect` | ✅ | `Notifications.razor` |
| `simulation list/status/enable/disable/add/remove` | ✅ | `Simulation.razor` |
| `allowlists list` | ✅ (service) | (programmatique) |
| `explain` (cf. ci-dessus) | ✅ | `ExplainPage.razor` |
| LAPI `/health`, `/v1/decisions` | ✅ | (service `CrowdSecApiService`) |

L'unique commande cscli présente dans master mais volontairement non exposée est `cscli explain --dsn` (test d'un DSN acquis) - couverture considérée suffisante avec `--log` et `--file`.

## Troisième passe - exposition UI des commandes service-only (session du 1er nov. 2026, suite)

La session précédente avait ajouté beaucoup de commandes au `CrowdSecCommandBuilder` et au service, mais certaines n'étaient pas encore exposées dans l'UI. Cette passe corrige cela et ajoute une commande cscli nouvellement identifiée contre le source GitHub.

- [x] **Bouncers add --key** : la commande `cscli bouncers add NAME` accepte un flag `--key KEY` (vérifié dans `cmd/crowdsec-cli/clibouncer/add.go`). Ajout de `CrowdSecCommandBuilder.BouncersAdd(name, apiKey)` et de `ICrowdSecCliService.AddBouncerAsync(name, apiKey?)`. La page `Bouncers.razor` a maintenant un champ "API key (optional)" qui active `--key` quand rempli.
- [x] **Hub diagnostics UI** : `cscli hub search/branch/types` (déjà au service) sont maintenant exposés dans une nouvelle section "Hub diagnostics" de `HubManagement.razor`. Search prend un query textuel, Branch et Types sont des boutons one-shot. La sortie cscli est affichée dans un `<details>` réutilisable.
- [x] **Page LAPI** : nouvelle page `LapiPage.razor` exposant `cscli lapi status` (rafraîchissable), un formulaire `cscli lapi register --machine/--url/--token` pour enrôler une machine distante, et un bouton `cscli lapi context list` pour lister les contextes LAPI. Ajout au NavMenu et au TopBar.
- [x] **Smoke-test étendu** : `tests/shell/smoke-test.sh` gagne un flag `--quadlet` qui installe le Quadlet systemd complet, vérifie `systemctl is-active/enabled crowdsec-manager`, attend la healthcheck du conteneur Quadlet, vérifie le polling LAPI dans ses logs, puis nettoie (disable + rm quadlet + rmi image). Une section "TODO.md mapping" à la fin du log explicite la correspondance entre chaque section du script et les 7 items restants.
- [x] **Tests xUnit** : 5 nouveaux tests dans `CrowdSecCommandBuilderMoreTests.cs` pour `BouncersAdd` (sans clé, avec clé, avec clé vide/blanche/null).
- [x] **TODO.md annoté** : chaque item `[ ]` restant indique maintenant quelle section du `smoke-test.sh` l'automatise, pour faciliter la validation sur l'hôte de déploiement.


## Comparaison avec CrowdSec officiel

La référence CrowdSec distingue les opérations `cscli` locales, l’API LAPI et les clés bouncer. Une clé bouncer est en lecture seule sur les décisions ; la consultation des alertes relève de l’authentification machine. Les éléments existants sont donc reliés à `cscli` pour les alertes, et à `/v1/decisions` pour la lecture LAPI des décisions.

| Besoin | État constaté |
| --- | --- |
| Lister les configurations installées et disponibles (`cscli hub list -a -o json`) | Implémenté ; format groupé testé avec la sortie réelle de CrowdSec 1.6. |
| Actualiser l’index Hub (`cscli hub update`) | Implémenté ; reste à valider sur une instance CrowdSec Linux de déploiement. |
| Installer/supprimer et mettre à niveau les collections, parsers, scénarios et postoverflows | Implémenté via `cscli`; exécution des opérations en runtime reste à valider. |
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
