#!/bin/sh
# The first setup in one go, in a terminal where the owner types his password for sudo once - the
# Imager's user needs it for sudo. Then setup.sh, Tailscale (he signs in with the link it shows),
# Tailscale Serve for the page, and the PC's Tailscale address into the settings.
#
#   ssh -t gustav@wecker.local "sh wecker/first-run.sh"
set -eu
cd "$(dirname "$0")"

echo "Einmal dein Passwort für den Pi – das aus dem Imager. Danach läuft alles von selbst."
sudo -v
# Keep sudo's ticket fresh while this runs - the updates may take a while
( while kill -0 "$$" 2>/dev/null; do sudo -n true; sleep 50; done ) 2>/dev/null &

sh setup.sh

echo
echo "== Tailscale: den Link unten im Browser öffnen und mit deinem Tailscale-Konto anmelden"
sudo tailscale up --hostname=wecker

echo
echo "== Die Weck-Seite ins Tailnet"
sudo tailscale serve --bg --https=443 http://127.0.0.1:8080

# The PC by its Tailscale address - the check then needs no name lookup
pcip="$(tailscale ip -4 gustavs-legion 2>/dev/null | head -n 1 || true)"
if [ -n "$pcip" ]; then
  sudo python3 - "$pcip" <<'EOF'
import json
import sys
path = "/etc/wecker.json"
with open(path, encoding="utf-8") as f:
    cfg = json.load(f)
cfg["pc"]["ip"] = sys.argv[1]
with open(path, "w", encoding="utf-8") as f:
    json.dump(cfg, f, ensure_ascii=False, indent=2)
EOF
  # The copy here too - "setup.sh wecker" installs it and would otherwise drop the address again
  cp /etc/wecker.json wecker.json
  sudo systemctl restart wecker
fi

echo
echo "== Fertig"
echo "Wecker: $(systemctl is-active wecker), PC im Tailnet: ${pcip:-nicht gefunden}"
tailscale serve status 2>/dev/null | head -n 4 || true
if [ -f /var/run/reboot-required ]; then
  echo "Ein Update will einen Neustart – sudo reboot (oder er macht es selbst um 4:30)."
fi
