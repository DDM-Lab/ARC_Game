"""Canonical-state tests for save_game_logs.py.

Run from the repository root:

    python -m unittest discover -s tests/replay -v

The golden vectors in canonical_vectors.json are shared with the C# side
(Assets/Scripts/GameLog/Replay/CanonicalJson.cs). Both languages must produce the same text and
hash for every vector, or replay verification would reject good sessions.
"""

import copy
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, REPO)

import save_game_logs as sgl  # noqa: E402

VECTORS_PATH = os.path.join(HERE, "canonical_vectors.json")
with open(VECTORS_PATH, encoding="utf-8") as f:
    VECTORS = json.load(f)


def _read(rel):
    with open(os.path.join(REPO, rel), encoding="utf-8") as f:
        return f.read()


class NumberVectors(unittest.TestCase):
    def test_number_rounding_matches_vectors(self):
        for case in VECTORS["numbers"]:
            with self.subTest(number=case["in"]):
                self.assertEqual(sgl.canonicalize_json(case["in"]), case["out"])

    def test_out_of_range_and_non_finite_numbers_are_rejected(self):
        for text in VECTORS["numberErrors"]:
            with self.subTest(number=text):
                with self.assertRaises((sgl.CanonicalStateError, ValueError)):
                    sgl.canonicalize_json(text)

    def test_ties_round_to_even(self):
        # 0.0000025 is exactly halfway between 0.000002 and 0.000003; the even neighbour wins.
        self.assertEqual(sgl.canonicalize_json("0.0000025"), "0.000002")
        self.assertEqual(sgl.canonicalize_json("0.0000035"), "0.000004")

    def test_integral_floats_and_ints_are_the_same_value(self):
        # JsonUtility writes a float of 5.0 as "5", so the two must canonicalise identically.
        self.assertEqual(sgl.canonicalize_json("5.0"), sgl.canonicalize_json("5"))


class JsonVectors(unittest.TestCase):
    def test_json_vectors(self):
        for case in VECTORS["json"]:
            with self.subTest(json=case["in"]):
                self.assertEqual(sgl.canonicalize_json(case["in"]), case["out"])

    def test_invalid_json_is_rejected(self):
        for text in VECTORS["jsonErrors"]:
            with self.subTest(json=text):
                with self.assertRaises((sgl.CanonicalStateError, ValueError)):
                    sgl.canonicalize_json(text)

    def test_canonical_text_is_a_fixed_point(self):
        for case in VECTORS["json"]:
            once = sgl.canonicalize_json(case["in"])
            self.assertEqual(sgl.canonicalize_json(once), once)


class CaseFileInSync(unittest.TestCase):
    def test_unity_case_file_matches_vectors(self):
        # The Unity EditMode test reads canonical_cases.tsv. If the vectors change without
        # regenerating it, Unity would test stale expectations.
        sys.path.insert(0, HERE)
        import make_canonical_cases as mk
        with open(mk.CASES, encoding="utf-8") as f:
            self.assertEqual(f.read(), mk.render(mk.build_rows()))


