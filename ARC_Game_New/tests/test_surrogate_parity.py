"""The search wing's surrogate (oracle/sim) reproduces captured Unity games exactly.

Each fixture in tests/fixtures/sim_parity is a seeded headless game (noop, a baseline, or a
baseline with random exploration baskets) reduced by oracle/sim/parity.py to what Unity accepted
per decision and what it then showed: the port must hold Unity's RNG state at every decision
and match its observation and score (cora.scoring, to 4 dp) after every one. Regenerate after a
build change with oracle.sim.capture + oracle.sim.parity.
"""
import glob
import gzip
import json
import os

import pytest

from oracle.sim.parity import check

FIXTURES = sorted(glob.glob(os.path.join(os.path.dirname(__file__), "fixtures", "sim_parity", "*.json.gz")))


@pytest.mark.parametrize("path", FIXTURES, ids=[os.path.basename(p)[:-8] for p in FIXTURES])
def test_surrogate_reproduces_capture(path):
    with gzip.open(path, "rt") as f:
        fixture = json.load(f)
    assert check(fixture) == []
