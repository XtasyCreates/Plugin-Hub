#!/usr/bin/env python3
"""TISS-Helfer - the TISS Bot's calendar and reminders, from the Raspberry Pi, day and night.

The TISS Bot runs on the laptop. After every reading of TISS it sends the Pi what the phone
needs (POST /api/lieferung): the calendar feed it built - the exams the user is registered for,
each with "Abmeldung endet" - and every exam of the user's courses with its dates. The Pi keeps
the latest delivery and
  - serves the feed (GET /kalender/<token>.ics), so the phone's calendar never has to wake the
    laptop;
  - reminds through ntfy, on the bot's own topic: a day before deregistering ends (exams the user
    is registered for), an hour before a registration opens (courses with no registration for a
    coming exam) and a new exam date as soon as it shows up. Nights are quiet: what would be due
    between 22:00 and 8:00 comes at 21:00 before - a new exam date at 8:00 after.
It never reads TISS itself. Nothing but Python's standard library.

    tiss_helfer.py --config /etc/wecker.json --state /var/lib/tiss-helfer

Tailscale Serve hands https://<pi>/tiss on to 127.0.0.1:8081 (tailscale serve --set-path /tiss).
The settings are the Wecker's (see wecker.example.json): "allow", the Tailscale logins that may
use it, and "pc", the laptop; an optional part "tiss" changes listen, base and from. Deliveries
come from the laptop only: Tailscale Serve says which address sent a request (X-Forwarded-For -
Serve sets it, a device cannot), and the Pi asks Tailscale for the laptop's addresses. They
also carry the header X-Tiss-Bot, which no web page can send without asking first.
"""

import argparse
import ipaddress
import json
import os
import re
import ssl
import subprocess
import threading
import time
import urllib.error
import urllib.request
from collections import namedtuple
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from zoneinfo import ZoneInfo

HERE = Path(__file__).resolve().parent
VIENNA = ZoneInfo("Europe/Vienna")
TLS = ssl.create_default_context()

NIGHT_FROM, NIGHT_TO, EVENING = 22, 8, 21   # quiet from 22:00 to 8:00; moved forward, a reminder comes at 21:00
TICK = float(os.environ.get("TISS_HELFER_TICK", "20"))      # seconds between two looks at what is due
SHIFT = float(os.environ.get("TISS_HELFER_SHIFT", "0"))     # tests: seconds this process's clock runs ahead
RETRY = timedelta(minutes=2)                # after ntfy failed, before the next try
STALE = timedelta(hours=26)                 # no delivery for longer: the page says so
MAX_BODY = 2_000_000
TAILSCALE = os.environ.get("TISS_HELFER_TAILSCALE", "tailscale")
WEEKDAYS = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"]

TOKEN = re.compile(r"^[A-Za-z0-9_-]{8,128}$")
TOPIC = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
TISS_LINK = re.compile(r"^https://tiss\.tuwien\.ac\.at/[\x21-\x7e]*$")
DAY = re.compile(r"(\d{2})\.(\d{2})\.(\d{4})")
FRACTION = re.compile(r"(\.\d{6})\d+")


def clock():
    return datetime.now(timezone.utc) + timedelta(seconds=SHIFT)


def load(path):
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
    cfg.setdefault("allow", [])
    cfg.setdefault("pc", {})
    tiss = cfg.setdefault("tiss", {})
    tiss.setdefault("listen", "127.0.0.1:8081")
    tiss.setdefault("base", "/tiss")
    tiss["base"] = tiss["base"].rstrip("/")
    tiss.setdefault("from", [])
    return cfg


class Refused(Exception):
    """A request the helper turns down, with the reason in the user's words."""


# ── Times ─────────────────────────────────────────────────────────────────────

def parse_time(value):
    """An ISO time as the bot writes it ("2026-10-28T10:00:00+01:00") - None for anything else."""
    if not isinstance(value, str) or not value:
        return None
    try:
        at = datetime.fromisoformat(FRACTION.sub(r"\1", value.replace("Z", "+00:00")))
    except ValueError:
        return None
    return at if at.tzinfo else at.replace(tzinfo=VIENNA)


