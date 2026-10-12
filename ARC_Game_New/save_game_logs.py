#!/usr/bin/env python3
"""Receives game-log uploads from the ARC Game (LogSender.cs) and saves them as CSV.

Two kinds of upload, chosen by the payload's "uploadKind" field:

  final       The end-of-game upload (Day 8). Saved in LOG_DIR as <PlayerName>_<SessionId>_final.*.
              A payload with no uploadKind (older clients) is treated as "final".

  checkpoint  A cumulative snapshot uploaded at the end of each earlier day, as insurance against
              a participant dropping out or the final upload failing. Saved in
              LOG_DIR/checkpoints/ as <PlayerName>_<SessionId>_checkpoint_day<N>.*.

The body is the payload JSON, either plain (older clients) or gzipped (newer clients, sent as
Content-Type: application/gzip). A gzipped body is recognised by its first two bytes, so the
Content-Type header is not relied on. Both are limited to MAX_BODY_BYTES as received, and a gzipped
body is also limited to MAX_PAYLOAD_BYTES once decompressed.

Each upload is saved as two files before the reply:

  <base>.csv             the log, one row per message
  <base>.upload.json.gz  the whole upload: a gzipped body exactly as received, or a plain body gzipped

There is one file set per (session, final) and per (session, day). The game resends the whole log
when it does not get the reply in time, so a resend replaces the earlier copy instead of adding a
file. A save never replaces a copy that already holds more messages (an older resend that finishes
late cannot overwrite a newer one), and a resend with the same content as the saved copy is
answered as a duplicate without writing anything or starting another replay build.

The reply goes out as soon as those two files are saved. The replay check and <base>.replay.json
are built afterwards by a background process (this script with --build-replay), because on a full
session they take long enough to push the reply past the game's request timeout. One build runs at
a time. A received upload is therefore "saved" before its replay is "built": if a background build
fails, <base>.replay_error.txt says why, and the upload itself stays saved. To build any missing or
out-of-date sidecars by hand (or from cron):

    python3 save_game_logs.py --build-pending

<base>.upload_log.jsonl gets one line per upload request (saved / duplicate / kept_newer, with the
client's attempt number and upload id from the query string) and one per replay build (built /
failed / superseded), so a session's upload history can be read without the web server's logs.

Every save is atomic (temp file + rename), so a failed or interrupted write can never leave a
half-written file or destroy an earlier good one, and is verified before answering. The response
is an explicit ack:

    {"ok": true, "bytes": <request body bytes received>, "payload_bytes": <bytes after decompressing>,
     "messages": <rows in the upload>, "result": "saved" | "duplicate" | "kept_newer", ...}

The client compares "bytes" with the bytes it sent (the compressed size, for a gzipped body) and
"payload_bytes" with the size of its JSON. Anything that stops the data from being fully saved
returns a non-2xx status with {"ok": false, ...}.
"""

import copy
import csv
import gzip
import hashlib
import io
import json
import os
import subprocess
import sys
import traceback
import urllib.parse
import zlib
from datetime import datetime, timezone
from decimal import Decimal, ROUND_HALF_EVEN, localcontext

try:
    import fcntl  # POSIX only. The server is Linux; without it (Windows dev/tests) locking is skipped.
except ImportError:
    fcntl = None

# Overridable only so the script can be tested without touching the real directory. Web servers
# don't pass arbitrary environment variables through to CGI scripts, so production uses the default.
LOG_DIR = os.environ.get("ARC_LOG_DIR", "/home/ddmlab/project-containers/experiment-data/arc-game")
CHECKPOINT_DIR = os.path.join(LOG_DIR, "checkpoints")

# Sanity cap so a bogus request can't make this script buffer an unbounded body in memory. Real
# uploads are a few MB at most. NOTE: the web server's own limit (Apache LimitRequestBody, nginx
# client_max_body_size, ...) is applied before this script runs and is not visible here.
MAX_BODY_BYTES = 50 * 1024 * 1024

# The same limit for a gzipped body once decompressed, so the cap means the same thing for old and
# new clients (the game logs payload size as a share of it) and a small gzip cannot expand into
# gigabytes.
MAX_PAYLOAD_BYTES = MAX_BODY_BYTES

GZIP_MAGIC = b"\x1f\x8b"

RAW_SUFFIX = ".upload.json.gz"
REPLAY_SUFFIX = ".replay.json"
REPLAY_ERROR_SUFFIX = ".replay_error.txt"
UPLOAD_LOG_SUFFIX = ".upload_log.jsonl"
LOCK_NAME = ".save_game_logs.lock"
BUILD_LOCK_NAME = ".replay_build.lock"

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


