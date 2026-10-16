#!/usr/bin/env python3
"""Melder - tells the phone about code requests, also while the PC sleeps.

Runs on the Raspberry Pi next to the Wecker. Every two minutes it looks into the mailbox the Plugin
Hub reads code requests from - over IMAP and only reading, like the hub: EXAMINE and BODY.PEEK, so
nothing is marked, moved or deleted - and tells the phone through ntfy: "Testcode-Anfrage für Laser
Create". A tap on it opens the Wecker, which wakes the PC and goes on to the requests in the hub.
Until a request is answered, a reminder every 12 hours; one that falls due at night waits for the
morning. A push names the plug-in and what is asked for, never who asks.

A mail is a request when the hub would take it for one (Services/Inbox.cs, Recognize): it carries a
device id and is not a reply. The plug-in comes from the subject the activation window writes
("Free trial of <plug-in>", "License for <plug-in>"), else from the text; trial or licence from the
box ticked in it.

A request is done when an answer to it lies in the sent mail (In-Reply-To or References name it -
the hub's code mail does), when an answer went to the same request from the same device (it came
twice), or when the mail has left the inbox (archived or deleted in Gmail). After seven days
without any of that it says so a last time and leaves it.

Besides: the Pi's health - temperature, free space on the card, under-voltage, AdGuard and the
Wecker running, the mailbox readable - with a push when something is wrong and when it is fine
again, and on Sundays at 10 a line that it is alive.

Nothing about customers is kept: the state holds checksums of message id and device id, the
plug-in, what was asked for and the times. Nothing but Python's standard library.

    melder.py                  the service: settings and app password from systemd (LoadCredential)
    melder.py --check          log in once and say what it sees - reads no mail, sends no push
    melder.py --once           one round, then stop
    --config, --password-file, --state  the files, when not run by systemd
"""

import argparse
import email
import email.policy
import hashlib
import html
import imaplib
import json
import os
import re
import secrets
import shutil
import socket
import ssl
import struct
import subprocess
import sys
import time
import traceback
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path

TLS = ssl.create_default_context()

DEFAULTS = {
    "imap": {"host": "imap.gmail.com", "port": 993, "tls": True, "user": ""},
    "ntfy": {"server": "https://ntfy.sh", "topic": "", "click": ""},
    "products": [],
    "intervalSeconds": 120,
    "remindHours": 12,
    "giveUpDays": 7,
    "quiet": {"from": "22:00", "to": "08:00"},
    "health": {
        "tempWarn": 75,
        "diskMinGb": 2,
        "mailFailMinutes": 30,
        "thermal": "/sys/class/thermal/thermal_zone0/temp",
        "hwmon": "/sys/class/hwmon",
        "disk": "/",
        "adguard": {"tcp": "127.0.0.1:53"},
        "wecker": {"tcp": "127.0.0.1:8080"},
        "heartbeat": {"weekday": 6, "time": "10:00"},
    },
}

TAP = "Tippen weckt den PC und öffnet die Anfragen im Hub."
KIND = {"trial": "Testcode-Anfrage", "full": "Kauf-Anfrage"}
WHY = {"reply": "Antwort im Gesendet-Ordner", "same": "Antwort auf dieselbe Anfrage desselben Geräts",
       "gone": "Mail nicht mehr im Posteingang", "7-days": "seit 7 Tagen ohne Antwort - keine Erinnerung mehr"}
MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]


def log(text):
    print(text, flush=True)   # journald adds the time


# ── What a request is - as the hub sees it (Services/Inbox.cs, Services/MailParser.cs) ─────────

DEVICE_ID = re.compile(r"(?<![0-9A-Fa-f])[0-9A-Fa-f]{4}(?:[-\s][0-9A-Fa-f]{4}){5}(?![0-9A-Fa-f])")
REPLY_SUBJECT = re.compile(r"^\s*(re|aw|antw|wg|fwd?|tr)\s*:", re.IGNORECASE)
MESSAGE_ID = re.compile(r"<[^<>\s]+>")
FETCH_LIMIT = 262144   # the first 256 KB of a mail: a request is a few lines of text
BATCH = 20


class Mail:
    def __init__(self, message_id, subject, text, reply):
        self.message_id, self.subject, self.text, self.reply = message_id, subject, text, reply


def parse_mail(raw):
    """Message id, subject and plain text - from the text/plain part, or from the HTML when that is all."""
    msg = email.message_from_bytes(raw, policy=email.policy.default)
    subject = header(msg, "Subject")
    message_id = header(msg, "Message-ID").strip()
    if not message_id:
        message_id = "sha256:" + hashlib.sha256(raw).hexdigest()[:24]
    found = {}
    collect(msg, found, 0)
    text = found.get("plain")
    if text is None:
        text = html_to_text(found["html"]) if "html" in found else ""
    reply = bool(REPLY_SUBJECT.match(subject)) or msg.get("In-Reply-To") is not None
    return Mail(message_id, subject, text.replace("\r\n", "\n").strip(), reply)


