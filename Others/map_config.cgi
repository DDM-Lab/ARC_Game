#!/usr/bin/env python3
"""
ARC Game – Map Config CGI endpoint
Deployed on janus alongside the existing log-saving CGI script.

POST /cgi-bin/map_config.cgi   – body: MapConfig JSON → saved to disk
GET  /cgi-bin/map_config.cgi   – returns the saved MapConfig JSON
OPTIONS                        – CORS preflight
"""

import cgitb
import json
import os
import sys
from datetime import datetime

cgitb.enable()

# Directory where the config file lives (same pattern as LOG_DIR in your log script)
CONFIG_DIR  = "/home/ddmlab/arc-game-configs"
CONFIG_FILE = os.path.join(CONFIG_DIR, "latest_map_config.json")


# ── CORS / response helpers ───────────────────────────────────────────────────

def cors_headers():
    print("Access-Control-Allow-Origin: *")
    print("Access-Control-Allow-Methods: GET, POST, OPTIONS")
    print("Access-Control-Allow-Headers: Content-Type")


def send_json(status_code, body_dict):
    reason = "OK" if status_code == 200 else "Error"
    print(f"Status: {status_code} {reason}")
    print("Content-Type: application/json")
    cors_headers()
    print()
    print(json.dumps(body_dict))


def send_raw_json(raw_text):
    """Return the stored JSON directly so Unity's JsonUtility can parse it."""
    print("Status: 200 OK")
    print("Content-Type: application/json")
    cors_headers()
    print()
    print(raw_text)


def handle_options():
    print("Status: 200 OK")
    print("Content-Type: text/plain")
    cors_headers()
    print()


# ── Request handlers ──────────────────────────────────────────────────────────

def handle_get():
    if not os.path.exists(CONFIG_FILE):
        send_json(404, {
            "status": "error",
            "message": "No map config saved yet. Run the Instructor Config scene first."
        })
        return

    with open(CONFIG_FILE, "r", encoding="utf-8") as f:
        raw = f.read()

    send_raw_json(raw)


def handle_post():
    try:
        content_length = int(os.environ.get("CONTENT_LENGTH", 0))
        if content_length == 0:
            send_json(400, {"status": "error", "message": "Empty request body"})
            return

        raw = sys.stdin.read(content_length)

        # Validate it's parseable JSON before saving
        data = json.loads(raw)

        os.makedirs(CONFIG_DIR, exist_ok=True)
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            f.write(raw)

        schema  = data.get("schemaVersion", "?")
        ts      = data.get("timestamp", datetime.utcnow().isoformat())
        n_obj   = len(data.get("objects", []))

        send_json(200, {
            "status":  "success",
            "message": f"Map config saved (schema v{schema}, {n_obj} objects, timestamp {ts})"
        })

    except json.JSONDecodeError as e:
        send_json(400, {"status": "error", "message": f"Invalid JSON: {e}"})
    except IOError as e:
        send_json(500, {"status": "error", "message": f"File write error: {e}"})
    except Exception as e:
        send_json(500, {"status": "error", "message": f"Server error: {e}"})


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    method = os.environ.get("REQUEST_METHOD", "GET").upper()

    if   method == "OPTIONS": handle_options()
    elif method == "GET":     handle_get()
    elif method == "POST":    handle_post()
    else:
        send_json(405, {"status": "error", "message": f"Method {method} not allowed"})


if __name__ == "__main__":
    main()