def decode_body(raw):
    """Returns (json_bytes, compressed, None) on success or (None, None, (status, message)) on failure.

    A body starting with the gzip magic bytes is decompressed; anything else is plain JSON from an
    older client. Decompression stops at MAX_PAYLOAD_BYTES + 1, so an oversized body is refused
    without ever being expanded in full.
    """
    if raw[:2] != GZIP_MAGIC:
        return raw, False, None

    decompressor = zlib.decompressobj(16 + zlib.MAX_WBITS)  # gzip framing only
    try:
        data = decompressor.decompress(raw, MAX_PAYLOAD_BYTES + 1)
    except zlib.error as e:
        return None, None, (400, "Invalid gzip body: {}".format(e))
    if len(data) > MAX_PAYLOAD_BYTES:
        return None, None, (413, "Decompressed body too large (over {} bytes)".format(MAX_PAYLOAD_BYTES))
    if not decompressor.eof:
        return None, None, (400, "Incomplete gzip body ({} bytes received)".format(len(raw)))
    if decompressor.unused_data:
        return None, None, (400, "Unexpected data after the end of the gzip body")
    return data, True, None


def request_info():
    """The client's upload id and attempt number from the query string (both optional, for the
    upload log only). The query string always contains "=", so Apache never passes it to this
    script as command-line arguments."""
    query = urllib.parse.parse_qs(os.environ.get("QUERY_STRING", ""))

    def first(name):
        values = query.get(name)
        return values[0] if values else ""

    attempt = first("attempt")
    return {
        "uploadId": sanitize(first("uploadId"), 40) if first("uploadId") else None,
        "attempt": int(attempt) if attempt.isdigit() and len(attempt) < 6 else None,
    }


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
        "Budget", "Satisfaction", "Efficiency", "ActiveTaskCount", "StateSummary",
        "Category", "MessageType", "Content", "LedgerSeq", "TaskDecisionId"
    ])
    for msg in messages:
        # ledgerSeq is -1 (Unity's sentinel for "no ledger entry") on most rows; written blank,
        # not -1, so a researcher doesn't mistake it for a real sequence number.
        ledger_seq = msg.get("ledgerSeq", -1)
        writer.writerow([
            session_id,
            player_name,
            game_version,
            msg.get("timestamp", ""),
            msg.get("realTime", ""),
            msg.get("day", ""),
            msg.get("round", ""),
            msg.get("budget", ""),
            msg.get("satisfaction", ""),
            msg.get("efficiency", ""),
            msg.get("activeTaskCount", ""),
            msg.get("stateSummary", ""),
            msg.get("category", ""),
            msg.get("messageType", ""),
            msg.get("content", ""),
            ledger_seq if isinstance(ledger_seq, int) and ledger_seq >= 0 else "",
            msg.get("taskDecisionId", "") or ""
        ])
    return output.getvalue().encode("utf-8")


def _parse_ledger_entry(entry):
    """The Unity side builds targetJson/parametersJson as JSON-object STRINGS (JsonUtility
    cannot serialize a Dictionary or arbitrary nested object without one fixed C# type per
    action/event type - see JsonObj in GameLogPanel.cs). Parsed back into real nested objects
    here, so the file a replay tool or analyst actually reads has true nested target/parameters
    objects, not escaped strings - matching what LedgerEntry conceptually represents."""
    out = dict(entry)
    for src_key, dst_key in (("targetJson", "target"), ("parametersJson", "parameters")):
        raw = out.pop(src_key, None)
        try:
            out[dst_key] = json.loads(raw) if raw else {}
        except (TypeError, ValueError):
            out[dst_key] = {"_unparsed": raw}
    # Replay fields exist only on captured entries and keyframes, so they are parsed when present
    # and never added as empty objects. The verifier has already checked the raw strings.
    for src_key, dst_key in (("stateDeltaJson", "stateDelta"), ("stateJson", "state")):
        raw = out.pop(src_key, None)
        if raw:
            try:
                out[dst_key] = json.loads(raw)
            except (TypeError, ValueError):
                out[dst_key] = {"_unparsed": raw}
    return out


# ---------------------------------------------------------------------------
# Canonical state. Mirrors Assets/Scripts/GameLog/Replay/CanonicalJson.cs and CanonicalState.cs
# and must produce byte-identical output: tests/replay/canonical_vectors.json is checked by both.
# Numbers are parsed as Decimal from their source text (never float) so 6-dp rounding matches C#.
# ---------------------------------------------------------------------------

CANON_SCHEMA_VERSION = 1
_CANON_EXCLUDED_ROOT_KEYS = ("createdUtc", "seed")
# Dotted path from the snapshot root -> id field. Must match CanonicalState.KeyRules exactly.
_CANON_KEY_RULES = {
    "buildings": "originalSiteId",
    "prebuilt": "buildingName",
    "vehicles": "vehicleName",
    "workforce.workers": "workerId",
    "tasks.activeTasks": "uid",
    "tasks.completedTasks": "uid",
    "relocations.walks": "walkId",
    "budgetAllocations.pending": "allocId",
    "deliveries.active": "taskId",
    "deliveries.completed": "taskId",
    "deliveries.pending": "taskId",
    "clients.groups": "groupId",
}
_CANON_SIX_DP = Decimal("0.000001")
_CANON_PRECISION = 100
# System.Decimal's largest magnitude. C# cannot parse beyond it, so Python refuses too.
_CANON_DECIMAL_MAX = Decimal("79228162514264337593543950335")
_CANON_SHORT_ESCAPES = {'"': '\\"', "\\": "\\\\", "\n": "\\n", "\r": "\\r",
                        "\t": "\\t", "\b": "\\b", "\f": "\\f"}