def iso(at):
    return at.isoformat(timespec="seconds") if at else None


def local(at):
    return at.astimezone(VIENNA)


def before_night(at):
    """Due in the night: at 21:00 before it instead."""
    at = local(at)
    if at.hour >= NIGHT_FROM:
        return at.replace(hour=EVENING, minute=0, second=0, microsecond=0)
    if at.hour < NIGHT_TO:
        return (at - timedelta(days=1)).replace(hour=EVENING, minute=0, second=0, microsecond=0)
    return at


def after_night(at):
    """Due in the night: at 8:00 after it instead."""
    at = local(at)
    if at.hour >= NIGHT_FROM:
        return (at + timedelta(days=1)).replace(hour=NIGHT_TO, minute=0, second=0, microsecond=0)
    if at.hour < NIGHT_TO:
        return at.replace(hour=NIGHT_TO, minute=0, second=0, microsecond=0)
    return at


def day_of(exam):
    m = DAY.search(exam.get("date") or "")
    if not m:
        return None
    try:
        return datetime(int(m.group(3)), int(m.group(2)), int(m.group(1)), tzinfo=VIENNA)
    except ValueError:
        return None


def coming(exam, now):
    """Held today or later."""
    start = parse_time(exam.get("start")) or day_of(exam)
    return start is not None and local(start).date() >= local(now).date()


def moment(at, now):
    """'heute um 09:00', 'morgen um 23:59', 'am Do 10.12. um 12:00'."""
    at, today = local(at), local(now).date()
    if at.date() == today:
        day = "heute"
    elif at.date() == today + timedelta(days=1):
        day = "morgen"
    else:
        day = f"am {WEEKDAYS[at.weekday()]} {at:%d.%m.}"
    return f"{day} um {at:%H:%M}"


def held(exam):
    """'Mi 09.12., 16:00 - 18:00' - when the exam is."""
    day = day_of(exam)
    text = f"{WEEKDAYS[day.weekday()]} {day:%d.%m.}" if day else exam.get("date") or "?"
    return f"{text}, {exam['time']}" if exam.get("time") else text


# ── A delivery ────────────────────────────────────────────────────────────────

def text(value, limit=300):
    return value.strip()[:limit] if isinstance(value, str) else ""


def check(delivery):
    """The delivery as the bot sends it, cut down to what the helper uses - or Refused."""
    if not isinstance(delivery, dict) or delivery.get("app") != "TissBot":
        raise Refused("Das ist keine Lieferung des TISS Bots.")
    feed = delivery.get("feed") if isinstance(delivery.get("feed"), dict) else {}
    token, ics = feed.get("token"), feed.get("ics")
    if not isinstance(token, str) or not TOKEN.match(token):
        raise Refused("Der Schlüssel des Kalender-Abos fehlt.")
    if not isinstance(ics, str) or not ics.startswith("BEGIN:VCALENDAR") or len(ics) > 1_000_000:
        raise Refused("Das Kalender-Abo ist kein Kalender.")
    push = delivery.get("push") if isinstance(delivery.get("push"), dict) else {}
    server, topic = push.get("server"), push.get("topic")
    if not isinstance(server, str) or not server.startswith("https://") or len(server) > 200:
        raise Refused("Der ntfy-Server fehlt.")
    if not isinstance(topic, str) or not TOPIC.match(topic):
        raise Refused("Der Push-Name fehlt.")
    courses = delivery.get("courses")
    if not isinstance(courses, list) or len(courses) > 300:
        raise Refused("Die Lehrveranstaltungen fehlen.")

    clean = []
    for course in courses:
        if not isinstance(course, dict) or not text(course.get("id")):
            raise Refused("Eine Lehrveranstaltung hat keine id.")
        exams = []
        for exam in course.get("exams") if isinstance(course.get("exams"), list) else []:
            if not isinstance(exam, dict) or not text(exam.get("key")):
                continue
            link = exam.get("link")
            exams.append({
                "key": text(exam["key"], 64),
                "name": text(exam.get("name")),
                "date": text(exam.get("date"), 20),
                "time": text(exam.get("time"), 40),
                "start": iso(parse_time(exam.get("start"))),
                "regOpens": iso(parse_time(exam.get("regOpens"))),
                "regEnds": iso(parse_time(exam.get("regEnds"))),
                "deregEnds": iso(parse_time(exam.get("deregEnds"))),
                # True / False - or None: the bot has not read the user's own registrations
                "registered": exam.get("registered") if isinstance(exam.get("registered"), bool) else None,
                "inTiss": exam.get("inTiss") is not False,
                "botPlanned": exam.get("botPlanned") is True,
                "link": link if isinstance(link, str) and TISS_LINK.match(link) else "https://tiss.tuwien.ac.at/",
            })
        clean.append({
            "id": text(course["id"], 40),
            "nr": text(course.get("nr"), 20),
            "label": text(course.get("label")) or text(course.get("nr")) or text(course["id"], 40),
            # When the bot last read the course from TISS - None: never, so nothing to go by yet
            "read": iso(parse_time(course.get("read"))),
            "exams": exams[:300],
        })
    return {
        "app": "TissBot",
        "at": iso(parse_time(delivery.get("at"))),
        "feed": {"token": token, "ics": ics},
        "push": {"on": push.get("on") is not False, "server": server.rstrip("/"), "topic": topic},
        "courses": clean,
    }


