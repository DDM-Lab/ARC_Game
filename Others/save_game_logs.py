#!/usr/bin/env python3

import cgi
import cgitb
import csv
import io
import json
import os
import sys
from datetime import datetime

cgitb.enable()

LOG_DIR = "/home/ddmlab/arc-game-logs"

def send_response(status_code, message):
    print("Status: {} {}".format(status_code, "OK" if status_code == 200 else "Error"))
    print("Content-Type: application/json")
    print("Access-Control-Allow-Origin: *")
    print("Access-Control-Allow-Methods: POST, OPTIONS")
    print("Access-Control-Allow-Headers: Content-Type")
    print()
    print(json.dumps({"status": "success" if status_code == 200 else "error", "message": message}))

def handle_options():
    print("Status: 200 OK")
    print("Content-Type: text/plain")
    print("Access-Control-Allow-Origin: *")
    print("Access-Control-Allow-Methods: POST, OPTIONS")
    print("Access-Control-Allow-Headers: Content-Type")
    print()

def main():
    method = os.environ.get("REQUEST_METHOD", "GET")

    if method == "OPTIONS":
        handle_options()
        return

    if method != "POST":
        send_response(405, "Only POST is allowed")
        return

    try:
        content_length = int(os.environ.get("CONTENT_LENGTH", 0))
        if content_length == 0:
            send_response(400, "Empty request body")
            return

        raw_data = sys.stdin.read(content_length)
        log_data = json.loads(raw_data)

        session_id = log_data.get("sessionId", "unknown")
        player_name = log_data.get("playerName", "unknown")
        game_version = log_data.get("gameVersion", "unknown")
        messages = log_data.get("messages", [])

        safe_name = "".join(c if c.isalnum() or c in "-_" else "_" for c in player_name)
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")

        # Filename: PlayerName_SessionId_Timestamp_MessageCount.csv
        filename = "{}_{}_{}_{}.csv".format(safe_name, session_id, timestamp, len(messages))
        filepath = os.path.join(LOG_DIR, filename)

        os.makedirs(LOG_DIR, exist_ok=True)

        output = io.StringIO()
        writer = csv.writer(output)

        writer.writerow([
            "SessionId", "PlayerName", "GameVersion",
            "Timestamp", "RealTime", "Day", "Round",
            "Category", "MessageType", "Content"
        ])

        for msg in messages:
            writer.writerow([
                session_id,
                player_name,
                game_version,
                msg.get("timestamp", ""),
                msg.get("realTime", ""),
                msg.get("day", ""),
                msg.get("round", ""),
                msg.get("category", ""),
                msg.get("messageType", ""),
                msg.get("content", "")
            ])

        with open(filepath, "w", encoding="utf-8", newline="") as f:
            f.write(output.getvalue())

        send_response(200, "Saved {} messages to {}".format(len(messages), filename))

    except json.JSONDecodeError as e:
        send_response(400, "Invalid JSON: {}".format(str(e)))
    except IOError as e:
        send_response(500, "File write error: {}".format(str(e)))
    except Exception as e:
        send_response(500, "Server error: {}".format(str(e)))

if __name__ == "__main__":
    main()