class CanonicalStateError(ValueError):
    """The state cannot be canonicalised. Callers must not write a hash for it."""


def _canon_reject_constant(name):
    raise CanonicalStateError("non-finite number {} is not canonicalisable".format(name))


def _canon_no_duplicate_keys(pairs):
    obj = {}
    for key, value in pairs:
        if key in obj:
            raise CanonicalStateError("duplicate key '{}'".format(key))
        obj[key] = value
    return obj


def canon_parse(text):
    """Strict parse. Numbers become Decimal from their source text."""
    return json.loads(text, parse_float=Decimal, parse_int=Decimal,
                      parse_constant=_canon_reject_constant,
                      object_pairs_hook=_canon_no_duplicate_keys)


def canon_format_number(value):
    """Same rule as JsonCanon.FormatNumber in C#: round to 6 dp half-even; zero is "0";
    integral values are integers; otherwise exactly six decimals."""
    if abs(value) > _CANON_DECIMAL_MAX:
        raise CanonicalStateError("number {} exceeds the decimal range".format(value))
    with localcontext() as ctx:
        ctx.prec = _CANON_PRECISION
        q = value.quantize(_CANON_SIX_DP, rounding=ROUND_HALF_EVEN)
    if q == 0:
        return "0"
    if q == q.to_integral_value():
        return str(int(q))
    return format(q, "f")


def _canon_string(text):
    out = ['"']
    for ch in text:
        esc = _CANON_SHORT_ESCAPES.get(ch)
        if esc is not None:
            out.append(esc)
        elif ord(ch) < 0x20:
            out.append("\\u%04x" % ord(ch))
        else:
            out.append(ch)
    out.append('"')
    return "".join(out)


def canon_dumps(node):
    """Compact JSON, object keys sorted by code point (identical to ordinal for BMP text)."""
    if isinstance(node, dict):
        return "{" + ",".join(_canon_string(k) + ":" + canon_dumps(node[k]) for k in sorted(node)) + "}"
    if isinstance(node, list):
        return "[" + ",".join(canon_dumps(v) for v in node) + "]"
    if isinstance(node, str):
        return _canon_string(node)
    if node is None:
        return "null"
    if isinstance(node, bool):
        return "true" if node else "false"
    if isinstance(node, Decimal):
        return canon_format_number(node)
    if isinstance(node, int):
        # Only ever a delta's baseSeq, which C# writes as a plain integer.
        return str(node)
    raise CanonicalStateError("unsupported value type {}".format(type(node).__name__))


def _canon_key_text(value, path, id_field):
    if isinstance(value, str):
        return value
    if isinstance(value, Decimal):
        return canon_format_number(value)
    raise CanonicalStateError("{} id '{}' is not a string or number".format(path, id_field))


def _canon_transform(node, path):
    if isinstance(node, dict):
        return {k: _canon_transform(v, path + "." + k if path else k) for k, v in node.items()}
    if isinstance(node, list):
        id_field = _CANON_KEY_RULES.get(path)
        if id_field is None:
            return [_canon_transform(v, path + "[]") for v in node]
        keyed = {}
        for item in node:
            if not isinstance(item, dict):
                raise CanonicalStateError("{} holds a non-object element".format(path))
            if id_field not in item:
                raise CanonicalStateError("{} element is missing '{}'".format(path, id_field))
            key = _canon_key_text(item[id_field], path, id_field)
            if key in keyed:
                raise CanonicalStateError("{} has duplicate {} {}".format(path, id_field, key))
            keyed[key] = _canon_transform(item, path + "[]")
        return keyed
    return node


def canonicalize_snapshot(snapshot_text):
    """GameSnapshot JSON text -> (canonical_text, sha256_hex). Mirrors CanonicalState.FromSnapshotJson."""
    root = canon_parse(snapshot_text)
    if not isinstance(root, dict):
        raise CanonicalStateError("snapshot root is not a JSON object")
    for key in _CANON_EXCLUDED_ROOT_KEYS:
        root.pop(key, None)
    root["schemaVersion"] = Decimal(CANON_SCHEMA_VERSION)
    text = canon_dumps(_canon_transform(root, ""))
    return text, hashlib.sha256(text.encode("utf-8")).hexdigest()


def canonicalize_json(text):
    """Canonical text for a plain JSON document (no key rules). Mirrors JsonCanon.Canonicalize."""
    return canon_dumps(canon_parse(text))


# ---------------------------------------------------------------------------
# State deltas and the replay-chain verifier. Mirrors Assets/Scripts/GameLog/Replay/StateDiff.cs.
# Ops: set {op,path,from,value} | add {op,path,value} | remove {op,path,fromHash}. Paths are key lists.
# ---------------------------------------------------------------------------

def _state_hash(node):
    return hashlib.sha256(canon_dumps(node).encode("utf-8")).hexdigest()