def news(known, delivery, now):
    """
    The exam dates that came up since the courses were first delivered - each once, ever:
    one that drops out of TISS for a reading and comes back is not new. A course seen for the
    first time is only the starting point.
    """
    found = []
    for course in delivery["courses"]:
        if not course["read"]:
            continue
        seen = known.get(course["id"])
        first = seen is None
        if first:
            seen = known[course["id"]] = {}
        for exam in course["exams"]:
            if exam["key"] in seen:
                continue
            seen[exam["key"]] = iso(now)
            if not first and coming(exam, now):
                found.append((course, exam))
    return found


# ── Reminders ─────────────────────────────────────────────────────────────────

Reminder = namedtuple("Reminder", "kind key at until course exam")


def plan(delivery, now):
    """Every reminder the delivery calls for whose moment has not passed - the earliest first."""
    out = []
    for course in (delivery or {}).get("courses", []):
        exams = course["exams"]
        # Registered for a coming exam of the course: its other dates need no reminder to register
        booked = any(e["registered"] is True and coming(e, now) for e in exams)
        for exam in exams:
            dereg = parse_time(exam["deregEnds"])
            if exam["registered"] is True and dereg and dereg > now:
                out.append(Reminder("dereg", f"dereg|{course['id']}|{exam['key']}|{exam['deregEnds']}",
                                    before_night(local(dereg) - timedelta(days=1)), dereg, course, exam))
            opens = parse_time(exam["regOpens"])
            if exam["inTiss"] and opens and opens > now and exam["registered"] is not True and not booked:
                out.append(Reminder("opens", f"opens|{course['id']}|{exam['key']}|{exam['regOpens']}",
                                    opens - timedelta(hours=1), opens, course, exam))
    out.sort(key=lambda r: r.at)
    return out


def message(key, kind, title, body, click, tag, priority, at=None, until=None):
    return {"key": key, "kind": kind, "title": title, "message": body, "click": click, "tag": tag,
            "priority": priority, "at": iso(at), "until": iso(until)}


def render(reminder, now):
    """The push for a reminder, worded for the moment it goes out."""
    course, exam = reminder.course, reminder.exam
    if reminder.kind == "dereg":
        return message(reminder.key, "dereg", f"Abmeldung endet {moment(reminder.until, now)}: {course['label']}",
                       f"{exam['name']} am {held(exam)}.", exam["link"], "hourglass_flowing_sand", 4,
                       reminder.at, reminder.until)
    planned = " Der Bot ist dafür eingeplant." if exam["botPlanned"] else ""
    return message(reminder.key, "opens", f"Anmeldung öffnet {moment(reminder.until, now)}: {course['label']}",
                   f"{exam['name']} am {held(exam)}.{planned}", exam["link"], "alarm_clock", 4,
                   reminder.at, reminder.until)


