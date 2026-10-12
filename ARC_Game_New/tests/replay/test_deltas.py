"""State-delta and replay-chain tests for save_game_logs.py.

Run from the repository root:

    python -m unittest discover -s tests/replay -v

delta_cases.tsv is written by the C# harness (tests/replay/csharp_harness) from the real
StateDiff code. Applying those deltas here checks that the game's output is accepted by the
server-side implementation, which is the direction that matters in production.
"""

import base64
import copy
import json
import os
import sys
import unittest
from decimal import Decimal

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, REPO)

import save_game_logs as sgl  # noqa: E402

FIXTURE = os.path.join(HERE, "fixtures", "snapshot_synthetic.json")
DELTA_CASES = os.path.join(HERE, "delta_cases.tsv")


def _fixture_text():
    with open(FIXTURE, encoding="utf-8") as f:
        return f.read()


def _s0():
    text, _ = sgl.canonicalize_snapshot(_fixture_text())
    return sgl.canon_parse(text)


def _h(tree):
    return sgl._state_hash(tree)


def _chain():
    """A small valid chain: S0 (anchor) -> S1 -> S2 -> S3, with one uncaptured click first and a
    keyframe at S2. Returns (payload, [S0, S1, S2, S3])."""
    s0 = _s0()
    s1 = copy.deepcopy(s0)
    s1["economy"]["currentBudget"] = Decimal(9000)
    s2 = copy.deepcopy(s1)
    del s2["relocations"]["walks"]["3"]
    s3 = copy.deepcopy(s2)
    s3["vehicles"]["Vehicle 3"] = copy.deepcopy(s2["vehicles"]["Vehicle 1"])

    def delta_text(base_seq, before, after):
        ops = sgl.state_diff(before, after)
        return sgl.canon_dumps(sgl.build_state_delta(base_seq, _h(before), _h(after), ops))

    s2_json = sgl.canon_dumps(s2)
    ledger = [
        {"seq": 0, "source": "player", "type": "Click", "captured": False},
        {"seq": 1, "source": "player", "type": "Build", "captured": True,
         "stateDeltaJson": delta_text(-1, s0, s1)},
        {"seq": 2, "source": "system", "type": "Keyframe", "captured": True,
         "stateDeltaJson": delta_text(1, s1, s2), "stateJson": s2_json, "stateHash": _h(s2)},
        {"seq": 3, "source": "player", "type": "AssignWorker", "captured": True,
         "stateDeltaJson": delta_text(2, s2, s3)},
    ]
    payload = {
        "initialSnapshotJson": _fixture_text(),
        "initialStateHash": _h(s0),
        "canonicalSchemaVersion": sgl.CANON_SCHEMA_VERSION,
        "ledger": ledger,
        "roundCheckpoints": [{"day": 1, "round": 3, "trigger": "RoundEnd", "ledgerSeq": 2, "stateHash": _h(s2)}],
    }
    return payload, [s0, s1, s2, s3]


def _entry(payload, seq):
    return next(e for e in payload["ledger"] if e["seq"] == seq)


def _errors(payload):
    return sgl.verify_replay_chain(payload)["errors"]


class CSharpDeltasApplyInPython(unittest.TestCase):
    def test_every_csharp_delta_reproduces_its_after_state(self):
        rows = 0
        with open(DELTA_CASES, encoding="utf-8") as f:
            for line in f:
                line = line.rstrip("\n")
                if not line:
                    continue
                rows += 1
                before_text, after_text, delta_text = (base64.b64decode(p).decode("utf-8") for p in line.split("\t"))
                before = sgl.canon_parse(before_text)
                delta = sgl.canon_parse(delta_text)
                applied = sgl.apply_state_delta(copy.deepcopy(before), delta)
                self.assertEqual(sgl.canon_dumps(applied), after_text, "row {}".format(rows))
        self.assertGreater(rows, 0)

    def test_corrupted_csharp_deltas_are_rejected(self):
        # The first row with at least one op ("no change" has none, so nothing to drop there).
        with open(DELTA_CASES, encoding="utf-8") as f:
            for line in f:
                line = line.rstrip("\n")
                if not line:
                    continue
                before_text, _, delta_text = (base64.b64decode(p).decode("utf-8") for p in line.split("\t"))
                delta = sgl.canon_parse(delta_text)
                if delta["ops"]:
                    break
        before = sgl.canon_parse(before_text)

        dropped = copy.deepcopy(delta)
        dropped["ops"].pop()
        bad_base = copy.deepcopy(delta)
        bad_base["baseHash"] = "0" * 64
        for name, bad in (("dropped op", dropped), ("bad baseHash", bad_base)):
            with self.subTest(case=name):
                with self.assertRaises(sgl.CanonicalStateError):
                    sgl.apply_state_delta(copy.deepcopy(before), bad)