def header(msg, name):
    try:
        value = msg.get(name)
    except Exception:   # a header the parser cannot make sense of
        return ""
    return "" if value is None else str(value)


def collect(part, found, depth):
    """The first text/plain and the first text/html that are not attachments."""
    if header(part, "Content-Disposition").strip().lower().startswith("attachment"):
        return
    kind = part.get_content_type()
    if kind.startswith("multipart/"):
        if depth < 6 and part.get_param("boundary") is not None:
            for sub in part.iter_parts():
                collect(sub, found, depth + 1)
        return
    if kind == "text/plain" and "plain" not in found:
        found["plain"] = decode(part)
    elif kind == "text/html" and "html" not in found:
        found["html"] = decode(part)


def decode(part):
    try:
        payload = part.get_payload(decode=True) or b""
    except Exception:
        payload = b""
    charset = part.get_content_charset() or "utf-8"   # undeclared text is mostly UTF-8 today
    try:
        return payload.decode(charset, "replace")
    except LookupError:
        return payload.decode("utf-8", "replace")


def html_to_text(text):
    text = re.sub(r"<(script|style)\b[^>]*>.*?</\1>", "", text, flags=re.S | re.I)
    text = re.sub(r"<br\s*/?>|</p>|</div>|</li>|</tr>|</h\d>", "\n", text, flags=re.I)
    return html.unescape(re.sub(r"<[^>]+>", "", text))


def ticked(text, label):
    return re.search(r"\[\s*[xX✓✔*]\s*\]\s*" + label, text, re.IGNORECASE) is not None


def recognize(mail, products):
    """The hub's Inbox.Recognize: a mail with a device id that is not a reply. Checksums only."""
    if mail.reply:
        return None
    found = DEVICE_ID.search(mail.text)
    if not found:
        return None
    digits = re.sub(r"[^0-9A-Fa-f]", "", found.group()).upper()
    device = "-".join(digits[i:i + 4] for i in range(0, 24, 4))
    subject, text = mail.subject.lower(), mail.text.lower()
    product = next((p for p in products if p.lower() in subject), None) \
        or next((p for p in products if p.lower() in text), None)
    trial, full = ticked(mail.text, "a free trial"), ticked(mail.text, "a full licen[cs]e")
    return {"product": product, "wants": None if trial == full else "trial" if trial else "full",
            "device": digest(device), "msg": digest(message_key(mail.message_id))}


def message_key(message_id):
    m = MESSAGE_ID.search(message_id)
    return m.group() if m else message_id.strip()


def digest(text):
    return hashlib.sha256(("melder:" + text).encode("utf-8")).hexdigest()[:32]


# ── The mailbox ───────────────────────────────────────────────────────────────

class MailError(Exception):
    """The mailbox could not be read - said so that the owner knows what to do."""


LIST_LINE = re.compile(rb'^\((?P<flags>[^)]*)\)\s+(?:"(?:[^"\\]|\\.)*"|NIL)\s+(?P<name>.+)$')


def quote(name):
    return '"' + name.replace("\\", "\\\\").replace('"', '\\"') + '"'


def unquote(name):
    if len(name) >= 2 and name[0] == name[-1] == '"':
        return re.sub(r"\\(.)", r"\1", name[1:-1])
    return name


