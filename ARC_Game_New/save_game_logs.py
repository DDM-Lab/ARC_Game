#!/usr/bin/env python3
"""Receives game-log uploads from the ARC Game (LogSender.cs) and saves them as CSV.

Two kinds of upload, chosen by the payload's "uploadKind" field:

  final       The end-of-game upload (Day 8). Saved in LOG_DIR as
              <PlayerName>_<SessionId>_<Timestamp>_<MessageCount>.csv, exactly as before.
              A payload with no uploadKind (older clients) is treated as "final".

  checkpoint  A cumulative snapshot uploaded at the end of each earlier day, as insurance against
              a participant dropping out or the final upload failing. Saved in
              LOG_DIR/checkpoints/ as <PlayerName>_<SessionId>_checkpoint_day<N>.csv - one file per
              (session, day), overwritten if the same day is uploaded again (a retry), so
              retries never pile up. Later days are supersets of earlier ones.

Every save is atomic (temp file + rename), so a failed or interrupted write can never leave a
half-written file or destroy an earlier good one, and is verified before answering. The response
is an explicit ack:

    {"ok": true, "bytes": <request body bytes received>, "messages": <rows saved>, ...}

The client compares "bytes" with what it sent. Anything that stops the data from being fully
saved returns a non-2xx status with {"ok": false, ...}.
"""

import csv
import io
import json
import os
import sys
import traceback
from datetime import datetime

# Overridable only so the script can be tested without touching the real directory. Web servers
# don't pass arbitrary environment variables through to CGI scripts, so production uses the default.
LOG_DIR = os.environ.get("ARC_LOG_DIR", "/home/ddmlab/project-containers/experiment-data/arc-game")
CHECKPOINT_DIR = os.path.join(LOG_DIR, "checkpoints")

# Sanity cap so a bogus request can't make this script buffer an unbounded body in memory. Real
# uploads are a few MB at most. NOTE: the web server's own limit (Apache LimitRequestBody, nginx
# client_max_body_size, ...) is applied before this script runs and is not visible here.
MAX_BODY_BYTES = 50 * 1024 * 1024

STATUS_TEXT = {
    200: "OK",
    400: "Bad Request",
    405: "Method Not Allowed",
    413: "Payload Too Large",
    500: "Internal Server Error",
}


def send_response(status_code, message, **extra):
    ok = status_code == 200
    body = {"ok": ok, "status": "success" if ok else "error", "message": message}
    body.update(extra)
    print("Status: {} {}".format(status_code, STATUS_TEXT.get(status_code, "Error")))
    print("Content-Type: application/json")
    print("Access-Control-Allow-Origin: *")
    print("Access-Control-Allow-Methods: POST, OPTIONS")
    print("Access-Control-Allow-Headers: Content-Type")
    print()
    print(json.dumps(body))


def handle_options():
    print("Status: 200 OK")
    print("Content-Type: text/plain")
    print("Access-Control-Allow-Origin: *")
    print("Access-Control-Allow-Methods: POST, OPTIONS")
    print("Access-Control-Allow-Headers: Content-Type")
    print()


def sanitize(value, max_len=64):
    """Reduce an untrusted value to something safe to put in a filename (no path separators)."""
    cleaned = "".join(c if c.isalnum() or c in "-_" else "_" for c in str(value))
    return cleaned[:max_len] or "unknown"


def read_body():
    """Returns (raw_bytes, None) on success or (None, (status, message)) on failure.

    Reads bytes, not text: Content-Length counts bytes, and decoding with the process locale
    (often plain ASCII under CGI) would break on the non-ASCII characters game messages contain.
    """
    try:
        content_length = int(os.environ.get("CONTENT_LENGTH") or 0)
    except ValueError:
        return None, (400, "Invalid Content-Length")

    if content_length <= 0:
        return None, (400, "Empty request body")
    if content_length > MAX_BODY_BYTES:
        return None, (413, "Request body too large ({} bytes, limit {})".format(content_length, MAX_BODY_BYTES))

    raw = sys.stdin.buffer.read(content_length)
    if len(raw) != content_length:
        # The client hung up (or was cut off, e.g. by leaving the page) mid-upload.
        return None, (400, "Incomplete request body ({} of {} bytes received)".format(len(raw), content_length))
    return raw, None


