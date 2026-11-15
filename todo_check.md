# Vérification des fonctionnalités

Inventaire basé sur les fonctionnalités documentées dans `README.md`, les pages Blazor/ViewModels et les tests présents dans `src/*Tests` et `tests/`.

**Légende :** `[X]` = un test unitaire existant couvre la logique indiquée ; `[ ]` = aucune couverture unitaire repérée pour ce point, même si un test d’intégration ou un script automatisé existe (ceux-ci sont précisés dans le texte). Une case cochée ne signifie pas que le parcours complet a été vérifié manuellement avec un vrai moteur CrowdSec.

## Tableau de bord et données

- [X] Charger statistiques, décisions récentes et disponibilité GeoIP — `DashboardViewModelTests.Refresh_LoadsStatsDecisionsAndGeoAvailability`.
- [X] Actions rapides du tableau de bord : mise à jour du Hub et suppression d’une décision — `DashboardViewModelTests.HubUpdateAndDeleteDecision_SetActionState`.
- [ ] Vérifier visuellement les cartes, états de chargement/erreur et actualisation dans le navigateur.
- [X] Polling du LAPI, repli sur `cscli`, annulation et arrêt du service hébergé — `ThreatFeedServiceTests`.
- [X] Dédupliquer les alertes, persister l’historique, publier les événements, réessayer une écriture et purger l’historique ancien — `ThreatFeedServiceTests.ProcessBatch_*`.
- [X] Géolocalisation de repli, IPv6, adresses privées et normalisation IPv4 — `GeoIpServiceTests`.
- [X] Bus d’événements, snapshot/rejeu et capacité limitée — `ThreatMapEventBusTests`, `ThreatMapEventReplayBufferTests` et `ThreatMapViewModelTests`.
- [X] Traitement des événements et statistiques par le ViewModel de la carte — `ThreatMapViewModelTests`.
- [X] Tests unitaires JavaScript de la carte présents — `tests/javascript/threatmap.test.mjs`.
- [ ] Vérifier dans un navigateur le rendu cartographique réel, Leaflet/OpenStreetMap, les animations et l’arrivée des événements SignalR.

## Décisions et alertes

- [X] Lister/filtrer les décisions, utiliser le repli CLI si le LAPI échoue — `DecisionsViewModelTests.Refresh_FallsBackToCliWhenApiFails`.
- [X] Ajouter une décision, valider IP/CIDR, paramètres de durée/type/raison et bypass allowlist — `DecisionsViewModelTests.Add_*`, `CrowdSecCommandBuilderTests` et `CrowdSecCommandBuilderExtendedTests`.
- [X] Supprimer une décision et tout supprimer, gérer erreurs/succès — `DecisionsViewModelTests.DeleteAndDeleteAll_HandleFailureAndSuccess` et tests du command builder.
- [X] Validation des cibles IP/CIDR au niveau CLI — `CrowdSecCliServiceTests.AddDecision_*`.
- [X] Rafraîchir/filtrer/effacer filtres d’alertes et supprimer une alerte — `AlertsViewModelTests`.
- [X] Bannir la source d’une alerte et vérifier son adresse — `AlertsViewModelTests.Ban_*`, `CrowdSecCommandBuilderTests`.
- [X] Désérialiser la forme d’alerte CrowdSec 1.8 — `CsAlertJsonTests`.
- [ ] Vérifier les interactions de la page Alerts et Decisions dans le navigateur avec le LAPI réel.

## Hub, configuration et diagnostics

