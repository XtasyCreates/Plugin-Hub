#!/bin/sh
# Sets the Raspberry Pi up as the Wecker, or brings it up to date - running it again does no harm.
# On the Pi, in the folder with the Wecker's files, as the user the Imager made (sudo needs no
# password there):
#
#   sh setup.sh            everything: system up to date, automatic security updates, Tailscale, the Wecker
#   sh setup.sh wecker     only the Wecker's own files - after a change to wecker.py, page.html or wecker.json
#   sh setup.sh tiss       only the TISS-Helfer (tiss_helfer.py, tiss.html) and its path /tiss in the tailnet
#
# Afterwards, once and by hand (see README.md): "sudo tailscale up" - the owner signs in with the
# link it shows - and "sudo tailscale serve --bg --https=443 http://127.0.0.1:8080".
set -eu
cd "$(dirname "$0")"

install_wecker() {
  sudo install -d -m 755 /opt/wecker
  sudo install -m 644 wecker.py page.html icon.png /opt/wecker/
  sudo install -m 644 wecker.service /etc/systemd/system/wecker.service
  if [ -f wecker.json ]; then sudo install -m 644 wecker.json /etc/wecker.json; fi
  sudo systemctl daemon-reload
  sudo systemctl enable wecker >/dev/null 2>&1
  sudo systemctl restart wecker
  sleep 2
  echo "Wecker: $(systemctl is-active wecker)"
}

# The TISS Bot's calendar and reminders (README.md, "TISS-Helfer"): a service of its own on
# 127.0.0.1:8081, in the tailnet next to the Wecker under /tiss
install_tiss() {
  sudo install -d -m 755 /opt/wecker
  sudo install -m 644 tiss_helfer.py tiss.html /opt/wecker/
  sudo install -m 644 tiss-helfer.service /etc/systemd/system/tiss-helfer.service
  sudo systemctl daemon-reload
  sudo systemctl enable tiss-helfer >/dev/null 2>&1
  sudo systemctl restart tiss-helfer
  if tailscale status >/dev/null 2>&1; then
    # Adds /tiss to what Serve does on 443 - the Wecker on / stays as it is
    sudo tailscale serve --bg --set-path /tiss http://127.0.0.1:8081
  else
    echo "Tailscale ist noch nicht angemeldet - danach: sh setup.sh tiss"
  fi
  sleep 2
  echo "TISS-Helfer: $(systemctl is-active tiss-helfer)"
}

if [ "${1:-all}" = "wecker" ]; then
  install_wecker
  exit 0
fi
if [ "${1:-all}" = "tiss" ]; then
  install_tiss
  exit 0
fi

. /etc/os-release
arch="$(dpkg --print-architecture)"
echo "== $PRETTY_NAME ($arch)"
export DEBIAN_FRONTEND=noninteractive

echo "== System up to date"
sudo -E apt-get -q update
sudo -E apt-get -y -q -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold full-upgrade

echo "== Automatic security updates - a restart at 04:30 when one asks for it"
sudo -E apt-get -y -q install unattended-upgrades
sudo tee /etc/apt/apt.conf.d/20auto-upgrades >/dev/null <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
APT::Periodic::AutocleanInterval "7";
EOF
sudo tee /etc/apt/apt.conf.d/52wecker >/dev/null <<'EOF'
// The Wecker (Plugin Hub): besides Debian's security updates (50unattended-upgrades) also the
// Pi's own kernel and firmware and Tailscale - and a restart at night when an update needs one
Unattended-Upgrade::Origins-Pattern {
        "origin=Raspberry Pi Foundation,codename=${distro_codename}";
        "origin=Tailscale";
};
Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-Time "04:30";
EOF

echo "== Tailscale"
if ! command -v tailscale >/dev/null 2>&1; then
  # Tailscale's own package source: Debian for the 64-bit system, Raspbian for the 32-bit one
  dist=debian
  [ "$arch" = "armhf" ] && dist=raspbian
  curl -fsSL "https://pkgs.tailscale.com/stable/$dist/$VERSION_CODENAME.noarmor.gpg" \
    | sudo tee /usr/share/keyrings/tailscale-archive-keyring.gpg >/dev/null
  curl -fsSL "https://pkgs.tailscale.com/stable/$dist/$VERSION_CODENAME.tailscale-keyring.list" \
    | sudo tee /etc/apt/sources.list.d/tailscale.list >/dev/null
  sudo -E apt-get -q update
  sudo -E apt-get -y -q install tailscale
fi
sudo systemctl enable --now tailscaled >/dev/null 2>&1
echo "Tailscale: $(tailscale version | head -n 1)"

echo "== Wecker"
install_wecker

echo "== TISS-Helfer"
install_tiss

if [ -f /var/run/reboot-required ]; then
  echo "== A restart is due (an update asks for it): sudo reboot"
fi