def _path_parent(state, path):
    if not isinstance(path, list) or not path or not all(isinstance(p, str) for p in path):
        raise CanonicalStateError("op path must be a non-empty list of keys")
    cur = state
    for key in path[:-1]:
        nxt = cur.get(key)
        if not isinstance(nxt, dict):
            raise CanonicalStateError("path passes through a missing or non-object key '{}'".format(key))
        cur = nxt
    return cur, path[-1]


def _apply_op(state, op):
    if not isinstance(op, dict):
        raise CanonicalStateError("op is not an object")
    kind = op.get("op")
    parent, key = _path_parent(state, op.get("path"))
    if kind == "set":
        if key not in parent:
            raise CanonicalStateError("set targets a missing key '{}'".format(key))
        if isinstance(parent[key], dict):
            raise CanonicalStateError("set targets an object at '{}'".format(key))
        if canon_dumps(parent[key]) != canon_dumps(op.get("from")):
            raise CanonicalStateError("set 'from' does not match the current value at '{}'".format(key))
        parent[key] = copy.deepcopy(op.get("value"))
    elif kind == "add":
        if key in parent:
            raise CanonicalStateError("add targets an existing key '{}'".format(key))
        parent[key] = copy.deepcopy(op.get("value"))
    elif kind == "remove":
        if key not in parent:
            raise CanonicalStateError("remove targets a missing key '{}'".format(key))
        if _state_hash(parent[key]) != op.get("fromHash"):
            raise CanonicalStateError("remove 'fromHash' does not match the value at '{}'".format(key))
        del parent[key]
    else:
        raise CanonicalStateError("unknown op '{}'".format(kind))


def apply_state_delta(state, delta):
    """Applies a delta to state in place. Checks the base hash before and the result hash after,
    as StateDiff.ApplyDelta does. Apply to a deep copy when the original must survive a failure."""
    if _state_hash(state) != delta.get("baseHash"):
        raise CanonicalStateError("base hash mismatch before applying delta")
    ops = delta.get("ops")
    if not isinstance(ops, list):
        raise CanonicalStateError("delta ops is not a list")
    for op in ops:
        _apply_op(state, op)
    if _state_hash(state) != delta.get("resultHash"):
        raise CanonicalStateError("result hash mismatch after applying delta")
    return state


def _diff_objects(before, after, parent, ops):
    for key in sorted(before):
        if key not in after:
            ops.append({"op": "remove", "path": parent + [key], "fromHash": _state_hash(before[key])})
    for key in sorted(after):
        path = parent + [key]
        if key not in before:
            ops.append({"op": "add", "path": path, "value": copy.deepcopy(after[key])})
            continue
        old, new = before[key], after[key]
        if isinstance(old, dict) and isinstance(new, dict):
            _diff_objects(old, new, path, ops)
        elif isinstance(old, dict) or isinstance(new, dict):
            ops.append({"op": "remove", "path": path, "fromHash": _state_hash(old)})
            ops.append({"op": "add", "path": path, "value": copy.deepcopy(new)})
        elif canon_dumps(old) != canon_dumps(new):
            ops.append({"op": "set", "path": path, "from": copy.deepcopy(old), "value": copy.deepcopy(new)})


def state_diff(before, after):
    """Ops that turn canonical tree `before` into `after`. Mirrors StateDiff.Diff."""
    ops = []
    _diff_objects(before, after, [], ops)
    return ops


def build_state_delta(base_seq, base_hash, result_hash, ops):
    return {"baseSeq": base_seq, "baseHash": base_hash, "resultHash": result_hash, "ops": ops}


def _verify_entry_capture(entry, fail):
    """Checks the delta-independent rules for one ledger entry."""
    if entry.get("type") == "Click" and entry.get("captured"):
        fail(entry, "click entries must not be captured")


