#!/usr/bin/env python3
"""Wecker - wakes the PC from anywhere: a small page on the Raspberry Pi in the home network.

The phone opens it through Tailscale (https://wecker.<tailnet>.ts.net; Tailscale Serve hands it on
to 127.0.0.1:8080 here). Is the PC asleep, the Pi sends it a magic packet through the home network,
because a wake-up call cannot come over the internet; once the PC is awake and the Plugin Hub
answers, the page goes on to the hub. Nothing but Python's standard library.

    wecker.py --config /etc/wecker.json        (settings: see wecker.example.json)

Who may use it: requests Tailscale Serve hands on carry the login of the person asking
(Tailscale-User-Login); only the logins in "allow" are served, a request through Funnel - from
the open internet - never. Waking takes a header the page sets (X-Wecker), so no other web page
can make the phone wake the PC.
"""

import argparse
import json
import socket
import ssl
import sys
import threading
import time
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
TLS = ssl.create_default_context()
WAKE_GAP = 3.0          # seconds between two wake-up calls at the least
_last_wake = 0.0
_gate = threading.Lock()


def load(path):
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
    mac = bytes.fromhex(cfg["mac"].replace(":", "").replace("-", ""))
    if len(mac) != 6:
        raise SystemExit("mac: six bytes, e.g. C8:53:09:57:5E:D3")
    cfg["_packet"] = b"\xff" * 6 + mac * 16
    cfg.setdefault("listen", "127.0.0.1:8080")
    cfg.setdefault("broadcast", ["255.255.255.255"])
    cfg.setdefault("wakePort", 9)
    cfg.setdefault("allow", [])
    cfg.setdefault("wakeOnOpen", True)
    cfg.setdefault("waitSeconds", 120)
    cfg.setdefault("bot", None)
    pc = cfg["pc"]
    pc.setdefault("port", 443)
    pc.setdefault("tls", True)
    pc.setdefault("check", "/api/hello")
    return cfg


# ── The PC ────────────────────────────────────────────────────────────────────

def pc_state(cfg):
    """asleep: nothing answers. awake: the PC answers, the hub not (yet). hub: the hub answers."""
    pc = cfg["pc"]
    name, address = pc["name"], pc.get("ip") or pc["name"]
    try:
        raw = socket.create_connection((address, pc["port"]), timeout=1.2)
    except OSError:
        return "asleep"
    try:
        raw.settimeout(3)
        conn = TLS.wrap_socket(raw, server_hostname=name) if pc["tls"] else raw
        with conn:
            conn.sendall(f"GET {pc['check']} HTTP/1.1\r\nHost: {name}\r\nConnection: close\r\n\r\n".encode("ascii"))
            head = conn.recv(64).split(b"\r\n", 1)[0].split()
        # 200: the hub says hello. 403: the hub answers but refuses the Pi - running all the same.
        # Anything else (502 from Tailscale Serve) means the PC is up and the hub is not.
        return "hub" if len(head) >= 2 and head[1] in (b"200", b"403") else "awake"
    except (OSError, ssl.SSLError):
        return "awake"
    finally:
        raw.close()


def wake(cfg):
    """The magic packet - six times FF, then the MAC sixteen times - a few times to every broadcast address."""
    global _last_wake
    with _gate:
        if time.monotonic() - _last_wake < WAKE_GAP:
            return False
        _last_wake = time.monotonic()
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
        s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        for _ in range(3):
            for address in cfg["broadcast"]:
                s.sendto(cfg["_packet"], (address, cfg["wakePort"]))
            time.sleep(0.1)
    return True


# ── The page ──────────────────────────────────────────────────────────────────

PAGE = (HERE / "page.html").read_text(encoding="utf-8")
ICON = (HERE / "icon.png").read_bytes() if (HERE / "icon.png").exists() else b""

SECURITY = {
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Referrer-Policy": "no-referrer",
    "Content-Security-Policy": "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; "
                               "img-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
}


class Handler(BaseHTTPRequestHandler):
    server_version = "Wecker"
    sys_version = ""
    cfg = None

    def log_message(self, fmt, *args):   # journald adds the time
        sys.stdout.write("%s %s\n" % (self.headers.get("Tailscale-User-Login", "-"), fmt % args))
        sys.stdout.flush()

    def allowed(self):
        if "Tailscale-Funnel-Request" in self.headers:
            return False
        allow = self.cfg["allow"]
        return not allow or self.headers.get("Tailscale-User-Login", "").lower() in [a.lower() for a in allow]

    def send(self, status, body, kind):
        self.send_response(status)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        for k, v in SECURITY.items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def json(self, status, value):
        self.send(status, json.dumps(value, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8")

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        if not self.allowed():
            return self.send(403, b"Nicht freigegeben.", "text/plain; charset=utf-8")
        path = self.path.split("?", 1)[0]
        if path == "/":
            settings = {"hub": self.cfg["hub"], "bot": self.cfg["bot"], "wakeOnOpen": self.cfg["wakeOnOpen"],
                        "waitSeconds": self.cfg["waitSeconds"]}
            # Inside a <script>: no "<" that could end it
            body = PAGE.replace("/*SETTINGS*/{}", json.dumps(settings, ensure_ascii=False).replace("<", "\\u003c"))
            return self.send(200, body.encode("utf-8"), "text/html; charset=utf-8")
        if path == "/status":
            return self.json(200, {"state": pc_state(self.cfg), "at": datetime.now().isoformat(timespec="seconds")})
        if path == "/icon.png" and ICON:
            return self.send(200, ICON, "image/png")
        return self.send(404, b"Gibt es nicht.", "text/plain; charset=utf-8")

    def do_POST(self):
        if not self.allowed():
            return self.send(403, b"Nicht freigegeben.", "text/plain; charset=utf-8")
        # Only the page itself: a form or a script elsewhere cannot set this header without asking first
        origin = self.headers.get("Origin")
        host = self.headers.get("X-Forwarded-Host") or self.headers.get("Host") or ""
        if self.headers.get("X-Wecker") != "1" or (origin and origin.split("://", 1)[-1] != host):
            return self.send(403, b"Nur von der Weck-Seite.", "text/plain; charset=utf-8")
        if self.path != "/wake":
            return self.send(404, b"Gibt es nicht.", "text/plain; charset=utf-8")
        sent = wake(self.cfg)
        if sent:
            self.log_message("wake-up call sent to %s", self.cfg["mac"])
        return self.json(200, {"sent": sent})


def main():
    parser = argparse.ArgumentParser(description="Weckt den PC über das Heimnetz.")
    parser.add_argument("--config", default="/etc/wecker.json")
    cfg = load(parser.parse_args().config)
    host, port = cfg["listen"].rsplit(":", 1)
    Handler.cfg = cfg
    server = ThreadingHTTPServer((host, int(port)), Handler)
    server.daemon_threads = True
    print(f"Wecker on http://{host}:{port}/ for {cfg['pc']['name']}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