class SnapshotVectors(unittest.TestCase):
    def test_fixture_matches_golden_canonical_text_and_hash(self):
        for case in VECTORS["snapshots"]:
            with self.subTest(fixture=case["fixture"]):
                text, digest = sgl.canonicalize_snapshot(_read(case["fixture"]))
                self.assertEqual(text, case["out"])
                self.assertEqual(digest, case["hash"])

    def test_inline_snapshot_vectors(self):
        for case in VECTORS["snapshotInline"]:
            with self.subTest(snapshot=case["in"]):
                text, _ = sgl.canonicalize_snapshot(case["in"])
                self.assertEqual(text, case["out"])

    def test_invalid_snapshots_are_rejected(self):
        for text in VECTORS["snapshotErrors"]:
            with self.subTest(snapshot=text):
                with self.assertRaises(sgl.CanonicalStateError):
                    sgl.canonicalize_snapshot(text)

    def test_volatile_root_fields_do_not_change_the_hash(self):
        base = _read("tests/replay/fixtures/snapshot_synthetic.json")
        doc = json.loads(base)
        doc["createdUtc"] = "2030-12-31T23:59:59Z"
        doc["seed"] = 999999
        self.assertEqual(sgl.canonicalize_snapshot(base)[1],
                         sgl.canonicalize_snapshot(json.dumps(doc))[1])

    def test_keyed_lists_ignore_array_order(self):
        # Reordering workers (same ids) is not a state change. Keying by workerId is what makes
        # this hold, and it is the reason keyed maps are used instead of positional arrays.
        doc = json.loads(_read("tests/replay/fixtures/snapshot_synthetic.json"))
        reordered = copy.deepcopy(doc)
        reordered["workforce"]["workers"].reverse()
        reordered["vehicles"].reverse()
        self.assertEqual(sgl.canonicalize_snapshot(json.dumps(doc))[1],
                         sgl.canonicalize_snapshot(json.dumps(reordered))[1])

    def test_changing_one_keyed_item_changes_only_that_key(self):
        doc = json.loads(_read("tests/replay/fixtures/snapshot_synthetic.json"))
        before = json.loads(sgl.canonicalize_snapshot(json.dumps(doc))[0])
        doc["relocations"]["walks"][0]["quantity"] = 3
        after = json.loads(sgl.canonicalize_snapshot(json.dumps(doc))[0])
        self.assertEqual(sorted(before["relocations"]["walks"]), ["3"])
        self.assertNotEqual(before["relocations"]["walks"]["3"], after["relocations"]["walks"]["3"])
        for key in before:
            if key != "relocations":
                self.assertEqual(before[key], after[key], key)

    def test_removing_an_entity_does_not_shift_its_neighbours(self):
        doc = json.loads(_read("tests/replay/fixtures/snapshot_synthetic.json"))
        full = json.loads(sgl.canonicalize_snapshot(json.dumps(doc))[0])
        doc["workforce"]["workers"] = [w for w in doc["workforce"]["workers"] if w["workerId"] != 2]
        less = json.loads(sgl.canonicalize_snapshot(json.dumps(doc))[0])
        self.assertNotIn("2", less["workforce"]["workers"])
        self.assertEqual(full["workforce"]["workers"]["10"], less["workforce"]["workers"]["10"])
        self.assertEqual(full["workforce"]["workers"]["1"], less["workforce"]["workers"]["1"])

    def test_duplicate_id_is_rejected_not_silently_merged(self):
        doc = json.loads(_read("tests/replay/fixtures/snapshot_synthetic.json"))
        doc["vehicles"].append(copy.deepcopy(doc["vehicles"][0]))
        with self.assertRaises(sgl.CanonicalStateError):
            sgl.canonicalize_snapshot(json.dumps(doc))

    def test_float_text_and_reserialised_float_agree(self):
        # The verifier re-canonicalises a sidecar that was written by json.dumps. That is only
        # sound if re-serialising a parsed float does not change its canonical value.
        for text in ("0.0000015", "1E-05", "12.5", "0.3333333", "-0.0000025"):
            with self.subTest(number=text):
                self.assertEqual(sgl.canonicalize_json(text),
                                 sgl.canonicalize_json(json.dumps(json.loads(text))))

    def test_real_day1_snapshot_when_available(self):
        # Local-only check against a participant export outside the repository. Skipped when the
        # file is absent, so it never fails on another machine. It exercises a real snapshot.
        candidate = os.path.abspath(os.path.join(REPO, "..", "Unknown_unknown_20261006_155449_379.replay.json"))
        if not os.path.exists(candidate):
            self.skipTest("real replay export not present")
        with open(candidate, encoding="utf-8") as f:
            snap = json.load(f)["initialSnapshot"]
        _, digest = sgl.canonicalize_snapshot(json.dumps(snap))
        self.assertEqual(len(digest), 64)
        self.assertEqual(sgl.canonicalize_snapshot(json.dumps(snap))[1], digest)


if __name__ == "__main__":
    unittest.main()
