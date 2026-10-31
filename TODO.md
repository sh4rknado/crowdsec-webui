# État du projet — CrowdSec Manager

État établi le 31 octobre 2026 à partir de `readme.md`, `workload.md` et du code source.

## Implémenté dans le code

- [x] Application ASP.NET Core / Blazor Server ciblant .NET 8.
- [x] Authentification ASP.NET Identity avec stockage SQLite, rôle `Administrator` et création initiale du compte administrateur depuis `ADMIN_USER` / `ADMIN_PASSWORD`.
- [x] Tableau de bord avec statistiques CrowdSec, actions Hub et sauvegarde de configuration.
- [x] Gestion des décisions : liste, ajout, suppression et suppression globale.
- [x] Consultation et suppression des alertes.
- [x] Gestion Hub : liste, mise à jour, mise à niveau, installation et suppression d’éléments.
- [x] Gestion des bouncers et consultation des machines.
- [x] Éditeur de configuration YAML avec liste blanche, validation YAML et sauvegarde tar.
- [x] Services d’intégration `cscli` et LAPI, carte des menaces avec SignalR, historique SQLite et géolocalisation MaxMind avec solution de repli.
- [x] Endpoint `/health` et fichiers de déploiement Quadlet / scripts d’installation présents.
- [x] Référence du projet corrigée dans la solution Visual Studio.
- [x] Dépendances NuGet MaxMind et SignalR corrigées/ajoutées ; erreurs C# de compilation corrigées.

## Vérifié dans cet environnement

- [x] `dotnet build src/CrowdSecManager.sln` réussit sans erreur après restauration NuGet.
- [x] Le build s’effectue sans avertissement après correction de l’annotation nullable.
- [x] Dépôt Git local initialisé et premier commit créé (voir l’historique Git).

## À faire ou à valider en environnement de déploiement

- [ ] Construire l’image avec Podman et valider l’installation du runtime .NET dans l’image CrowdSec.
- [ ] Effectuer le smoke test du `workload.md` sur un hôte Linux avec systemd / Podman et vérifier l’état du service Quadlet.
- [ ] Valider l’amorçage de CrowdSec, la disponibilité LAPI et les commandes `cscli` avec une instance réelle.
- [ ] Tester la connexion, le rôle administrateur, les opérations CRUD et la persistance SQLite en exécution.
- [ ] Tester l’éditeur et la restauration de sauvegarde avec les fichiers de configuration CrowdSec réels.
- [ ] Tester la carte temps réel avec des alertes réelles et une base GeoLite2-City.mmdb valide.
- [ ] Ajouter une suite de tests automatisés ; aucune suite de tests n’est présente dans le dépôt à ce jour.
- [ ] Examiner l’incohérence documentaire sur le port HTTP local : le README indique `localhost:5000`, tandis que le conteneur écoute sur `8081`.

## Limites connues à suivre

- La compilation confirme la cohérence de la solution, pas le fonctionnement de l’application avec un CrowdSec réel.
- Sans base GeoLite2, la carte utilise la localisation de repli décrite dans le workload.
- La base applicative est créée avec `EnsureCreated()` ; le README recommande des migrations EF Core avant une évolution du schéma en production.
- Les instructions de restauration NuGet nécessitent l’accès à `api.nuget.org`.
