"""The search wing's surrogate (oracle/sim) reproduces captured Unity games exactly.

Each fixture in tests/fixtures/sim_parity is a seeded headless game (noop, a baseline, or a
baseline with random exploration baskets) reduced by oracle/sim/parity.py to what Unity accepted
per decision and what it then showed: the port must hold Unity's RNG state at every decision
and match its observation and score (cora.scoring, to 4 dp) after every one. Fixtures that
record their tool calls (MCTS plans replayed on Unity) are also replayed call by call through
cora.executor, as search drives the port: every call must get Unity's outcome, and the game
Unity's final score. Regenerate after a build change with oracle.sim.capture + oracle.sim.parity.
"""
import glob
import gzip
import json
import os

import pytest

from oracle.sim.parity import check, check_calls

FIXTURES = sorted(glob.glob(os.path.join(os.path.dirname(__file__), "fixtures", "sim_parity", "*.json.gz")))


CALL_FIXTURES = [p for p in FIXTURES if "calls" in json.load(gzip.open(p, "rt"))]
# Replaying Unity's accepted actions (`taken`) matches each answer to the port's open tasks by type
# and facility in offer order, which is ambiguous when a plan keeps several same-type tasks open;
# these fixtures are held to the stricter call-by-call replay below instead.
TAKEN_FIXTURES = [p for p in FIXTURES if p not in CALL_FIXTURES]


@pytest.mark.parametrize("path", TAKEN_FIXTURES, ids=[os.path.basename(p)[:-8] for p in TAKEN_FIXTURES])
def test_surrogate_reproduces_capture(path):
    with gzip.open(path, "rt") as f:
        fixture = json.load(f)
    assert check(fixture) == []


@pytest.mark.parametrize("path", CALL_FIXTURES, ids=[os.path.basename(p)[:-8] for p in CALL_FIXTURES])
def test_surrogate_reproduces_calls(path):
    with gzip.open(path, "rt") as f:
        fixture = json.load(f)
    assert check_calls(fixture) == []