def new_date(course, exam, now):
    """The push for an exam date that came up - nights wait until 8:00."""
    opens, ends = parse_time(exam["regOpens"]), parse_time(exam["regEnds"])
    if opens and opens > now:
        when = moment(opens, now)
        period = f" Anmeldung ab {when[3:] if when.startswith('am ') else when}."
    elif ends and ends > now:
        period = f" Anmeldung offen bis {moment(ends, now)}."
    else:
        period = ""
    # Worth telling until the exam is held
    day = day_of(exam)
    until = parse_time(exam["start"]) or (day + timedelta(days=1) if day else None)
    return message(f"new|{course['id']}|{exam['key']}", "new", f"Neuer Prüfungstermin: {course['label']}",
                   f"{exam['name']} am {held(exam)}.{period}", exam["link"], "new", 3, after_night(now), until)


def due(data, now):
    """What is to go out now and has not."""
    sent = data["sent"]
    out = [render(r, now) for r in plan(data["delivery"], now) if r.at <= now < r.until and r.key not in sent]
    for waiting in data["waiting"]:
        at = parse_time(waiting["at"])
        if at and at <= now and waiting["key"] not in sent:
            out.append(waiting)
    return out


def send(push, msg):
    """One message to ntfy - (True, None) or (False, why)."""
    server = os.environ.get("TISS_HELFER_NTFY") or push["server"]
    body = {"topic": push["topic"], "title": msg["title"], "message": msg["message"],
            "priority": msg["priority"], "tags": [msg["tag"]], "click": msg["click"]}
    request = urllib.request.Request(server.rstrip("/") + "/", data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
                                     headers={"Content-Type": "application/json", "User-Agent": "TISS-Helfer"},
                                     method="POST")
    try:
        with urllib.request.urlopen(request, timeout=15, context=TLS):
            return True, None
    except urllib.error.HTTPError as e:
        return False, f"ntfy antwortet mit Fehler {e.code}."
    except (urllib.error.URLError, OSError) as e:
        return False, f"ntfy war nicht erreichbar: {getattr(e, 'reason', e)}"


# ── What the Pi keeps ─────────────────────────────────────────────────────────

class Store:
    """state.json in the service's own folder: the latest delivery, what was sent, what is known."""

    def __init__(self, folder):
        self.file = Path(folder) / "state.json"
        self.lock = threading.RLock()
        try:
            self.data = json.loads(self.file.read_text(encoding="utf-8"))
        except FileNotFoundError:
            self.data = {}
        except (OSError, ValueError) as e:
            print(f"state.json unreadable, starting empty: {e}", flush=True)
            self.data = {}
        defaults = {"delivery": None, "received": None, "sender": None, "known": {}, "sent": {},
                    "waiting": [], "pushes": [], "problem": None, "retryAt": None}
        for key, empty in defaults.items():
            self.data.setdefault(key, empty)

    def save(self):
        temp = self.file.with_name(self.file.name + ".tmp")
        temp.write_text(json.dumps(self.data, ensure_ascii=False, indent=1), encoding="utf-8")
        os.replace(temp, self.file)

    def accept(self, delivery, sender, now):
        with self.lock:
            data = self.data
            fresh = news(data["known"], delivery, now)
            waiting = {w["key"] for w in data["waiting"]}
            for course, exam in fresh:
                msg = new_date(course, exam, now)
                if msg["key"] not in waiting and msg["key"] not in data["sent"]:
                    data["waiting"].append(msg)
            data["delivery"] = delivery
            data["received"] = iso(now)
            data["sender"] = sender
            self.prune(now)
            self.save()
        return {"courses": len(delivery["courses"]), "exams": sum(len(c["exams"]) for c in delivery["courses"]),
                "newDates": len(fresh), "feedEvents": delivery["feed"]["ics"].count("BEGIN:VEVENT")}

    def prune(self, now):
        data = self.data
        data["sent"] = {k: v for k, v in data["sent"].items() if (parse_time(v) or now) > now - timedelta(days=45)}
        data["waiting"] = [w for w in data["waiting"]
                           if w["key"] not in data["sent"] and (parse_time(w.get("until")) or now + timedelta(days=1)) > now]
        # Exam dates known for more than a year and no longer in TISS are forgotten
        present = {c["id"]: {e["key"] for e in c["exams"]} for c in (data["delivery"] or {}).get("courses", [])}
        for course_id, seen in data["known"].items():
            for key in [k for k, first in seen.items()
                        if k not in present.get(course_id, set()) and (parse_time(first) or now) < now - timedelta(days=400)]:
                del seen[key]
        data["pushes"] = data["pushes"][-30:]

    def record(self, msg, ok, problem, now):
        data = self.data
        data["pushes"].append({"at": iso(now), "kind": msg["kind"], "title": msg["title"], "ok": ok})
        data["pushes"] = data["pushes"][-30:]
        if ok:
            if msg["kind"] != "probe":
                data["sent"][msg["key"]] = iso(now)
                data["waiting"] = [w for w in data["waiting"] if w["key"] != msg["key"]]
            data["problem"] = None
            data["retryAt"] = None
        else:
            data["problem"] = problem
            data["retryAt"] = iso(now + RETRY) if msg["kind"] != "probe" else data["retryAt"]