- [X] Charger/parser les éléments du Hub et dériver leur état d’installation — `CrowdSecCommandBuilderTests.HubParser_*`, `CrowdSecCliServiceTests.ListHub_*`.
- [X] Filtrer/rechercher dans le Hub, valider une recherche et installer un élément — `HubManagementViewModelTests`.
- [X] Construire les commandes Hub install/remove/upgrade/update — `CrowdSecCommandBuilderTests` et `CrowdSecCommandBuilderMoreTests`.
- [ ] Vérifier avec le Hub CrowdSec réel les opérations update, upgrade, install et remove, y compris les erreurs réseau.
- [X] Lister, charger, valider et enregistrer les YAML autorisés ; refuser YAML invalide, chemins hors whitelist et chemins symlinkés — `ConfigFileServiceTests`.
- [X] Créer un fichier YAML, sélectionner/éditer, valider/recharger le brouillon et présenter les résultats — `ConfigEditorViewModelTests`.
- [X] Import/export ZIP YAML, chemins relatifs et rejet des chemins d’archive dangereux — `ConfigFileServiceTests.ExportYamlZip_*` et `ImportYamlZip_*`.
- [X] Sauvegarde tar des configurations imbriquées — `ConfigFileServiceTests.Backup_*`.
- [X] Construire les commandes config show/show-yaml/feature flags/backup/restore — `CrowdSecCommandBuilderMoreTests`.
- [X] Valider la configuration CrowdSec avec le binaire configuré et afficher les diagnostics — `CrowdSecCliServiceTests.ValidateEngineConfig_*`.
- [X] Construire la commande de reload ciblant CrowdSec et gérer l’absence de processus — `CrowdSecCliServiceTests.ReloadService_*`, `CrowdSecCommandBuilderTests.ReloadSignal_*`.
- [X] Tester les helpers JavaScript de téléchargement ZIP et texte — `tests/javascript/config-transfer.test.mjs`.
- [X] Couvrir par tests le sélecteur/import ZIP (format, limite 30 Mio, erreurs), le rafraîchissement après import et la confirmation avant d’abandonner un brouillon — `BlazorScriptReferenceTests.ConfigEditor_ConfirmsDiscardBeforeReloadAndSupportsZipImport`, `ConfigEditorViewModelTests.ImportYamlZip_*`.
- [X] Expliquer une ligne/fichier, vérifier champs obligatoires, labels et rendu d’erreur — `ExplainViewModelTests` et `CrowdSecCommandBuilderMoreTests.Explain*`.
- [ ] Vérifier `cscli explain` contre les parsers et scénarios installés dans l’instance réelle.

## Connecteurs CrowdSec

- [X] Bouncers : validation du nom, ajout, extraction de clé et inspection — `BouncersViewModelTests.Add_*`, `BouncersViewModelTests.Inspect_*`.
- [X] Bouncers : suppression/prune, erreurs/succès et rafraîchissement du ViewModel — `BouncersViewModelTests.DeleteAndPrune_ReportResultsAndRefresh` (assertion mise à jour pour le nouveau message).
- [X] Construire les commandes bouncer add/inspect — `CrowdSecCommandBuilderMoreTests`.
- [X] Vérifier la commande bouncer prune avec seuil `1h` explicite et `--force` — `BouncersPrune_SkipsInteractiveConfirmation`.
- [X] Vérifier que `bouncers prune` n’ajoute pas `-o json` et transmet seuil/`--force` — `CrowdSecCliServiceTests.PruneBouncers_UsesForceWithoutAppendingJsonOutput`.
- [X] Couvrir la confirmation du prune, le seuil affiché et l’appel conditionnel au ViewModel — `BlazorScriptReferenceTests.BouncersPage_ConfirmsInactivePruneBeforeCallingViewModel`.
- [ ] Vérifier visuellement les réponses « confirmer » et « annuler » du dialogue dans le navigateur.
- [ ] Vérifier avec CrowdSec que le prune ne supprime que les bouncers inactifs depuis le seuil prévu (60 minutes) et que la liste est rafraîchie après action.
- [X] Machines : inscription avec/sans nom, échec d’inscription, chargement et prune au niveau ViewModel — `MachinesViewModelTests`.
- [X] Construire les commandes machines list/register/delete/prune — `CrowdSecCommandBuilderMoreTests`.
- [X] Confirmer le prune machines avant l’appel et expliciter le délai d’inactivité de 10 minutes — `BlazorScriptReferenceTests.MachinesPage_ConfirmsExpiredPruneBeforeCallingViewModel`.
- [X] Couvrir la commande CLI machines prune sans invite interactive, sans sortie JSON, avec seuil `10m`; et le seuil `1h` du prune bouncer — `MachinesPrune_UsesTenMinuteThresholdWithoutPrompt`, `CrowdSecCliServiceTests.PruneMachines_UsesForceAndTenMinuteThresholdWithoutJsonOutput`.
- [ ] Vérifier les seuils effectifs et les entrées supprimées par `cscli machines prune`/`bouncers prune` dans une instance CrowdSec isolée.
- [X] LAPI : afficher état, enregistrer une machine distante avec validation URL et lister les contextes — `LapiViewModelTests`.
- [X] Construire les commandes LAPI status/register/context — `CrowdSecCommandBuilderMoreTests`.
- [X] Appeler l’endpoint health, envoyer l’en-tête API key, lire les décisions, gérer erreur/repli/cancellation — `CrowdSecApiServiceTests`.
- [ ] Vérifier les connecteurs et contextes contre une installation CrowdSec réelle.