class Mailbox:
    """Just enough IMAP to read: log in, open a folder read-only (EXAMINE), search by UID and fetch
    without marking anything read (BODY.PEEK). It never stores, copies, moves or deletes."""

    def __init__(self, cfg, password, timeout=30):
        if cfg["tls"]:
            self.imap = imaplib.IMAP4_SSL(cfg["host"], cfg["port"], ssl_context=TLS, timeout=timeout)
        else:
            self.imap = imaplib.IMAP4(cfg["host"], cfg["port"], timeout=timeout)
        try:
            self.imap.login(cfg["user"], password)
        except BaseException:
            self.close()
            raise

    def close(self):
        try:
            self.imap.logout()
        except Exception:
            pass

    def folders(self):
        """(name, attributes) of every folder."""
        typ, data = self.imap.list('""', "*")
        if typ != "OK":
            raise MailError("Die Ordner des Postfachs ließen sich nicht lesen.")
        out = []
        for item in data or []:
            if isinstance(item, tuple):   # a name sent as a literal
                m = re.match(rb"\(([^)]*)\)", item[0])
                out.append((item[1].decode("utf-8", "replace"), (m.group(1) if m else b"").decode("ascii", "replace").split()))
            elif item:
                m = LIST_LINE.match(item)
                if m:
                    out.append((unquote(m.group("name").decode("utf-8", "replace")), m.group("flags").decode("ascii", "replace").split()))
        return out

    def sent_folder(self):
        """The folder sent mail lands in: marked \\Sent (RFC 6154) - Gmail's "[Gmail]/Gesendet"."""
        folders = self.folders()
        for name, flags in folders:
            if any(f.lower() == "\\sent" for f in flags):
                return name
        names = {n.lower(): n for n, _ in folders}
        for guess in ("[Gmail]/Sent Mail", "[Gmail]/Gesendet", "[Google Mail]/Sent Mail", "[Google Mail]/Gesendet",
                      "Sent", "Gesendet", "Sent Items", "INBOX.Sent"):
            if guess.lower() in names:
                return names[guess.lower()]
        return None

    def examine(self, name):
        """Opens a folder read-only: UIDVALIDITY (do the UIDs seen before still mean the same mails),
        UIDNEXT (the UID the next mail gets) and how many mails it holds."""
        typ, data = self.imap.select(quote(name), readonly=True)   # EXAMINE
        if typ != "OK":
            raise MailError(f"Der Ordner „{name}“ ließ sich nicht öffnen.")
        return self._code("UIDVALIDITY"), self._code("UIDNEXT"), number(data[0] if data else None)

    def _code(self, name):
        _, data = self.imap.response(name)
        return number(data[-1] if data else None)

    def search(self, criteria):
        typ, data = self.imap.uid("SEARCH", criteria)
        if typ != "OK":
            raise MailError("Die Suche im Postfach ging nicht.")
        uids = []
        for line in data or []:
            if isinstance(line, bytes):
                uids.extend(int(x) for x in line.split() if x.isdigit())
        return uids

    def fetch(self, uids, item):
        """(uid, arrival, bytes) for each mail - item says what, always a BODY.PEEK that leaves it unread."""
        if not uids:
            return []
        typ, data = self.imap.uid("FETCH", ",".join(str(u) for u in uids), f"(UID INTERNALDATE {item})")
        if typ != "OK":
            raise MailError("Mails ließen sich nicht abholen.")
        out, current = [], None
        for part in data or []:
            if isinstance(part, tuple):
                if current:
                    out.append(current)
                current = [fetched_uid(part[0]), fetched_date(part[0]), part[1]]
            elif isinstance(part, bytes) and current is not None:
                # What came after the literal - ")" or the rest of the items
                current[0] = current[0] or fetched_uid(part)
                current[1] = current[1] or fetched_date(part)
                out.append(current)
                current = None
        if current:
            out.append(current)
        return [(u, d, raw) for u, d, raw in out if u is not None]

    def present(self, uids):
        """Which of these mails are still in the open folder."""
        return set(self.search("UID " + ",".join(str(u) for u in sorted(uids)))) if uids else set()


