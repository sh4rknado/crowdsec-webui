# Base GeoIP de CrowdSec Manager

## À quoi sert `GeoLite2-City.mmdb` ?

`GeoLite2-City.mmdb` est une base locale de géolocalisation d’adresses IP publiée par MaxMind. CrowdSec Manager s’en sert pour associer l’adresse IP source d’une alerte à un pays, une ville approximative et des coordonnées géographiques.

Le service `GeoIpService` lit cette base au démarrage, puis `ThreatFeedService` l’utilise lorsqu’il transforme les nouvelles alertes CrowdSec en événements. Les coordonnées permettent de placer la source sur la threat map et de tracer un arc vers l’hôte protégé. Le pays et son code ISO alimentent aussi les informations de l’événement, notamment la liste des principaux pays sources.

La base GeoIP ne contient ni les alertes CrowdSec ni les décisions de bannissement. Elle ne décide pas si une IP doit être bloquée et ne sert pas de fond cartographique. Les tuiles de la carte du monde proviennent séparément d’OpenStreetMap ; GeoIP sert à positionner les sources d’attaque sur cette carte.

## Précision et cas particuliers

- Une géolocalisation IP est une estimation. Elle peut indiquer la ville du fournisseur Internet, du VPN ou du point de sortie réseau plutôt que l’emplacement physique de la personne.
- Les adresses privées, locales et de boucle locale sont rattachées à l’emplacement de l’hôte protégé, configuré avec `GeoIp:HomeLatitude`, `GeoIp:HomeLongitude` et `GeoIp:HomeLabel`.
- Si la base est absente, illisible ou ne connaît pas l’adresse, le service utilise un emplacement déterministe de repli. Le pays est alors indiqué comme inconnu (`??`). Cet emplacement permet au rendu de fonctionner, mais ne représente pas la position réelle de l’IP.

## Emplacement et configuration

Le chemin par défaut est :

```text
/var/lib/GeoIP/GeoLite2-City.mmdb
```

Le dépôt fournit le fichier source dans `deploy/GeoLite2-City.mmdb`. Le `Dockerfile` le copie dans l’image à l’emplacement par défaut. Pour mettre à jour la base embarquée, remplacer ce fichier source puis reconstruire l’image.

Le chemin peut être remplacé avec la clé de configuration `GeoIp:DatabasePath`, ou sa variable d’environnement .NET :

```text
GeoIp__DatabasePath=/chemin/vers/GeoLite2-City.mmdb
```

La base est chargée une fois par le service GeoIP au démarrage. Après son remplacement ou la modification du chemin, redémarrer l’application pour que la nouvelle base soit lue. Le tableau de bord indique si la base configurée a pu être ouverte.

Pour obtenir une version à jour, télécharger GeoLite2-City depuis [MaxMind](https://www.maxmind.com/en/geolite2/signup) et respecter les conditions applicables à cette base.