## Simulation, notifications et Console

- [X] Simulation : ajouter/retirer un scénario, activer/désactiver le mode global et rafraîchir l’état — `SimulationViewModelTests`.
- [X] Construire les commandes simulation list/status/enable/disable/add/remove — `CrowdSecCommandBuilderExtendedTests`.
- [ ] Vérifier les effets de simulation sur des alertes CrowdSec réelles.
- [X] Notifications : nom par défaut, chargement/inspection, validation des plugins, écriture YAML/profil et rafraîchissement — `NotificationsViewModelTests`.
- [X] Générer les profils/plugins Slack, HTTP et email, y compris les entrées invalides — `NotificationConfigBuilderTests`.
- [X] Construire les commandes notifications list/inspect — `CrowdSecCommandBuilderExtendedTests`.
- [ ] Vérifier la livraison réelle vers Slack, webhook HTTP et serveur email, et le reload de CrowdSec.
- [X] Construire les commandes Console status/enable/disable — `CrowdSecCommandBuilderExtendedTests`.
- [X] Page CrowdSec Console ajoutée dans **Advanced**, avec statut, activation/désactivation, actualisation après succès et restriction Administrator — `CrowdSecConsoleViewModelTests`, `BlazorScriptReferenceTests.CrowdSecConsolePage_RestrictsAlertSharingToAdministrators`.
- [ ] Vérifier l’enrôlement, l’état et le partage d’alertes avec le service CrowdSec Console réel.

## Terminal, sécurité et comptes

- [X] Tokeniser les commandes, retirer le préfixe cscli et gérer les entrées de manière sûre — `CommandLineTokenizerTests`.
- [X] Terminal : aide, historique, exécution, sortie, erreurs, annulation, concurrence et états occupé — `TerminalViewModelTests`.
- [ ] Vérifier l’interaction terminal dans le navigateur (touches, défilement, focus et sortie longue).
- [X] Résultats CLI et formatage des erreurs — `CliResultTests` et `CrowdSecCliServiceTests`.
- [X] Process runner : capturer stdout/stderr, terminer sur timeout et annulation — `CrowdSecCliServiceTests.ProcessRunner_*`.
- [X] Création idempotente de l’administrateur, refus d’amorçage sans mot de passe, persistance SQLite de l’historique — `IdentityAndPersistenceTests`.
- [ ] Routes protégées, redirection login, antiforgery, cookies login/logout et refus des URLs externes — couverture d’intégration dans `HttpSmokeTests`, pas un test unitaire.
- [X] Couvrir le rôle Administrator dans la page Account et valider réellement les annotations (champs obligatoires, longueur, confirmation) — `BlazorScriptReferenceTests.AccountPage_RequiresAdministratorAndValidatesPasswordConfirmation`, `IdentityAndPersistenceTests.AccountPasswordChangeForm_ValidatesRequiredLengthAndConfirmation`.
- [X] Vérifier avec Identity/SQLite que `ChangePasswordAsync` refuse un mot de passe courant incorrect, puis remplace réellement l’ancien — `IdentityAndPersistenceTests.Identity_ChangesPasswordOnlyWithCurrentPassword`.
- [ ] Tester le handler de la page Account et le renouvellement du cookie après changement dans un parcours Blazor authentifié.
- [ ] Vérifier visuellement la navigation, l’accessibilité des onglets et les droits Administrator/User.