def verify_replay_chain(payload):
    """Re-derives the state chain from the raw payload and checks every link.

    Works on the raw strings the game sent (initialSnapshotJson, stateDeltaJson, stateJson), so
    the check runs on exactly the bytes the game hashed, not on a re-serialised copy. Returns a
    report; it never raises, because a bad chain must not block the CSV save.

    Checks: seq is continuous from 0; the anchor canonicalises to initialStateHash; every captured
    entry links to the previous captured entry (baseSeq, baseHash); every delta applies and gives
    its resultHash; each keyframe's stateJson is canonical and hashes to its stateHash and to its
    delta's resultHash; every roundCheckpoint points at a matching keyframe; uncaptured entries have
    no delta; clicks are never captured.
    """
    report = {"ok": True, "errors": [], "warnings": [], "entries": 0, "capturedEntries": 0,
              "uncapturedEntries": 0, "keyframes": 0, "lastCapturedSeq": None, "finalStateHash": None}

    def fail(entry_or_none, message):
        seq = entry_or_none.get("seq") if isinstance(entry_or_none, dict) else None
        report["errors"].append(message if seq is None else "seq {}: {}".format(seq, message))

    ledger = payload.get("ledger")
    if not isinstance(ledger, list):
        ledger = []
    report["entries"] = len(ledger)

    anchor_hash = payload.get("initialStateHash")
    state = None
    chain_hash = anchor_hash
    chain_seq = -1
    anchor_text = payload.get("initialSnapshotJson")
    if anchor_hash and anchor_text:
        try:
            anchor_canon, digest = canonicalize_snapshot(anchor_text)
            if digest != anchor_hash:
                fail(None, "initial snapshot canonical hash does not match initialStateHash")
            state = canon_parse(anchor_canon)
        except (CanonicalStateError, ValueError) as e:
            fail(None, "initial snapshot cannot be canonicalised: {}".format(e))
    else:
        report["warnings"].append("no initial state anchor in this upload")

    if payload.get("canonicalSchemaVersion") != CANON_SCHEMA_VERSION:
        report["warnings"].append("canonicalSchemaVersion is {} (expected {})".format(
            payload.get("canonicalSchemaVersion"), CANON_SCHEMA_VERSION))

    by_seq = {}
    expected_seq = 0
    for entry in ledger:
        if not isinstance(entry, dict):
            fail(None, "ledger entry is not an object")
            continue
        seq = entry.get("seq")
        if seq != expected_seq:
            fail(entry, "sequence gap or reordering: expected seq {}".format(expected_seq))
        expected_seq = (seq + 1) if isinstance(seq, int) else expected_seq + 1
        if isinstance(seq, int):
            by_seq[seq] = entry
        _verify_entry_capture(entry, fail)

        if not entry.get("captured"):
            report["uncapturedEntries"] += 1
            if entry.get("stateDeltaJson"):
                fail(entry, "uncaptured entry carries a state delta")
            if entry.get("type") == "Keyframe":
                fail(entry, "keyframe has no captured state ({})".format(entry.get("captureError") or "no reason"))
            continue

        report["capturedEntries"] += 1
        raw_delta = entry.get("stateDeltaJson")
        if not raw_delta:
            fail(entry, "captured entry has no state delta")
            continue
        try:
            delta = canon_parse(raw_delta)
        except (CanonicalStateError, ValueError) as e:
            fail(entry, "state delta is not valid JSON: {}".format(e))
            continue

        if delta.get("baseSeq") != chain_seq:
            fail(entry, "baseSeq {} does not match the previous captured seq {}".format(delta.get("baseSeq"), chain_seq))
        if delta.get("baseHash") != chain_hash:
            fail(entry, "baseHash does not match the previous captured state")

        # Once a link fails, the state cannot be re-derived, so later hashes are not confirmed.
        # The claimed links are still checked above, for every entry.
        if state is not None:
            try:
                apply_state_delta(state, delta)
            except CanonicalStateError as e:
                fail(entry, "delta does not apply: {}".format(e))
                state = None

        if entry.get("type") == "Keyframe":
            report["keyframes"] += 1
            stored = entry.get("stateJson")
            if not stored:
                fail(entry, "keyframe has no stateJson")
            else:
                try:
                    if canon_dumps(canon_parse(stored)) != stored:
                        fail(entry, "keyframe stateJson is not in canonical form")
                except (CanonicalStateError, ValueError) as e:
                    fail(entry, "keyframe stateJson is not valid: {}".format(e))
                if hashlib.sha256(stored.encode("utf-8")).hexdigest() != entry.get("stateHash"):
                    fail(entry, "keyframe stateHash does not match its stateJson")
                if entry.get("stateHash") != delta.get("resultHash"):
                    fail(entry, "keyframe stateHash does not match its delta resultHash")

        chain_seq = seq
        chain_hash = delta.get("resultHash")

    if state is not None and chain_hash is not None and _state_hash(state) != chain_hash:
        fail(None, "replayed state hash does not match the last captured resultHash")

    for cp in payload.get("roundCheckpoints") or []:
        if not isinstance(cp, dict):
            continue
        match = by_seq.get(cp.get("ledgerSeq"))
        if match is None or match.get("type") != "Keyframe" or match.get("stateHash") != cp.get("stateHash"):
            fail(None, "roundCheckpoint for ledgerSeq {} has no matching keyframe".format(cp.get("ledgerSeq")))

    report["lastCapturedSeq"] = chain_seq if report["capturedEntries"] else None
    report["finalStateHash"] = chain_hash if report["capturedEntries"] else None
    report["ok"] = not report["errors"]
    return report


