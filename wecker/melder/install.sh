#!/bin/sh
# Installs the Melder on the Pi or brings it up to date - running it again does no harm. In a
# terminal where the owner types his password for sudo (and, the first time, the Gmail app password):
#
#   ssh -t gustav@wecker "sh wecker/melder/install.sh"            install or update
#   ssh -t gustav@wecker "sh wecker/melder/install.sh passwort"   a new Gmail app password
#
# The settings come as melder.json next to this script (with the ntfy topic - not in Git); they go
# to /etc/melder, readable for root only, and the copy here is deleted. The app password is typed
# in with a * for every key (passwort.py) and goes straight to /etc/melder/imap-password - it never
# lies anywhere else.
set -eu
cd "$(dirname "$0")"

if [ ! -t 0 ]; then
  echo "Bitte mit ssh -t starten - das App-Passwort wird im Terminal eingetippt."
  exit 1
fi

echo "Einmal dein Passwort für den Pi (sudo):"
sudo -v

echo "== Melder"
sudo install -d -m 755 /opt/melder
sudo install -m 644 melder.py /opt/melder/melder.py
sudo install -d -m 700 /etc/melder
if [ -f melder.json ]; then
  sudo install -m 600 -o root -g root melder.json /etc/melder/melder.json
  rm -f melder.json
fi
if ! sudo test -s /etc/melder/melder.json; then
  echo "Es fehlen die Einstellungen: melder.json (wie melder.example.json) hierher kopieren."
  exit 1
fi

if [ "${1:-}" = "passwort" ] || ! sudo test -s /etc/melder/imap-password; then
  echo
  # passwort.py asks on the terminal (a * for every key) and hands only the 16 letters on - straight
  # into a root-only file, taken over only when it is complete
  python3 passwort.py | sudo sh -c 'umask 077; cat > /etc/melder/imap-password.neu'
  if sudo test -s /etc/melder/imap-password.neu; then
    sudo mv /etc/melder/imap-password.neu /etc/melder/imap-password
  else
    sudo rm -f /etc/melder/imap-password.neu
    echo "Kein App-Passwort gespeichert. Nochmal: sh wecker/melder/install.sh passwort"
    exit 1
  fi
fi

echo
echo "== Anmeldung prüfen (liest keine Mail)"
result=0
sudo python3 /opt/melder/melder.py --check --config /etc/melder/melder.json --password-file /etc/melder/imap-password || result=$?
if [ "$result" -eq 2 ]; then
  # A password Gmail refuses does not stay on the Pi - it may be another one typed by mistake
  sudo rm -f /etc/melder/imap-password
  echo "Gmail hat das Passwort abgelehnt - es ist auf dem Pi wieder gelöscht."
  echo "Mit einem App-Passwort von myaccount.google.com/apppasswords: sh wecker/melder/install.sh"
  exit 1
elif [ "$result" -ne 0 ]; then
  echo "Der Melder läuft so nicht (siehe oben). Nochmal: sh wecker/melder/install.sh"
  exit 1
fi

echo
echo "== Wecker-Seite (Tippen auf eine Meldung führt zu den Anfragen im Hub)"
# The copy here follows the one in use, so that "setup.sh wecker" keeps the PC's address
cp /etc/wecker.json ../wecker.json
if ! cmp -s ../page.html /opt/wecker/page.html; then
  sudo install -m 644 ../page.html /opt/wecker/page.html
  sudo systemctl restart wecker
  echo "Neue Seite eingespielt."
fi
echo "Wecker: $(systemctl is-active wecker)"

echo
echo "== Dienst"
sudo install -m 644 melder.service /etc/systemd/system/melder.service
sudo systemctl daemon-reload
sudo systemctl enable melder >/dev/null 2>&1
sudo systemctl restart melder
sleep 6
echo "Melder: $(systemctl is-active melder)"
sudo journalctl -u melder -n 8 --no-pager -o cat