def parse_payload(raw):
    """Returns (payload, messages, None) on success or (None, None, (status, message)) on failure."""
    try:
        payload = json.loads(raw.decode("utf-8"))
    except UnicodeDecodeError:
        return None, None, (400, "Request body is not valid UTF-8")
    except json.JSONDecodeError as e:
        return None, None, (400, "Invalid JSON: {}".format(e))

    if not isinstance(payload, dict):
        return None, None, (400, "Payload must be a JSON object")

    # An empty or missing message list used to be "saved" as a header-only file and reported as a
    # success - a silent way to lose a whole session. Refuse it instead.
    messages = payload.get("messages")
    if not isinstance(messages, list) or not messages:
        return None, None, (400, "Payload has no messages")
    if not all(isinstance(m, dict) for m in messages):
        return None, None, (400, "Every entry in messages must be an object")

    total = payload.get("totalMessages")
    if isinstance(total, int) and total != len(messages):
        return None, None, (400, "totalMessages ({}) does not match the {} messages received".format(total, len(messages)))

    return payload, messages, None


def build_csv(payload, messages):
    session_id = payload.get("sessionId", "unknown")
    player_name = payload.get("playerName", "unknown")
    game_version = payload.get("gameVersion", "unknown")

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
    return output.getvalue().encode("utf-8")


def atomic_write(path, data):
    """Write to a temp file, flush it to disk, then rename over the target. The target is either
    the complete new file or (if anything fails) untouched - never half-written. Verified after."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp_path = "{}.tmp.{}".format(path, os.getpid())
    try:
        with open(tmp_path, "wb") as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp_path, path)
    except BaseException:
        try:
            os.remove(tmp_path)
        except OSError:
            pass
        raise

    saved = os.path.getsize(path)
    if saved != len(data):
        raise IOError("Saved file is {} bytes, expected {}".format(saved, len(data)))


def main():
    method = os.environ.get("REQUEST_METHOD", "GET")

    if method == "OPTIONS":
        handle_options()
        return

    if method != "POST":
        send_response(405, "Only POST is allowed")
        return

    try:
        raw, error = read_body()
        if error:
            send_response(*error)
            return

        payload, messages, error = parse_payload(raw)
        if error:
            send_response(*error)
            return

        kind = "checkpoint" if payload.get("uploadKind") == "checkpoint" else "final"
        safe_name = sanitize(payload.get("playerName", "unknown"))
        safe_session = sanitize(payload.get("sessionId", "unknown"))

        if kind == "checkpoint":
            try:
                day = int(payload.get("checkpointDay", 0))
            except (TypeError, ValueError):
                day = 0
            if day < 1:
                send_response(400, "A checkpoint upload needs checkpointDay >= 1")
                return
            filename = "{}_{}_checkpoint_day{}.csv".format(safe_name, safe_session, day)
            filepath = os.path.join(CHECKPOINT_DIR, filename)
        else:
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            # Filename: PlayerName_SessionId_Timestamp_MessageCount.csv
            filename = "{}_{}_{}_{}.csv".format(safe_name, safe_session, timestamp, len(messages))
            filepath = os.path.join(LOG_DIR, filename)

        data = build_csv(payload, messages)
        atomic_write(filepath, data)

        send_response(
            200,
            "Saved {} messages ({} upload) to {}".format(len(messages), kind, filename),
            bytes=len(raw),
            messages=len(messages),
            saved_bytes=len(data),
            kind=kind,
            file=filename,
        )

    except OSError as e:
        traceback.print_exc(file=sys.stderr)  # full detail goes to the web server's error log
        send_response(500, "File write error ({})".format(type(e).__name__))
    except Exception as e:
        traceback.print_exc(file=sys.stderr)
        send_response(500, "Server error ({})".format(type(e).__name__))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Anything that escaped main() must still come back as a real error status, not a
        # crashed CGI (which many servers turn into an ambiguous empty response).
        traceback.print_exc(file=sys.stderr)
        send_response(500, "Server error")
