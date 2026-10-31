podman run -d --name crowdsec-manager \
  --network host \
  -e ADMIN_USER=admin \
  -e ADMIN_PASSWORD='MyStrong!Pass1' \
  -v /etc/crowdsec:/etc/crowdsec:Z \
  -v /var/lib/crowdsec:/var/lib/crowdsec:Z \
  -v /var/lib/crowdsec-manager:/var/lib/crowdsec-manager:Z \
  -v /var/lib/GeoIP:/var/lib/GeoIP:ro,Z \
  -v /var/log:/var/log:ro,Z \
  localhost/crowdsec-manager:latest
