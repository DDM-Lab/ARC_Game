#!/usr/bin/env python3
"""End-to-end platform test: every collaborator-facing endpoint and validation point.

Drives a RUNNING router over HTTP the way a collaborator would — this is deliberately not
hermetic, because the things most likely to break (auth gates, namespacing, warning
surfacing, capability enforcement) only exist at the HTTP boundary.

    python agent_router.py --port 9876 --admin-port 9877 &
    ./.venv/bin/python test_platform_e2e.py

Covers:
  A. capability bundles   (bundles/testkit/*.json)      -> must upload cleanly
  B. reject cases         (testkit/negative/R*.json)    -> must be REFUSED
  C. warn cases           (testkit/negative/W*.json)    -> must upload WITH warnings
  D. endpoints            /health /whoami /configs /bundles /plugins /my/*
  E. capability gates     participant key must NOT be able to upload code
  F. namespacing          uploads land under the KEY's label, not the manifest's
"""
from __future__ import annotations

import json
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

URL = "http://127.0.0.1:9876"
ADMIN = "http://127.0.0.1:9877"
KEY = "dev-local-key"
KIT = Path("bundles/testkit")

FAILS: list[str] = []
CHECKS = 0


def check(label: str, cond: bool, detail: str = "") -> bool:
    global CHECKS
    CHECKS += 1
    print(f"  {'PASS' if cond else 'FAIL'}  {label}" + (f"   -- {detail}" if not cond else ""))
    if not cond:
        FAILS.append(label)
    return cond


def call(path: str, key: str = KEY, method: str = "GET", body: bytes | None = None,
         base: str = URL, ctype: str = "application/json"):
    req = urllib.request.Request(base + path, data=body, method=method,
                                 headers={"Authorization": f"Bearer {key}",
                                          "Content-Type": ctype})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            raw = r.read()
            try:
                # The export endpoint returns gzip, which raises UnicodeDecodeError (not
                # JSONDecodeError) from json.loads — catch both or binary responses crash.
                return r.status, json.loads(raw)
            except (json.JSONDecodeError, UnicodeDecodeError):
                return r.status, raw
    except urllib.error.HTTPError as e:
        raw = e.read().decode(errors="replace")
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, raw
    except urllib.error.URLError as e:
        return 0, str(e.reason)


def push(path: Path, key: str = KEY, base_qs: str = ""):
    return call("/bundles" + base_qs, key=key, method="POST", body=path.read_bytes())


# ── D. endpoints ────────────────────────────────────────────────────────────
def test_endpoints():
    print("\n[D] endpoints")
    st, h = call("/health")
    if not check("GET /health -> 200", st == 200, f"got {st}: {h}"):
        print("\n  router not running; start it first:")
        print("    ./.venv/bin/python agent_router.py --port 9876 --admin-port 9877 &")
        raise SystemExit(1)

    st, me = call("/whoami")
    check("GET /whoami -> 200", st == 200, f"got {st}")
    check("/whoami reports capabilities", isinstance(me, dict) and "capabilities" in me,
          f"got {me}")
    st, _ = call("/whoami", key="bogus-key")
    check("GET /whoami with bad key -> 401", st == 401, f"got {st}")

    st, cfgs = call("/configs")
    check("GET /configs -> 200", st == 200, f"got {st}")
    st, _ = call("/configs", key="bogus-key")
    check("GET /configs with bad key -> 401", st == 401, f"got {st}")

    st, s = call("/my/sessions")
    check("GET /my/sessions -> 200", st == 200, f"got {st}")

    st, _ = call("/my/sessions/export?format=tar")
    check("GET /my/sessions/export -> 200", st == 200, f"got {st}")

    # Route-ordering guard: /export must not be swallowed by /{session_id}.
    check("export is NOT parsed as a session id", st == 200,
          "the literal 'export' matched the {session_id} route")

    st, _ = call("/plugins")
    check("GET /plugins -> 200", st == 200, f"got {st}")

    st, _ = call("/admin/keys")
    check("data plane does NOT serve /admin/* (404)", st == 404, f"got {st}")

    st, _ = call("/admin/keys", base=ADMIN, key="bogus-key")
    check("admin plane rejects an unknown key (401)", st == 401, f"got {st}")
    st, _ = call("/admin/keys", base=ADMIN, key=KEY)
    check("admin plane accepts the admin key", st == 200, f"got {st}")