def tick(store, now):
    """Sends what is due. A failure waits a little before the next try, so ntfy is not hammered."""
    with store.lock:
        data = store.data
        delivery = data["delivery"]
        if not delivery or not delivery["push"]["on"]:
            return
        retry = parse_time(data["retryAt"])
        if retry and now < retry:
            return
        todo = due(data, now)
        push = dict(delivery["push"])
    for msg in todo:
        ok, problem = send(push, msg)
        with store.lock:
            store.record(msg, ok, problem, now)
            store.save()
        print(f"push {'sent' if ok else 'failed'}: {msg['kind']} {msg['key']}" + ("" if ok else f" - {problem}"), flush=True)
        if not ok:
            return


def probe(store, now):
    """A reminder to see that they arrive - also when the bot's pushes are switched off."""
    with store.lock:
        data = store.data
        delivery = data["delivery"]
        if not delivery:
            raise Refused("Der Laptop hat noch nichts geliefert – der Pi kennt den Push-Namen noch nicht.")
        coming_up = [r for r in plan(delivery, now) if r.key not in data["sent"]]
        push = dict(delivery["push"])
    body = "So kommen deine TISS-Erinnerungen vom Pi – auch wenn der Laptop schläft."
    click = "https://tiss.tuwien.ac.at/"
    if coming_up:
        first = coming_up[0]
        body += f" Die nächste {moment(max(first.at, now), now)}: {render(first, max(first.at, now))['title']}"
        click = first.exam["link"]
    msg = message("probe", "probe", "Probe-Erinnerung vom Pi", body, click, "bell", 3)
    ok, problem = send(push, msg)
    with store.lock:
        store.record(msg, ok, problem, now)
        store.save()
    if not ok:
        raise Refused(problem)
    return {"ok": True, "pushOn": push["on"]}


def status(store, now, feed_base):
    with store.lock:
        data = store.data
        delivery = data["delivery"]
        upcoming = []
        if delivery:
            for r in plan(delivery, now):
                if r.key not in data["sent"]:
                    when = max(r.at, now)
                    upcoming.append({"at": iso(when), "kind": r.kind, "title": render(r, when)["title"]})
        for w in data["waiting"]:
            upcoming.append({"at": w["at"], "kind": w["kind"], "title": w["title"]})
        upcoming.sort(key=lambda u: u["at"])
        received = parse_time(data["received"])
        reads = [parse_time(c["read"]) for c in (delivery or {}).get("courses", []) if c["read"]]
        return {
            "now": iso(now),
            "received": data["received"],
            "stale": received is None or now - received > STALE,
            "read": iso(max(reads)) if reads else None,
            "courses": len(delivery["courses"]) if delivery else 0,
            "exams": sum(len(c["exams"]) for c in delivery["courses"]) if delivery else 0,
            "feedEvents": delivery["feed"]["ics"].count("BEGIN:VEVENT") if delivery else 0,
            "feed": f"{feed_base}/kalender/{delivery['feed']['token']}.ics" if delivery else None,
            "pushOn": delivery["push"]["on"] if delivery else None,
            "problem": data["problem"],
            "next": upcoming[:8],
            "pushes": data["pushes"][-10:][::-1],
        }


