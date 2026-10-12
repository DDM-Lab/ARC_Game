"""Converts canonical_vectors.json into canonical_cases.tsv for the Unity EditMode test.

Unity's test code reads the TSV rather than parsing JSON, so the test has no parser of its own
to share a bug with the code under test. Each line is:

    kind \t base64(input) \t base64(expected) or ERROR \t expectedHash (snapshot rows only)

Run from the repository root after editing canonical_vectors.json:

    python tests/replay/make_canonical_cases.py

test_canonical.py fails if the TSV is out of date, so the two files cannot drift apart.
"""

import base64
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
VECTORS = os.path.join(HERE, "canonical_vectors.json")
CASES = os.path.join(HERE, "canonical_cases.tsv")


def build_rows():
    import sys
    sys.path.insert(0, REPO)
    import save_game_logs as sgl

    def b64(text):
        return base64.b64encode(text.encode("utf-8")).decode("ascii")

    with open(VECTORS, encoding="utf-8") as f:
        v = json.load(f)

    rows = []
    for c in v["numbers"]:
        rows.append(["json", b64(c["in"]), b64(c["out"])])
    for c in v["json"]:
        rows.append(["json", b64(c["in"]), b64(c["out"])])
    for text in v["numberErrors"] + v["jsonErrors"]:
        rows.append(["json", b64(text), "ERROR"])
    for c in v["snapshots"]:
        with open(os.path.join(REPO, c["fixture"]), encoding="utf-8") as f:
            text = f.read()
        out, digest = sgl.canonicalize_snapshot(text)
        rows.append(["snapshot", b64(text), b64(out), digest])
    for c in v["snapshotInline"]:
        rows.append(["snapshot", b64(c["in"]), b64(c["out"])])
    for text in v["snapshotErrors"]:
        rows.append(["snapshot", b64(text), "ERROR"])
    return rows


def render(rows):
    return "".join("\t".join(r) + "\n" for r in rows)


if __name__ == "__main__":
    with open(CASES, "w", encoding="utf-8", newline="\n") as f:
        f.write(render(build_rows()))
    print("wrote", CASES)
