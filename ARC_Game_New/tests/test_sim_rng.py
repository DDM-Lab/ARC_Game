"""The surrogate's UnityRandom reproduces UnityEngine.Random.

Single draws against transitions captured from the game (oracle/sim/corpus/rng_transitions.json),
the batch and integer-threshold fast paths against the plain ones, and seeding: every parity
fixture starts from rng.game_start(seed) (Random.InitState plus the scene's load draws).
"""
import glob
import gzip
import json
import os

import pytest

from oracle.sim.rng import UnityRandom, game_start, threshold_for

HERE = os.path.dirname(__file__)
START = (878108152, 3570879193, 3354241182, 3355245330)


def test_single_draws_match_unity():
    path = os.path.join(HERE, "..", "oracle", "sim", "corpus", "rng_transitions.json")
    pairs = [(tuple(p["before"]), tuple(p["after"])) for p in json.load(open(path))["pairs"]]
    assert pairs
    for before, after in pairs:
        r = UnityRandom(state=before)
        r.next_uint()
        assert r.get_state() == after


@pytest.mark.parametrize("k", [1, 2, 7, 50, 331])
def test_batch_equals_sequential(k):
    a, b = UnityRandom(state=START), UnityRandom(state=START)
    a.next_batch(k)
    for _ in range(k):
        b.next_uint()
    assert (a.get_state(), a.draws) == (b.get_state(), b.draws)


@pytest.mark.parametrize("chance", [0.0, 0.05, 0.25, 0.5, 0.9, 1.0])
def test_integer_threshold_matches_value(chance):
    a, b = UnityRandom(state=START), UnityRandom(state=START)
    t = threshold_for(chance)
    assert all(a.value_lt(t) == (b.value() < chance) for _ in range(2000))


FIXTURES = sorted(glob.glob(os.path.join(HERE, "fixtures", "sim_parity", "*.json.gz")))


@pytest.mark.parametrize("path", FIXTURES, ids=[os.path.basename(p)[:-8] for p in FIXTURES])
def test_game_start_matches_capture(path):
    with gzip.open(path, "rt") as f:
        fx = json.load(f)
    assert list(game_start(fx["seed"]).get_state()) == list(fx["seed_state"])