# ── The laptop ────────────────────────────────────────────────────────────────

def address(value):
    try:
        return ipaddress.ip_address(str(value).strip().strip("[]"))
    except ValueError:
        return None


class Laptop:
    """
    The laptop's Tailscale addresses: deliveries are taken from them only. Tailscale names them
    (tailscale ip <name>, both IPv4 and IPv6 - Windows may connect by either), asked again every
    ten minutes; "pc.ip" and "tiss.from" in the settings count as well.
    """

    def __init__(self, cfg):
        pc = cfg["pc"]
        self.name = (pc.get("name") or "").split(".")[0]
        self.fixed = {a for a in map(address, [pc.get("ip"), pc.get("ip6"), *cfg["tiss"]["from"]]) if a}
        self.found = set()
        self.asked = -1e9
        self.lock = threading.Lock()

    def addresses(self):
        with self.lock:
            wait = 600 if self.found else 30
            if time.monotonic() - self.asked > wait:
                self.asked = time.monotonic()
                self.found = self.ask() or self.found
            return self.fixed | self.found

    def ask(self):
        if not self.name:
            return set()
        try:
            out = subprocess.run([TAILSCALE, "ip", self.name], capture_output=True, text=True, timeout=5)
        except (OSError, subprocess.SubprocessError):
            return set()
        return {a for a in map(address, out.stdout.split()) if a} if out.returncode == 0 else set()


# ── The page ──────────────────────────────────────────────────────────────────

PAGE = (HERE / "tiss.html").read_text(encoding="utf-8") if (HERE / "tiss.html").exists() else "<!doctype html><title>TISS-Helfer</title>"

SECURITY = {
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Referrer-Policy": "no-referrer",
    "Content-Security-Policy": "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; "
                               "img-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
}