## Sauvegardes, déploiement et opérations

- [X] Sauvegarder/restaurer configs CrowdSec, données moteur/manager et SQLite — `AppBackupServiceTests`.
- [X] La page Configuration expose des opérations de backup et leurs états dans le ViewModel — `ConfigEditorViewModelTests.CrowdSecCommandsAndBackup_*`.
- [ ] Tester l’interface complète de création/téléchargement/restauration de backup dans le navigateur.
- [ ] Scripts shell isolés d’entrypoint, supervision du moteur, format de build, préflight et migration Blazor — cinq scripts automatisés passent avec 60 assertions cumulées, mais ce ne sont pas des tests unitaires.
- [ ] Smoke-test conteneur CrowdSec complet — non lancé : son script monte directement `/etc/crowdsec`, `/var/lib/crowdsec`, `/var/lib/crowdsec-manager` et `/var/log` du WSL, retire le conteneur fixe `crowdsec-manager-smoke` et peut supprimer une décision. Il faut d’abord adapter son environnement pour utiliser des volumes et un nom dédiés.
- [ ] Vérification visuelle des pages interactives — l’application locale isolée a répondu sur `/health` et `/login`; 16 routes authentifiées ont été chargées par HTTP, mais l’outil de navigateur a expiré à deux reprises avant d’ouvrir l’onglet.
- [ ] Vérification du moteur CrowdSec installé sur Windows — `cscli` v1.6.0 démarre, mais `lapi status` et `bouncers list` échouent en lecture des identifiants sous `C:\ProgramData\CrowdSec\config` (« Access is denied »). Aucun changement n’a été tenté.
- [ ] Vérification du moteur CrowdSec dans WSL — Podman est présent mais aucune image du projet n’est disponible et `jq` manque ; le smoke-test documenté s’appuie en outre sur les répertoires de données réels du WSL.
- [ ] Contrôle du runtime ASP.NET 10 dans le Dockerfile — le script `blazor-script.test.sh` a été corrigé et passe ; c’est un contrôle statique shell, pas un test unitaire.
- [X] Mapping des variables d’environnement et priorité des paramètres .NET — `CrowdSecEnvironmentMapperTests`.
- [X] Vérifier des règles de sécurité de déploiement et références de scripts Blazor — `DeploymentSafetyTests`, `BlazorScriptReferenceTests`.
- [ ] Endpoint health et réponses/navigation HTTP de base — couverture d’intégration dans `HttpSmokeTests`, pas un test unitaire.
- [ ] Vérifier le démarrage réel du conteneur/Quadlet, persistance des volumes, permissions SELinux, secret de bouncer et redémarrage automatique sur une machine Linux cible.

## Vérifications à faire ensuite

- [X] Mettre à jour les assertions bouncer obsolètes : commande prune avec `--force` et message « Pruned inactive bouncers. ».
- [X] Ajouter un test CLI prouvant que `bouncers prune` ne reçoit pas `-o json` et que l’option `--force` est envoyée.
- [X] Suite complète relancée après les ajouts : 392 tests .NET réussis (118 ViewModel + 274 service/UI), 9 tests JavaScript réussis et 60 assertions de scripts shell réussies.
- [ ] Parcourir chaque page dans le navigateur et vérifier les succès, erreurs, validations et confirmations.
- [ ] Effectuer un smoke-test avec un moteur CrowdSec de test dans des volumes isolés, sans toucher aux données CrowdSec du WSL.

## Contrôles complémentaires exécutés

- L’application a été lancée avec une base SQLite temporaire, un chemin CLI inexistant et un port LAPI de test fermé ; `/health` et `/login` ont répondu HTTP 200.
- Après connexion au compte synthétique local, 16 routes ont répondu HTTP 200 : `/`, `/threatmap`, `/decisions`, `/alerts`, `/hub`, `/simulation`, `/connectors`, `/lapi`, `/bouncers`, `/machines`, `/config`, `/notifications`, `/console`, `/explain`, `/account` et `/advanced`. Cela vérifie le rendu HTTP initial, pas les clics interactifs.
- Aucun clic UI ni changement sur l’instance CrowdSec installée n’a été effectué.