class PythonDeltaRoundTrip(unittest.TestCase):
    def test_diff_then_apply_reproduces_the_target(self):
        s0 = _s0()
        targets = []
        t = copy.deepcopy(s0)
        t["economy"]["currentBudget"] = Decimal(1)
        targets.append(t)
        t = copy.deepcopy(s0)
        t["clock"] = Decimal(5)          # object -> leaf
        targets.append(t)
        t = copy.deepcopy(s0)
        del t["vehicles"]["Vehicle 1"]   # remove a keyed entity
        targets.append(t)
        t = copy.deepcopy(s0)
        t["flood"]["x.y"] = Decimal(1)   # a key containing a dot
        targets.append(t)
        for target in targets:
            with self.subTest(target=sgl.canon_dumps(target)[:60]):
                ops = sgl.state_diff(s0, target)
                delta = sgl.build_state_delta(0, _h(s0), _h(target), ops)
                self.assertEqual(sgl.canon_dumps(sgl.apply_state_delta(copy.deepcopy(s0), delta)),
                                 sgl.canon_dumps(target))

    def test_no_change_gives_no_ops(self):
        s0 = _s0()
        self.assertEqual(sgl.state_diff(s0, copy.deepcopy(s0)), [])


class ChainVerifier(unittest.TestCase):
    def test_valid_chain_verifies(self):
        payload, _ = _chain()
        report = sgl.verify_replay_chain(payload)
        self.assertTrue(report["ok"], report["errors"])
        self.assertEqual(report["capturedEntries"], 3)
        self.assertEqual(report["keyframes"], 1)
        self.assertEqual(report["uncapturedEntries"], 1)
        self.assertEqual(report["lastCapturedSeq"], 3)

    def test_sidecar_carries_the_verification(self):
        payload, _ = _chain()
        sidecar = json.loads(sgl.build_replay_sidecar(payload).decode("utf-8"))
        self.assertTrue(sidecar["replayVerification"]["ok"])
        self.assertIn("stateDelta", sidecar["ledger"][1])
        self.assertNotIn("stateDelta", sidecar["ledger"][0])

    def test_tampered_delta_value_is_rejected(self):
        payload, _ = _chain()
        delta = sgl.canon_parse(_entry(payload, 1)["stateDeltaJson"])
        for op in delta["ops"]:
            if op["op"] == "set":
                op["value"] = Decimal(9001)
        _entry(payload, 1)["stateDeltaJson"] = sgl.canon_dumps(delta)
        errs = _errors(payload)
        self.assertTrue(any("does not apply" in e for e in errs), errs)

    def test_dropped_entry_is_a_sequence_gap(self):
        payload, _ = _chain()
        payload["ledger"] = [e for e in payload["ledger"] if e["seq"] != 1]
        self.assertTrue(any("sequence gap" in e for e in _errors(payload)))

    def test_reordered_entries_are_rejected(self):
        payload, _ = _chain()
        payload["ledger"][1], payload["ledger"][2] = payload["ledger"][2], payload["ledger"][1]
        self.assertTrue(any("sequence gap" in e for e in _errors(payload)))

    def test_wrong_baseHash_is_rejected(self):
        payload, _ = _chain()
        delta = sgl.canon_parse(_entry(payload, 2)["stateDeltaJson"])
        delta["baseHash"] = "1" * 64
        _entry(payload, 2)["stateDeltaJson"] = sgl.canon_dumps(delta)
        self.assertTrue(any("baseHash does not match" in e for e in _errors(payload)))

    def test_wrong_baseSeq_is_rejected(self):
        payload, _ = _chain()
        delta = sgl.canon_parse(_entry(payload, 3)["stateDeltaJson"])
        delta["baseSeq"] = 0
        _entry(payload, 3)["stateDeltaJson"] = sgl.canon_dumps(delta)
        self.assertTrue(any("baseSeq" in e for e in _errors(payload)))

    def test_keyframe_stateHash_mismatch_is_rejected(self):
        payload, _ = _chain()
        _entry(payload, 2)["stateHash"] = "2" * 64
        errs = _errors(payload)
        self.assertTrue(any("does not match its stateJson" in e for e in errs), errs)

    def test_keyframe_state_altered_is_rejected(self):
        payload, _ = _chain()
        _entry(payload, 2)["stateJson"] = _entry(payload, 2)["stateJson"].replace("9000", "9001")
        self.assertTrue(_errors(payload))

    def test_keyframe_without_state_is_rejected(self):
        payload, _ = _chain()
        del _entry(payload, 2)["stateJson"]
        self.assertTrue(any("no stateJson" in e for e in _errors(payload)))

    def test_captured_entry_without_delta_is_rejected(self):
        payload, _ = _chain()
        del _entry(payload, 1)["stateDeltaJson"]
        self.assertTrue(any("no state delta" in e for e in _errors(payload)))

    def test_uncaptured_entry_carrying_a_delta_is_rejected(self):
        payload, _ = _chain()
        _entry(payload, 0)["stateDeltaJson"] = _entry(payload, 1)["stateDeltaJson"]
        self.assertTrue(any("uncaptured entry carries" in e for e in _errors(payload)))

    def test_captured_click_is_rejected(self):
        payload, _ = _chain()
        _entry(payload, 0)["captured"] = True
        self.assertTrue(any("click entries must not be captured" in e for e in _errors(payload)))

    def test_keyframe_that_failed_to_capture_is_reported(self):
        payload, _ = _chain()
        kf = _entry(payload, 2)
        kf["captured"] = False
        kf["captureError"] = "boom"
        del kf["stateDeltaJson"]
        self.assertTrue(any("keyframe has no captured state" in e for e in _errors(payload)))

    def test_roundcheckpoint_must_match_its_keyframe(self):
        payload, _ = _chain()
        payload["roundCheckpoints"][0]["stateHash"] = "3" * 64
        self.assertTrue(any("roundCheckpoint" in e for e in _errors(payload)))

    def test_altered_initial_snapshot_is_rejected(self):
        payload, _ = _chain()
        payload["initialSnapshotJson"] = _fixture_text().replace('"currentBudget": 9500', '"currentBudget": 9501')
        self.assertTrue(any("does not match initialStateHash" in e for e in _errors(payload)))

    def test_legacy_upload_without_chain_is_not_an_error(self):
        # Uploads from before this change have no anchor and no deltas. They are reported as a
        # warning, not a failure, so old sessions still verify as "nothing to check".
        report = sgl.verify_replay_chain({"ledger": [{"seq": 0, "source": "player", "type": "Build"}]})
        self.assertTrue(report["ok"], report["errors"])
        self.assertTrue(report["warnings"])
        self.assertEqual(report["capturedEntries"], 0)

    def test_garbage_is_reported_never_raised(self):
        payload, _ = _chain()
        _entry(payload, 1)["stateDeltaJson"] = "{not json"
        _entry(payload, 2)["stateJson"] = "[1,2"
        report = sgl.verify_replay_chain(payload)
        self.assertFalse(report["ok"])

    def test_verifier_does_not_mutate_the_payload(self):
        payload, _ = _chain()
        before = copy.deepcopy(payload)
        sgl.verify_replay_chain(payload)
        self.assertEqual(payload, before)


if __name__ == "__main__":
    unittest.main()