class Handler(BaseHTTPRequestHandler):
    server_version = "TISS-Helfer"
    sys_version = ""
    cfg = None
    store = None
    laptop = None

    def log_message(self, fmt, *args):   # journald adds the time; the feed's key stays out of the log
        line = re.sub(r"/kalender/[^ ]+\.ics", "/kalender/….ics", fmt % args)
        headers = getattr(self, "headers", None)
        print(f"{headers.get('Tailscale-User-Login', '-') if headers else '-'} {line}", flush=True)

    def allowed(self):
        if "Tailscale-Funnel-Request" in self.headers:
            return False
        allow = self.cfg["allow"]
        return not allow or self.headers.get("Tailscale-User-Login", "").lower() in [a.lower() for a in allow]

    def route(self):
        """The path below /tiss - Serve takes the prefix off, a request straight to the port has it or not."""
        path = self.path.split("?", 1)[0]
        base = self.cfg["tiss"]["base"]
        if base and (path == base or path.startswith(base + "/")):
            path = path[len(base):]
        return path or "/"

    def sender(self):
        """The Tailscale address the request came from, as Serve says - None for one that did not come through Serve."""
        if "X-Forwarded-Host" not in self.headers:
            return None
        return address((self.headers.get("X-Forwarded-For") or "").split(",")[-1])

    def own_origin(self):
        origin = self.headers.get("Origin")
        host = self.headers.get("X-Forwarded-Host") or self.headers.get("Host") or ""
        return not origin or origin.split("://", 1)[-1] == host

    def feed_base(self):
        if "X-Forwarded-Host" in self.headers:
            return f"https://{self.headers['X-Forwarded-Host']}{self.cfg['tiss']['base']}"
        return f"http://{self.headers.get('Host', '')}{self.cfg['tiss']['base']}"

    def send(self, status, body, kind="text/plain; charset=utf-8", headers=None):
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        for k, v in {**SECURITY, **(headers or {})}.items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def json(self, status, value):
        self.send(status, json.dumps(value, ensure_ascii=False), "application/json; charset=utf-8")

    def body(self):
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            data = bytearray()
            while True:
                size = int(self.rfile.readline().split(b";")[0].strip() or b"0", 16)
                if size == 0:
                    while self.rfile.readline().strip():
                        pass
                    return bytes(data)
                data += self.rfile.read(size)
                self.rfile.readline()
                if len(data) > MAX_BODY:
                    raise Refused("Die Lieferung ist zu groß.")
        length = int(self.headers.get("Content-Length") or 0)
        if length <= 0 or length > MAX_BODY:
            raise Refused("Die Lieferung ist leer oder zu groß.")
        return self.rfile.read(length)

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        if not self.allowed():
            return self.send(403, "Nicht freigegeben.")
        path = self.route()
        if path == "/":
            settings = json.dumps({"base": self.cfg["tiss"]["base"]}).replace("<", "\\u003c")
            return self.send(200, PAGE.replace("/*SETTINGS*/{}", settings), "text/html; charset=utf-8")
        if path == "/api/status":
            return self.json(200, status(self.store, clock(), self.feed_base()))
        if path.startswith("/kalender/") and path.endswith(".ics"):
            with self.store.lock:
                delivery = self.store.data["delivery"]
                if delivery and path == f"/kalender/{delivery['feed']['token']}.ics":
                    return self.send(200, delivery["feed"]["ics"], "text/calendar; charset=utf-8",
                                     {"Cache-Control": "no-cache"})
        return self.send(404, "Gibt es nicht.")

    def do_POST(self):
        if not self.allowed():
            return self.send(403, "Nicht freigegeben.")
        path = self.route()
        try:
            if path == "/api/lieferung":
                who = self.sender()
                if who is None or who not in self.laptop.addresses():
                    self.log_message("delivery refused - it came from %s", who)
                    return self.send(403, "Lieferungen nimmt der Pi nur vom Laptop an.")
                if self.headers.get("X-Tiss-Bot") != "1" or self.headers.get("Origin"):
                    return self.send(403, "Nur vom TISS Bot.")
                try:
                    delivery = check(json.loads(self.body()))
                except ValueError:
                    raise Refused("Die Lieferung ist kein JSON.")
                summary = self.store.accept(delivery, str(who), clock())
                self.log_message("delivery: %s courses, %s exams, %s new dates", summary["courses"], summary["exams"], summary["newDates"])
                # What became due with it goes out now, not at the next look
                threading.Thread(target=lambda: tick(self.store, clock()), daemon=True).start()
                return self.json(200, {"ok": True, **summary})
            if path == "/api/probe":
                if self.headers.get("X-Tiss-Helfer") != "1" or not self.own_origin():
                    return self.send(403, "Nur von der Seite des TISS-Helfers.")
                return self.json(200, probe(self.store, clock()))
            return self.send(404, "Gibt es nicht.")
        except Refused as e:
            return self.json(400, {"error": str(e)})


def worker(store):
    while True:
        try:
            tick(store, clock())
        except Exception as e:      # the next look tries again
            print(f"tick failed: {e!r}", flush=True)
        time.sleep(TICK)


def main():
    parser = argparse.ArgumentParser(description="Kalender-Abo und Erinnerungen des TISS Bots, vom Pi.")
    parser.add_argument("--config", default="/etc/wecker.json")
    parser.add_argument("--state", default=(os.environ.get("STATE_DIRECTORY") or "/var/lib/tiss-helfer").split(":")[0])
    args = parser.parse_args()
    cfg = load(args.config)
    Handler.cfg = cfg
    Handler.store = Store(args.state)
    Handler.laptop = Laptop(cfg)
    threading.Thread(target=worker, args=(Handler.store,), daemon=True).start()
    host, port = cfg["tiss"]["listen"].rsplit(":", 1)
    server = ThreadingHTTPServer((host, int(port)), Handler)
    server.daemon_threads = True
    print(f"TISS-Helfer on http://{host}:{port}{cfg['tiss']['base']}/ - deliveries from {cfg['pc'].get('name') or '?'}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