# ── A. capability bundles ───────────────────────────────────────────────────
def test_capability_bundles():
    print("\n[A] capability bundles — must upload cleanly")
    files = sorted(KIT.glob("*.json"))
    check("test-kit bundles present", len(files) >= 12, f"found {len(files)}")
    for f in files:
        qs = ""
        if "delta" in json.loads(f.read_text()):
            qs = "?base=" + urllib.parse.quote("continuous_all_officers_ddmlab")
        st, resp = push(f, base_qs=qs)
        okk = st == 200 and isinstance(resp, dict) and resp.get("status") == "ok"
        check(f"upload {f.stem}", okk, f"HTTP {st}: {resp}")
        if okk and f.stem in ("01-baseline", "06-tooldescriptions"):
            check(f"  {f.stem} reports no warnings", not resp.get("warnings"),
                  f"warnings={resp.get('warnings')}")


# ── B. reject cases ─────────────────────────────────────────────────────────
def test_rejects():
    print("\n[B] invalid bundles — must be REFUSED")
    for f in sorted((KIT / "negative").glob("R*.json")):
        qs = ""
        if "delta" in json.loads(f.read_text()):
            qs = "?base=" + urllib.parse.quote("continuous_all_officers_ddmlab")
        st, resp = push(f, base_qs=qs)
        check(f"reject {f.stem}", st >= 400,
              f"ACCEPTED with HTTP {st} — this is a validation hole! {resp}")


# ── C. warn cases ───────────────────────────────────────────────────────────
def test_warns():
    print("\n[C] risky-but-valid bundles — must upload WITH warnings")
    expect = {
        "W1-no-talkinghead": "talkinghead_endpoint",
        "W2-taskchoice-only": "task_choice",
        "W3-empty-scope": "empty subaction_space",
        "W4-gutted-toolpolicy": "tool_policy",
        "W5-tooldesc-typo": "tool_descriptions",
    }
    # W6 moved to the reject set: a duplicate talkinghead slot RAISES in RouterConfig, so a
    # config carrying it is unusable, not merely risky — it must fail at upload.
    for f in sorted((KIT / "negative").glob("W*.json")):
        if f.stem.startswith("W6"):
            st, resp = push(f)
            check("W6-dup-talkinghead is REJECTED (unusable, not just risky)",
                  st >= 400, f"accepted with HTTP {st}")
            continue
        st, resp = push(f)
        if not check(f"{f.stem} uploads (200)", st == 200, f"HTTP {st}: {resp}"):
            continue
        warns = " ".join(resp.get("warnings") or []) if isinstance(resp, dict) else ""
        needle = expect.get(f.stem, "")
        check(f"  {f.stem} warns about {needle!r}", needle.lower() in warns.lower(),
              f"warnings={resp.get('warnings')}")


# ── E/F. capability gates + namespacing ─────────────────────────────────────
def test_gates_and_namespacing():
    print("\n[E] capability gates")
    st, minted = call("/admin/keys", base=ADMIN, method="POST",
                      body=json.dumps({"cohort": "e2e-participant", "count": 1,
                                       "caps": []}).encode())
    if not check("mint a no-caps participant key", st == 200 and minted.get("keys"),
                 f"HTTP {st}: {minted}"):
        return
    pk = minted["keys"][0]

    st, _ = call("/plugins?name=evil", key=pk, method="POST", body=b"x = 1\n",
                 ctype="text/plain")
    check("participant CANNOT upload code (403)", st == 403, f"got {st}")

    st, me = call("/whoami", key=pk)
    check("participant /whoami works", st == 200, f"got {st}")
    check("participant reports can_upload_code=False",
          isinstance(me, dict) and me.get("can_upload_code") is False, f"got {me}")

    st, _ = call("/admin/keys", base=ADMIN, key=pk)
    check("participant CANNOT reach admin mint (403)", st == 403, f"got {st}")

    print("\n[F] namespacing — uploads land under the KEY's label")
    st, resp = push(KIT / "01-baseline.json")
    if check("baseline re-upload ok", st == 200, f"got {st}"):
        name = resp.get("name", "")
        check("stored name is <key-label>__<slug>, not the manifest owner",
              name.startswith("dev__") and "testkit__" not in name,
              f"stored as {name!r}")


def main() -> int:
    print("=" * 74)
    print("CORA platform end-to-end  (router must be running on :9876 / admin :9877)")
    print("=" * 74)
    test_endpoints()
    test_capability_bundles()
    test_rejects()
    test_warns()
    test_gates_and_namespacing()
    print("\n" + "=" * 74)
    if FAILS:
        print(f"FAILED {len(FAILS)}/{CHECKS}:")
        for f in FAILS:
            print(f"  - {f}")
        return 1
    print(f"all {CHECKS} checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