def number(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def fetched_uid(data):
    m = re.search(rb"\bUID (\d+)", data)
    return int(m.group(1)) if m else None


def fetched_date(data):
    m = re.search(rb'INTERNALDATE "([^"]+)"', data)
    return internal_date(m.group(1).decode("ascii", "replace")) if m else None


def internal_date(text):
    """'15-Oct-2026 19:02:11 +0000' - month names in English, whatever the locale."""
    m = re.match(r"\s*(\d{1,2})-([A-Za-z]{3})-(\d{4}) (\d{2}):(\d{2}):(\d{2}) ([+-])(\d{2})(\d{2})", text or "")
    if not m or m.group(2).title() not in MONTHS:
        return None
    day, mon, year, hh, mm, ss, sign, oh, om = m.groups()
    offset = timedelta(hours=int(oh), minutes=int(om)) * (1 if sign == "+" else -1)
    return datetime(int(year), MONTHS.index(mon.title()) + 1, int(day), int(hh), int(mm), int(ss), tzinfo=timezone(offset))


def imap_date(day):
    return f"{day.day}-{MONTHS[day.month - 1]}-{day.year}"


def refused(error):
    """Gmail said no to the password - not the network, not the server."""
    low = str(error).lower()
    return "authenticationfailed" in low or "invalid credentials" in low or "application-specific password" in low


def explain(error, host):
    text = str(error)
    low = text.lower()
    if refused(error):
        return ("Gmail lehnt die Anmeldung ab – es braucht ein App-Passwort (16 Buchstaben von "
                "myaccount.google.com/apppasswords), nicht das normale Passwort. Neu setzen: sh wecker/melder/install.sh passwort")
    if "imap" in low and ("disabled" in low or "not enabled" in low):
        return "In Gmail ist IMAP ausgeschaltet: Einstellungen → Weiterleitung und POP/IMAP → IMAP aktivieren."
    if isinstance(error, MailError):
        return text
    if isinstance(error, socket.gaierror):
        return f"{host} ist nicht erreichbar – hat der Pi Internet?"
    if isinstance(error, TimeoutError):
        return f"{host} antwortet nicht."
    if isinstance(error, ssl.SSLError):
        return f"Die sichere Verbindung zu {host} kam nicht zustande."
    if isinstance(error, OSError):
        return f"{host} ist nicht erreichbar: {error.strerror or text}"
    return "Das Postfach ließ sich nicht lesen: " + text


# ── Push ──────────────────────────────────────────────────────────────────────

class Ntfy:
    """Push messages to the phone: the Melder posts to a topic, the ntfy app on the phone is
    subscribed to it - the same topic as the TISS Bot's."""

    def __init__(self, cfg):
        self.cfg = cfg

    def send(self, title, message, priority=3, tags=(), click=None):
        body = {"topic": self.cfg["topic"], "title": title, "message": message, "priority": priority}
        if tags:
            body["tags"] = list(tags)
        if click:
            body["click"] = click
        request = urllib.request.Request(self.cfg["server"].rstrip("/") + "/", method="POST",
                                         data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
                                         headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request, timeout=15, context=TLS) as answer:
                answer.read()
        except urllib.error.HTTPError as e:
            log(f"Push abgelehnt: ntfy antwortet {e.code}")
            return False
        except (urllib.error.URLError, OSError) as e:
            log(f"Push ging nicht raus: {getattr(e, 'reason', e)}")
            return False
        log(f"Push: {title}")
        return True


# ── The Pi's health ───────────────────────────────────────────────────────────

class Probe:
    """Readings of the Pi - None for whatever this machine does not have."""

    def __init__(self, cfg):
        self.cfg = cfg

    def temperature(self):
        try:
            return int(Path(self.cfg["thermal"]).read_text().strip()) / 1000
        except (OSError, ValueError):
            return None

    def free_gb(self):
        try:
            return shutil.disk_usage(self.cfg["disk"]).free / 2 ** 30
        except OSError:
            return None

    def undervoltage(self):
        """The firmware's alarm (hwmon "rpi_volt"): the power supply gives too little."""
        try:
            for d in Path(self.cfg["hwmon"]).iterdir():
                try:
                    if (d / "name").read_text().strip() == "rpi_volt":
                        return (d / "in0_lcrit_alarm").read_text().strip() == "1"
                except OSError:
                    continue
        except OSError:
            pass
        return None

    def listens(self, address):
        """Does a service take connections there - AdGuard on its DNS port, the Wecker on its page.
        Only a connection, no request: nothing lands in AdGuard's query log or the Wecker's journal.
        (systemctl is no help here: a service without an account of its own cannot ask systemd.)"""
        host, port = address.rsplit(":", 1)
        try:
            with socket.create_connection((host, int(port)), timeout=3):
                return True
        except OSError:
            return False


# ── The Melder ────────────────────────────────────────────────────────────────

class Store:
    """state.json - written whole and then put in place, so a stop in between loses nothing."""

    def __init__(self, path):
        self.path = Path(path)

    def load(self):
        try:
            with open(self.path, encoding="utf-8") as f:
                state = json.load(f)
        except FileNotFoundError:
            state = {}
        except (OSError, ValueError) as e:
            log(f"{self.path.name} war nicht lesbar ({e}) – ich fange neu an, was im Postfach liegt, gilt als bekannt")
            try:
                os.replace(self.path, self.path.with_name(self.path.name + ".kaputt"))
            except OSError:
                pass
            state = {}
        for key, empty in (("inbox", {}), ("sent", {}), ("requests", []), ("mail", {}), ("health", {})):
            state.setdefault(key, empty)
        return state

    def save(self, state):
        new = self.path.with_name(self.path.name + ".neu")
        with open(new, "w", encoding="utf-8") as f:
            json.dump(state, f, ensure_ascii=False, indent=1)
        os.replace(new, self.path)


class Melder:
    def __init__(self, cfg, password, store, probe=None, ntfy=None):
        self.cfg = cfg
        self.password = password
        self.store = store
        self.state = store.load()
        self.probe = probe or Probe(cfg["health"])
        self.ntfy = ntfy or Ntfy(cfg["ntfy"])
        self.readings = {}
        self.told_health = False

    def push(self, title, message, priority=3, tags=(), click=None):
        return self.ntfy.send(title, message, priority, tags, click)

    def requests_open(self):
        return [r for r in self.state["requests"] if r["status"] == "open"]

    # One round: the mailbox, then the pushes that are due, then the Pi
    def cycle(self, now):
        mail_ok = False
        try:
            box = Mailbox(self.cfg["imap"], self.password)
            try:
                self.read_inbox(box, now)
                self.read_sent(box, now)
            finally:
                box.close()
            mail_ok = True
        except (imaplib.IMAP4.error, OSError, MailError) as e:
            self.mail_failed(now, e)
        except Exception as e:
            # A fault of the Melder's own: it counts like a mailbox that cannot be read, so it is told
            log("Fehler beim Lesen des Postfachs:\n" + traceback.format_exc(limit=6))
            self.mail_failed(now, MailError(f"Fehler im Melder ({type(e).__name__}) – siehe journalctl -u melder."))
        if mail_ok:
            self.mail_back(now)
        self.announce(now, mail_ok)
        self.remind(now)
        self.health(now)
        self.heartbeat(now)
        self.prune(now)
        self.store.save(self.state)

    # ── Reading the mailbox

    def read_inbox(self, box, now):
        inbox = self.state["inbox"]
        validity, uidnext, count = box.examine("INBOX")
        if inbox.get("validity") != validity:
            # The first time - or the folder was rebuilt and its UIDs start over: what lies there now
            # counts as known, only what comes after it is new
            if "validity" not in inbox:
                log(f"Posteingang: {count} Mails – was schon da ist, gilt als bekannt")
                if "started" not in self.state:
                    self.state["started"] = iso(now)
                    self.state["welcome"] = "pending"
            else:
                log("Der Posteingang wurde neu aufgebaut (UIDVALIDITY) – was dort liegt, gilt als bekannt")
                for r in self.requests_open():
                    r["uid"] = None
            inbox.clear()
            inbox.update(validity=validity, lastUid=uidnext - 1 if uidnext else max(box.search("ALL"), default=0))
            return

        # Whatever came after the newest mail seen - "n:*" answers with the newest mail even when
        # nothing came after n, hence the filter
        last = inbox["lastUid"]
        uids = sorted(u for u in box.search(f"UID {last + 1}:*") if u > last)
        known = {r["msg"] for r in self.state["requests"]}
        found = []
        for i in range(0, len(uids), BATCH):
            for uid, arrived, raw in box.fetch(uids[i:i + BATCH], f"BODY.PEEK[]<0.{FETCH_LIMIT}>"):
                try:
                    request = recognize(parse_mail(raw), self.cfg["products"])
                except Exception as e:
                    log(f"Mail {uid} war nicht lesbar: {type(e).__name__}")
                    continue
                if not request or request["msg"] in known:
                    continue
                known.add(request["msg"])
                request.update(id=secrets.token_hex(4), uid=uid, validity=validity, received=iso(arrived or now),
                               seen=iso(now), status="open", announced=None, lastPush=None, pushes=0, missing=0)
                self.state["requests"].append(request)
                found.append(request)
        inbox["lastUid"] = max([last, uidnext - 1] + uids)
        if uids:
            log(f"{len(uids)} neue Mail(s), davon {len(found)} Code-Anfrage(n)"
                + "".join(f"; {r['id']}: {r['wants'] or '?'} {r['product'] or 'Plugin unklar'}" for r in found))
        self.check_present(box, now)

    def check_present(self, box, now):
        """A request whose mail has left the inbox - archived or deleted in Gmail - is done. Twice
        in a row, so that a server's hiccup closes nothing."""
        validity = self.state["inbox"]["validity"]
        tracked = [r for r in self.requests_open() if r.get("uid") and r.get("validity") == validity]
        if not tracked:
            return
        there = box.present([r["uid"] for r in tracked])
        for r in tracked:
            if r["uid"] in there:
                r["missing"] = 0
                continue
            r["missing"] = r.get("missing", 0) + 1
            if r["missing"] >= 2:
                self.close(r, now, "gone")

    def read_sent(self, box, now):
        """Answers among the sent mails: In-Reply-To and References name the mail they answer."""
        sent = self.state["sent"]
        name = box.sent_folder()
        if not name:
            if not sent.get("none"):
                log("Kein Gesendet-Ordner gefunden – Antworten erkenne ich so nicht")
                sent["none"] = True
            return
        sent.pop("none", None)
        validity, uidnext, _ = box.examine(name)
        if sent.get("folder") != name or sent.get("validity") != validity:
            # The first time, or the folder is a new one: with no open request nothing in it matters;
            # otherwise look at what went out since the oldest open one came in
            sent.update(folder=name, validity=validity, lastUid=0)
            waiting = self.requests_open()
            if not waiting:
                sent["lastUid"] = max(uidnext - 1, 0)
                return
            oldest = min(parse_time(r["received"]) for r in waiting) - timedelta(days=1)
            uids = box.search("SINCE " + imap_date(oldest))
        else:
            last = sent["lastUid"]
            uids = sorted(u for u in box.search(f"UID {last + 1}:*") if u > last)

        answered = set()
        for i in range(0, len(uids), 50):
            for _, _, raw in box.fetch(uids[i:i + 50], "BODY.PEEK[HEADER.FIELDS (IN-REPLY-TO REFERENCES)]"):
                answered.update(digest(ref) for ref in MESSAGE_ID.findall(raw.decode("latin-1")))
        sent["lastUid"] = max([sent["lastUid"], uidnext - 1] + uids)

        closed = [r for r in self.requests_open() if r["msg"] in answered]
        for r in closed:
            self.close(r, now, "reply")
        # The same request twice from one device: one answer is enough
        for c in closed:
            for r in self.requests_open():
                if (r["device"], r["product"], r["wants"]) == (c["device"], c["product"], c["wants"]):
                    self.close(r, now, "same")

    def close(self, request, now, why):
        request.update(status="dropped" if why == "7-days" else "done", closed=iso(now), why=why)
        log(f"Anfrage {request['id']} erledigt: {WHY[why]}")

    def mail_failed(self, now, error):
        mail = self.state["mail"]
        why = explain(error, self.cfg["imap"]["host"])
        if not mail.get("failingSince"):
            mail["failingSince"] = iso(now)
            log(f"Postfach: {why} ({type(error).__name__}: {error})")
        elif mail.get("why") != why:
            log(f"Postfach: {why}")
        mail["why"] = why
        patience = timedelta(minutes=self.cfg["health"]["mailFailMinutes"])
        if not mail.get("alerted") and now - parse_time(mail["failingSince"]) >= patience:
            if self.push("Melder kann das Postfach nicht lesen", why + " Solange melde ich keine Code-Anfragen.",
                         priority=4, tags=["warning"]):
                mail["alerted"] = True

    def mail_back(self, now):
        mail = self.state["mail"]
        if mail.get("failingSince"):
            if mail.get("alerted") and not self.push("Postfach wieder lesbar", "Ich melde Code-Anfragen wieder.",
                                                      tags=["white_check_mark"]):
                return   # once more next round
            log("Postfach wieder lesbar")
        mail.clear()
        mail["lastOk"] = iso(now)

    # ── Pushes about requests

    def announce(self, now, mail_ok):
        if self.state.get("welcome") == "pending":
            every = self.cfg["remindHours"]
            if self.push("Melder läuft", f"Ich melde neue Code-Anfragen hier und erinnere alle {every} Stunden, "
                                         "bis sie beantwortet sind.", tags=["white_check_mark"]):
                self.state["welcome"] = "sent"
        waiting = [r for r in self.requests_open() if not r["announced"]]
        if not mail_ok:
            # Read before the sent mail could be looked at: give the answer a moment to show
            waiting = [r for r in waiting if now - parse_time(r["seen"]) >= timedelta(minutes=10)]
        if not waiting:
            return
        if len(waiting) == 1:
            title, message = label(waiting[0]), TAP
        else:
            title, message = f"{len(waiting)} neue Code-Anfragen", "\n".join(label(r) for r in waiting) + "\n" + TAP
        if self.push(title, message, tags=["key"], click=self.cfg["ntfy"]["click"]):
            for r in waiting:
                r.update(announced=iso(now), lastPush=iso(now), pushes=1)

    def remind(self, now):
        """Every 12 hours one push for all that are open - one due at night waits for the morning."""
        waiting = [r for r in self.requests_open() if r["announced"]]
        if not waiting or self.quiet(now):
            return
        every = timedelta(hours=self.cfg["remindHours"])
        limit = timedelta(days=self.cfg["giveUpDays"])
        last = [r for r in waiting if now - parse_time(r["received"]) >= limit]
        if not last and all(now - parse_time(r["lastPush"]) < every for r in waiting):
            return

        def since(r):
            return age(now - parse_time(r["received"]))

        if len(waiting) == 1:
            r = waiting[0]
            title = ("Letzte Erinnerung: " if last else "Noch offen: ") + label(r)
            message = f"Seit {since(r)} ohne Antwort. " + ("Ab jetzt erinnere ich nicht mehr daran." if last else TAP)
        else:
            title = f"Noch offen: {len(waiting)} Code-Anfragen"
            message = "\n".join(f"{label(r)} – seit {since(r)}" + (" (letzte Erinnerung)" if r in last else "")
                                for r in waiting) + "\n" + TAP
        if self.push(title, message, tags=["hourglass_flowing_sand"], click=self.cfg["ntfy"]["click"]):
            for r in waiting:
                r["lastPush"] = iso(now)
                r["pushes"] += 1
            for r in last:
                self.close(r, now, "7-days")

    def quiet(self, now):
        q = self.cfg.get("quiet") or {}
        start, end = clock(q.get("from")), clock(q.get("to"))
        if start is None or end is None or start == end:
            return False
        t = now.time()
        return (t >= start or t < end) if start > end else (start <= t < end)

    def prune(self, now):
        """Done requests are forgotten after 30 days."""
        self.state["requests"] = [r for r in self.state["requests"]
                                  if r["status"] == "open" or now - parse_time(r["closed"]) < timedelta(days=30)]

    # ── The Pi

    def health(self, now):
        cfg, probe = self.cfg["health"], self.probe
        r = {"temp": probe.temperature(), "free": probe.free_gb(), "power": probe.undervoltage()}
        for key in ("adguard", "wecker"):
            where = (cfg.get(key) or {}).get("tcp")
            r[key] = probe.listens(where) if where else None
        self.readings = r
        if not self.told_health:
            log("Gesundheit: " + self.summary())
            self.told_health = True

        if r["temp"] is not None:
            warn = cfg["tempWarn"]
            hot = r["temp"] >= (warn - 5 if self.alerted("temp") else warn)   # cooler again only 5 °C below
            self.observe("temp", hot, f"Pi ist heiß: {num(r['temp'], 0)} °C",
                         "Ab 80 °C bremst er sich selbst. Hat er genug Luft?",
                         f"Pi wieder kühl: {num(r['temp'], 0)} °C", need_bad=2, need_good=3)
        if r["free"] is not None:
            low = r["free"] < cfg["diskMinGb"] * (1.25 if self.alerted("disk") else 1)
            self.observe("disk", low, f"Pi: nur noch {num(r['free'])} GB frei",
                         "Auf der Speicherkarte wird es eng – meist sind es Protokolle oder alte Updates.",
                         f"Pi: wieder {num(r['free'])} GB frei")
        if r["power"] is not None:
            self.observe("power", r["power"], "Pi: Unterspannung",
                         "Das Netzteil liefert zu wenig Strom – das offizielle Netzteil nehmen, keinen USB-Anschluss.",
                         "Pi: Spannung wieder in Ordnung", need_good=15)
        if r.get("adguard") is not None:
            self.observe("adguard", not r["adguard"], "AdGuard antwortet nicht",
                         "Der DNS-Dienst auf dem Pi nimmt keine Verbindung an – Geräte mit Tailscale finden so keine Namen.",
                         "AdGuard antwortet wieder", need_bad=2)
        if r.get("wecker") is not None:
            self.observe("wecker", not r["wecker"], "Wecker-Dienst läuft nicht",
                         "Die Weck-Seite nimmt keine Verbindung an – vom iPhone lässt sich der PC so nicht wecken.",
                         "Wecker-Dienst läuft wieder", need_bad=2)

    def alerted(self, key):
        return self.state["health"].get(key, {}).get("alerted", False)

    def observe(self, key, bad, problem, detail, fine, need_bad=1, need_good=1):
        """A push when it has been wrong need_bad times in a row, and one when it has been right again need_good times."""
        h = self.state["health"].setdefault(key, {"alerted": False, "bad": 0, "good": 0})
        if bad:
            h["bad"], h["good"] = h["bad"] + 1, 0
            if not h["alerted"] and h["bad"] >= need_bad and self.push(problem, detail, priority=4, tags=["warning"]):
                h["alerted"] = True
        else:
            h["good"], h["bad"] = h["good"] + 1, 0
            if h["alerted"] and h["good"] >= need_good and self.push(fine, "Alles wieder in Ordnung.", tags=["white_check_mark"]):
                h["alerted"] = False

    def heartbeat(self, now):
        beat = self.cfg["health"].get("heartbeat")
        if not beat:
            return
        at = clock(beat.get("time", "10:00"))
        today = now.date().isoformat()
        if now.weekday() != beat.get("weekday", 6) or at is None or now.time() < at or self.state.get("heartbeat") == today:
            return
        if self.push("Pi läuft", self.summary(), priority=2, tags=["green_heart"]):
            self.state["heartbeat"] = today

    def summary(self, everything=True):
        r = self.readings
        parts = []
        if r.get("temp") is not None:
            parts.append(f"{num(r['temp'], 0)} °C")
        if r.get("free") is not None:
            parts.append(f"{num(r['free'])} GB frei")
        if r.get("power") is not None:
            parts.append("Unterspannung!" if r["power"] else "Strom ok")
        if r.get("adguard") is not None:
            parts.append("AdGuard ok" if r["adguard"] else "AdGuard gestört")
        if r.get("wecker") is not None:
            parts.append("Wecker ok" if r["wecker"] else "Wecker gestört")
        if not everything:
            return " · ".join(parts)
        parts.append("Postfach gestört" if self.state["mail"].get("failingSince") else "Postfach ok")
        n = len(self.requests_open())
        parts.append("keine Anfrage offen" if n == 0 else f"{n} Anfrage{'n' if n > 1 else ''} offen")
        return " · ".join(parts)


def label(r):
    kind = KIND.get(r["wants"], "Code-Anfrage")
    return f"{kind} für {r['product']}" if r["product"] else f"{kind} (Plugin unklar)"


def age(delta):
    minutes = max(int(delta.total_seconds() // 60), 1)
    if minutes < 60:
        return f"{minutes} Minute{'n' if minutes != 1 else ''}"
    hours = minutes // 60
    if hours < 48:
        return f"{hours} Stunde{'n' if hours != 1 else ''}"
    return f"{hours // 24} Tagen"


def num(value, places=1):
    return f"{value:.{places}f}".replace(".", ",")


def clock(text):
    try:
        hh, mm = str(text).split(":")
        return datetime.strptime(f"{int(hh):02d}:{int(mm):02d}", "%H:%M").time()
    except (TypeError, ValueError):
        return None


def iso(t):
    return t.isoformat(timespec="seconds")


def parse_time(text):
    return datetime.fromisoformat(text)


# ── Starting ──────────────────────────────────────────────────────────────────

def load_config(path):
    with open(path, encoding="utf-8") as f:
        given = json.load(f)
    cfg = {}
    for key, default in DEFAULTS.items():
        value = given.get(key, default)
        cfg[key] = {**default, **value} if isinstance(default, dict) and isinstance(value, dict) else value
    for key in ("adguard", "wecker", "heartbeat"):   # one level further: these may also be switched off with null
        if isinstance(cfg["health"].get(key), dict) and isinstance(DEFAULTS["health"][key], dict):
            cfg["health"][key] = {**DEFAULTS["health"][key], **cfg["health"][key]}
    if not cfg["imap"]["user"]:
        raise SystemExit("imap.user fehlt – das Postfach, z. B. support.autocreate@gmail.com")
    topic = cfg["ntfy"]["topic"]
    if len(topic) < 12 or not re.fullmatch(r"[A-Za-z0-9_-]+", topic):
        raise SystemExit("ntfy.topic: mindestens 12 Zeichen aus Buchstaben, Ziffern, - und _")
    if not cfg["products"]:
        raise SystemExit("products fehlt – die Namen der Plugins, z. B. [\"Laser Create\", \"SW to Laser\"]")
    return cfg


def read_secret(path):
    return "".join(Path(path).read_text(encoding="utf-8").split())   # Google shows it in groups of four


def check(cfg, password):
    """Logs in once and says what it sees: folders and how many mails - no mail is read, no push sent."""
    imap = cfg["imap"]
    print(f"Anmeldung bei {imap['host']} als {imap['user']} …", flush=True)
    try:
        box = Mailbox(imap, password)
    except (imaplib.IMAP4.error, OSError, MailError) as e:
        print("Geht nicht: " + explain(e, imap["host"]))
        return 2 if refused(e) else 1   # 2: the password is wrong - install.sh does not keep it
    try:
        _, _, count = box.examine("INBOX")
        line = f"Klappt. Posteingang: {count} Mails"
        sent = box.sent_folder()
        if sent:
            _, _, n = box.examine(sent)
            line += f", Gesendet-Ordner: {sent} ({n} Mails)"
        else:
            line += " – aber kein Gesendet-Ordner gefunden, Antworten erkenne ich so nicht"
        print(line)
    except (imaplib.IMAP4.error, OSError, MailError) as e:
        print("Geht nicht: " + explain(e, imap["host"]))
        return 1
    finally:
        box.close()
    melder = Melder.__new__(Melder)   # only for the readings - no state, no push
    melder.cfg, melder.probe, melder.state = cfg, Probe(cfg["health"]), {"mail": {}, "requests": [], "health": {}}
    melder.readings, melder.told_health = {}, True
    melder.push = lambda *a, **k: False
    melder.health(datetime.now().astimezone())
    print("Pi: " + melder.summary(everything=False))
    return 0


def main(argv=None):
    credentials = os.environ.get("CREDENTIALS_DIRECTORY")
    state_dir = (os.environ.get("STATE_DIRECTORY") or "").split(os.pathsep)[0]   # systemd may list several
    parser = argparse.ArgumentParser(description="Meldet Code-Anfragen aus dem Postfach per Push.")
    parser.add_argument("--config", default=os.path.join(credentials, "config") if credentials else "/etc/melder/melder.json")
    parser.add_argument("--password-file", default=os.path.join(credentials, "imap-password") if credentials else "/etc/melder/imap-password")
    parser.add_argument("--state", default=os.path.join(state_dir, "state.json") if state_dir else "state.json")
    parser.add_argument("--check", action="store_true", help="einmal anmelden und sagen, was er sieht")
    parser.add_argument("--once", action="store_true", help="eine Runde, dann Schluss")
    args = parser.parse_args(argv)

    cfg = load_config(args.config)
    password = read_secret(args.password_file)
    if args.check:
        return check(cfg, password)

    melder = Melder(cfg, password, Store(args.state))
    log(f"Melder für {cfg['imap']['user']}: alle {cfg['intervalSeconds']} s, Push über {cfg['ntfy']['server']}")
    while True:
        begun = time.monotonic()
        try:
            melder.cycle(datetime.now().astimezone())
        except Exception:
            log("Fehler in der Runde:\n" + traceback.format_exc(limit=6))
        if args.once:
            return 0
        time.sleep(max(5.0, cfg["intervalSeconds"] - (time.monotonic() - begun)))


if __name__ == "__main__":
    sys.exit(main())