def build_replay_sidecar(payload, verification=None):
    """Everything needed to reconstruct/replay this session that doesn't fit a flat CSV row:
    the seed and RNG state the episode started from, the parameter set it ran under, the map
    provenance, the initial + per-round full-state snapshots (Tier 1), and the player-action/
    system-event ledger (Tier 3). Written as its own JSON file alongside the CSV - see the
    replay-architecture review for why snapshots and the ledger are separate (ledger entries
    are the replay-drivers; snapshots are periodic full state + a hash for validation, not
    taken per-action)."""
    keys = [
        "seed", "seedSource", "rngState", "buildGuid",
        "mapFromServer", "mapConfigHash", "mapConfigJson", "authoritativeConfigJson",
        "initialSnapshotJson", "initialSnapshotHash", "roundCheckpoints",
        "initialBudget", "initialSatisfaction", "initialCommunityCount",
        "initialResidentsPerCommunity", "initialNumDays", "initialRoundsPerDay",
        "initialTrainedVolunteers", "initialUntrainedVolunteers",
        "initialBudgetDailyAdditions", "initialWeather", "initialKitchenCapacity",
        "initialShelterCapacity", "initialCaseworkCapacity", "initialNeededWorkersPerLoc",
        "initialFoodDemandFrequency", "initialERVCount", "initialExternalRelationFrequency",
        "initialEmergencyTaskFrequency", "initialShelterFloodThreshold",
        "initialShelterFloodRadius", "initialShelterFloodComparison",
        "floodExpansionRates", "floodSpreadMultipliers",
        "initialStateHash", "canonicalSchemaVersion",
    ]
    sidecar = {k: payload.get(k) for k in keys if k in payload}
    if verification is None:
        verification = verify_replay_chain(payload)
    sidecar["replayVerification"] = verification

    # Every *Json string field below (mapConfigJson, initialSnapshotJson, each round
    # checkpoint's snapshotJson) is, like the ledger's target/parameters, a JsonUtility string
    # on the Unity side for the same reason - these are large nested objects (MapConfig,
    # GameSnapshot) with no single fixed shape JsonUtility can embed directly. Parsed back into
    # real nested objects here so the whole sidecar is one consistently-structured file, not a
    # mix of real JSON and JSON-strings-within-JSON.
    for json_key, out_key in (("mapConfigJson", "mapConfig"), ("initialSnapshotJson", "initialSnapshot"),
                              ("authoritativeConfigJson", "authoritativeConfig")):
        raw = sidecar.pop(json_key, None)
        if raw:
            try:
                sidecar[out_key] = json.loads(raw)
            except (TypeError, ValueError):
                sidecar[out_key] = {"_unparsed": raw}

    checkpoints = sidecar.get("roundCheckpoints")
    if isinstance(checkpoints, list):
        for cp in checkpoints:
            if not isinstance(cp, dict):
                continue
            raw = cp.pop("snapshotJson", None)
            if raw:
                try:
                    cp["snapshot"] = json.loads(raw)
                except (TypeError, ValueError):
                    cp["snapshot"] = {"_unparsed": raw}

    ledger = payload.get("ledger")
    if isinstance(ledger, list):
        sidecar["ledger"] = [_parse_ledger_entry(e) for e in ledger if isinstance(e, dict)]
    sidecar["sessionId"] = payload.get("sessionId", "unknown")
    sidecar["playerName"] = payload.get("playerName", "unknown")
    sidecar["gameVersion"] = payload.get("gameVersion", "unknown")
    sidecar["uploadKind"] = payload.get("uploadKind", "final")
    sidecar["checkpointDay"] = payload.get("checkpointDay", 0)
    return json.dumps(sidecar, indent=2).encode("utf-8")


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


class FileLock:
    """Exclusive lock across processes, held on a lock file."""

    def __init__(self, path):
        self.path = path
        self.file = None

    def __enter__(self):
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        self.file = open(self.path, "a")
        if fcntl is not None:
            fcntl.flock(self.file, fcntl.LOCK_EX)
        return self

    def __exit__(self, *exc):
        if fcntl is not None:
            fcntl.flock(self.file, fcntl.LOCK_UN)
        self.file.close()


class DirLock(FileLock):
    """Exclusive lock on one log directory's files. Held only briefly, for compare-and-replace.

    Resends of the same session can overlap (an earlier request may still be running when the
    game sends again), and a background replay build can finish while a newer upload is being
    saved. "Is the saved copy newer?" and the replace must therefore happen as one step."""

    def __init__(self, directory):
        super().__init__(os.path.join(directory, LOCK_NAME))


def build_lock():
    """Held for the whole of a replay build, so only one runs at a time on the server (each one
    parses a full session upload). Upload requests never take it."""
    return FileLock(os.path.join(LOG_DIR, BUILD_LOCK_NAME))


