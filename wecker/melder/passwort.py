#!/usr/bin/env python3
"""Asks for the Gmail app password in the terminal for install.sh: every key shows as *, backspace
and Ctrl+U work, a paste counts as typed (the terminal's paste marks and arrow keys are skipped), and
in the end only the letters count - Google shows the password in groups of four. Three tries.

The questions go to the terminal (/dev/tty); the 16 letters go to stdout, where install.sh hands them
straight to the root-only file. Nothing is written when it does not work out.
"""
import os
import sys
import termios
import tty


def ask(fd, say):
    """One line, read key by key: the text typed, or None when the terminal is gone."""
    chars, esc = [], ""
    while True:
        data = os.read(fd, 1024)
        if not data:
            return None
        for ch in data.decode("utf-8", "ignore"):
            if esc:
                # ESC [ ... final byte (arrow keys, paste marks), ESC O x, or ESC and one key
                esc += ch
                if (esc[1] == "[" and len(esc) > 2 and "@" <= ch <= "~") or (esc[1] == "O" and len(esc) == 3) \
                        or esc[1] not in "[O":
                    esc = ""
                continue
            if ch == "\x1b":
                esc = ch
            elif ch in "\r\n":
                return "".join(chars)
            elif ch == "\x03":
                raise KeyboardInterrupt
            elif ch in "\x7f\x08":
                if chars:
                    chars.pop()
                    say("\b \b")
            elif ch == "\x15":   # Ctrl+U: start the line over
                say("\b \b" * len(chars))
                chars.clear()
            elif ch.isprintable():
                chars.append(ch)
                say("*")


def shape(typed):
    return "".join("x" if c.isascii() and c.isalpha() else "_" if c.isspace() else "#" if c.isdigit() else "?"
                   for c in typed)


def main():
    fd = os.open("/dev/tty", os.O_RDWR)

    def say(text):
        os.write(fd, text.encode("utf-8"))

    old = termios.tcgetattr(fd)
    try:
        for attempt in range(3):
            say("Gmail-App-Passwort für den Pi einfügen oder tippen (16 Buchstaben, jede Taste zeigt *), dann Enter:\n")
            tty.setraw(fd)
            try:
                typed = ask(fd, say)
            finally:
                termios.tcsetattr(fd, termios.TCSADRAIN, old)
            say("\n")   # the terminal is back to normal: "\n" starts a new line
            if typed is None:
                return 1
            letters = "".join(c for c in typed if c.isascii() and c.isalpha())
            if len(letters) == 16:
                say("16 Buchstaben erkannt.\n")
                sys.stdout.write(letters)
                sys.stdout.flush()
                return 0
            # Where it went wrong, without a letter of it: the shape of what came in
            say(f"Erkannt: {len(letters)} Buchstaben - ein App-Passwort hat 16.\n"
                f"So kam es an: {shape(typed)}   (x Buchstabe a-z, _ Leerzeichen, # Ziffer, ? anderes Zeichen)\n"
                + ("Noch einmal.\n" if attempt < 2 else "Abgebrochen.\n"))
        return 1
    except KeyboardInterrupt:
        say("\nAbgebrochen.\n")
        return 1
    finally:
        termios.tcsetattr(fd, termios.TCSADRAIN, old)


if __name__ == "__main__":
    sys.exit(main())