def append_upload_log(directory, base, entry):
    """Appends one JSON line to <base>.upload_log.jsonl. Best effort: never fails the caller."""
    line = dict(entry, time=datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ"))
    try:
        os.makedirs(directory, exist_ok=True)
        with open(os.path.join(directory, base + UPLOAD_LOG_SUFFIX), "a", encoding="utf-8") as f:
            f.write(json.dumps(line, sort_keys=True) + "\n")
    except OSError:
        traceback.print_exc(file=sys.stderr)


def saved_message_count(csv_path):
    """Rows in an already-saved CSV (header excluded), or -1 if there is no readable one."""
    try:
        with open(csv_path, newline="", encoding="utf-8") as f:
            return max(sum(1 for _ in csv.reader(f)) - 1, 0)
    except (OSError, UnicodeDecodeError, csv.Error):
        return -1


def _remove_if_present(path):
    try:
        os.remove(path)
    except FileNotFoundError:
        pass


def stored_payload_sha256(raw_path):
    """SHA-256 of the payload JSON in an already-saved <base>.upload.json.gz, or None."""
    try:
        with open(raw_path, "rb") as f:
            data, _, error = decode_body(f.read())
    except OSError:
        return None
    return None if error else hashlib.sha256(data).hexdigest()


def save_upload(directory, base, raw_gz, csv_data, message_count, payload_sha256):
    """Saves <base>.csv and <base>.upload.json.gz (raw_gz: the gzipped payload), unless the copy
    already there holds more messages or the same payload.

    Returns (result, existing_count): result is "saved", "kept_newer" (the saved copy has more
    messages) or "duplicate" (the saved copy is this exact payload). existing_count is -1 when
    there was no earlier copy. Saving removes the replay sidecar of the copy it replaces, so a
    sidecar on disk always matches the upload next to it; the background build writes the new one.
    """
    csv_path = os.path.join(directory, base + ".csv")
    raw_path = os.path.join(directory, base + RAW_SUFFIX)
    with DirLock(directory):
        existing = saved_message_count(csv_path)
        if existing > message_count:
            return "kept_newer", existing
        if existing == message_count and stored_payload_sha256(raw_path) == payload_sha256:
            return "duplicate", existing
        atomic_write(raw_path, raw_gz)
        atomic_write(csv_path, csv_data)
        _remove_if_present(os.path.join(directory, base + REPLAY_SUFFIX))
        _remove_if_present(os.path.join(directory, base + REPLAY_ERROR_SUFFIX))
    return "saved", existing


def _file_identity(st):
    return st.st_ino, st.st_size, st.st_mtime_ns


def _sidecar_is_current(raw_path, replay_path):
    try:
        return os.path.getmtime(replay_path) >= os.path.getmtime(raw_path)
    except FileNotFoundError:
        return False


def build_replay(raw_path):
    """Builds <base>.replay.json from <base>.upload.json.gz, one build at a time (build_lock).

    Returns "built", "up_to_date" (a sidecar newer than the upload already exists, e.g. written by
    a build that held the lock first), "failed" (the reason is written to <base>.replay_error.txt)
    or "superseded" (a newer upload replaced the file while this was running; that upload's own
    build writes the sidecar, so this one must not overwrite it with older data).
    """
    directory = os.path.dirname(raw_path)
    base = os.path.basename(raw_path)[:-len(RAW_SUFFIX)]
    replay_path = os.path.join(directory, base + REPLAY_SUFFIX)
    error_path = os.path.join(directory, base + REPLAY_ERROR_SUFFIX)

    with build_lock():
        if _sidecar_is_current(raw_path, replay_path):
            return "up_to_date"
        try:
            with open(raw_path, "rb") as f:
                source = _file_identity(os.fstat(f.fileno()))
                body = f.read()
            data, _, error = decode_body(body)
            if error:
                raise ValueError(error[1])
            payload = json.loads(data.decode("utf-8"))
            verification = verify_replay_chain(payload)
            sidecar = build_replay_sidecar(payload, verification)
        except Exception as e:
            try:
                atomic_write(error_path, traceback.format_exc().encode("utf-8"))
            except OSError:
                pass
            append_upload_log(directory, base, {"event": "replay", "result": "failed",
                                                "error": "{}: {}".format(type(e).__name__, e)[:300]})
            return "failed"

        with DirLock(directory):
            try:
                current = _file_identity(os.stat(raw_path))
            except FileNotFoundError:
                current = None
            if current != source:
                result = "superseded"
            else:
                atomic_write(replay_path, sidecar)
                _remove_if_present(error_path)
                result = "built"
        append_upload_log(directory, base, {"event": "replay", "result": result,
                                            "verified": verification.get("ok")})
        return result


def build_pending(directories=None):
    """Builds every replay sidecar that is missing or older than its upload. Returns a count per
    build_replay result, plus the names of the uploads whose build failed. If a background build
    for the same upload is running, this waits for it (build_lock) and then finds it up to date."""
    counts = {"built": 0, "up_to_date": 0, "failed": 0, "superseded": 0, "failed_files": []}
    for directory in directories or (LOG_DIR, CHECKPOINT_DIR):
        try:
            names = sorted(os.listdir(directory))
        except FileNotFoundError:
            continue
        for name in names:
            if not name.endswith(RAW_SUFFIX):
                continue
            raw_path = os.path.join(directory, name)
            if _sidecar_is_current(raw_path, raw_path[:-len(RAW_SUFFIX)] + REPLAY_SUFFIX):
                continue
            result = build_replay(raw_path)
            counts[result] += 1
            if result == "failed":
                counts["failed_files"].append(raw_path)
    return counts


# CGI request variables. Dropped from the background build's environment so it never looks like a
# web request (see cli()).
_CGI_ENV_KEYS = ("GATEWAY_INTERFACE", "REQUEST_METHOD", "QUERY_STRING", "CONTENT_LENGTH", "CONTENT_TYPE")


def spawn_replay_build(raw_path):
    """Starts build_replay(raw_path) in a background process and returns at once.

    The child gets its own session and none of this process's stdin/stdout/stderr: Apache ends the
    response when this script's stdout closes, so a child holding that pipe would hold the
    response open, and a child in this process group could be killed with it. A failed start is
    not an upload failure; --build-pending catches it up.
    """
    env = {k: v for k, v in os.environ.items() if k not in _CGI_ENV_KEYS}
    try:
        subprocess.Popen(
            [sys.executable, os.path.abspath(__file__), "--build-replay", raw_path],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            close_fds=True, start_new_session=True, env=env)
        return True
    except Exception:
        traceback.print_exc(file=sys.stderr)
        return False


def _inside(path, directory):
    path, directory = os.path.realpath(path), os.path.realpath(directory)
    return os.path.commonpath([path, directory]) == directory


def cli(args):
    """Command-line modes, for the background build and for catch-up. Refused under CGI: Apache
    passes a query string without "=" to a CGI script as command-line arguments, so a request
    must never be able to reach these."""
    if os.environ.get("GATEWAY_INTERFACE"):
        send_response(400, "Command-line modes are not available over HTTP")
        return 2

    if args[0] == "--build-replay":
        if len(args) != 2 or not args[1].endswith(RAW_SUFFIX) or not _inside(args[1], LOG_DIR):
            print("usage: save_game_logs.py --build-replay <file{}> (inside {})".format(RAW_SUFFIX, LOG_DIR),
                  file=sys.stderr)
            return 2
        return 0 if build_replay(args[1]) != "failed" else 1

    counts = build_pending(args[1:] or None)
    print("Replay sidecars: {built} built, {up_to_date} already up to date, {failed} failed, "
          "{superseded} superseded.".format(**counts))
    for path in counts["failed_files"]:
        print("  failed: {} (see {})".format(path, path[:-len(RAW_SUFFIX)] + REPLAY_ERROR_SUFFIX))
    return 1 if counts["failed"] else 0


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

        data, compressed, error = decode_body(raw)
        if error:
            send_response(*error)
            return

        payload, messages, error = parse_payload(data)
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
            directory = CHECKPOINT_DIR
            base = "{}_{}_checkpoint_day{}".format(safe_name, safe_session, day)
        else:
            directory = LOG_DIR
            base = "{}_{}_final".format(safe_name, safe_session)

        payload_sha256 = hashlib.sha256(data).hexdigest()
        # A gzipped body is stored exactly as received; a plain one is gzipped here (before any
        # lock is taken: it is the slow part).
        raw_gz = raw if compressed else gzip.compress(data, compresslevel=6)
        csv_data = build_csv(payload, messages)
        result, existing = save_upload(directory, base, raw_gz, csv_data, len(messages), payload_sha256)

        info = request_info()
        append_upload_log(directory, base, {
            "event": "upload", "result": result, "kind": kind,
            "uploadId": info["uploadId"], "attempt": info["attempt"],
            "bytes": len(raw), "payloadBytes": len(data), "compressed": compressed,
            "messages": len(messages), "sha256": payload_sha256,
            "userAgent": os.environ.get("HTTP_USER_AGENT", "")[:200],
        })

        filename = base + ".csv"
        if result == "saved":
            message = "Saved {} messages ({} upload) to {}".format(len(messages), kind, filename)
        elif result == "duplicate":
            message = "Already saved: {} holds this exact upload".format(filename)
        else:
            # Still a success: everything in this upload is already in the saved copy.
            message = "Kept {}: it already holds {} messages, more than the {} in this upload".format(
                filename, existing, len(messages))

        send_response(
            200,
            message,
            bytes=len(raw),
            payload_bytes=len(data),
            compressed=compressed,
            messages=len(messages),
            result=result,
            duplicate=result == "duplicate",
            saved_bytes=len(csv_data) if result == "saved" else 0,
            kind=kind,
            file=filename,
            replaced_earlier_copy=result == "saved" and existing >= 0,
            replay_file=base + REPLAY_SUFFIX,
            replay_pending=not os.path.exists(os.path.join(directory, base + REPLAY_SUFFIX)),
        )
        sys.stdout.flush()

        # After the reply: the replay check is too slow to make the game wait for it.
        if result == "saved":
            spawn_replay_build(os.path.join(directory, base + RAW_SUFFIX))

    except OSError as e:
        traceback.print_exc(file=sys.stderr)  # full detail goes to the web server's error log
        send_response(500, "File write error ({})".format(type(e).__name__))
    except Exception as e:
        traceback.print_exc(file=sys.stderr)
        send_response(500, "Server error ({})".format(type(e).__name__))


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] in ("--build-replay", "--build-pending"):
        sys.exit(cli(sys.argv[1:]))
    try:
        main()
    except Exception:
        # Anything that escaped main() must still come back as a real error status, not a
        # crashed CGI (which many servers turn into an ambiguous empty response).
        traceback.print_exc(file=sys.stderr)
        send_response(500, "Server error")
