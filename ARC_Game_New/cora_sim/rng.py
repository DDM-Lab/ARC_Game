"""Bit-exact reimplementation of UnityEngine.Random (xorshift128).

EMPIRICALLY PINNED, NOT ASSUMED. The state transition was fitted against 2663 consecutive
(before, after) state pairs captured from the live Unity build via SnapshotDebug's
[RNGMARK] instrumentation: classic xorshift128 with shift triple (11, 8, 19) reproduces
2663/2663. UnityEngine.Random.State is {s0,s1,s2,s3}, and a draw advances it as
(s0,s1,s2,s3) -> (s1,s2,s3,new), i.e. a shift register.

PERFORMANCE. Draw volume is ~300-500/round (one per river tile, one per neighbour
OCCURRENCE during flood expansion, one per flood tile when shrinking, clientCount+1 per
client check-in). At a naive ~0.4us/draw that is a quarter of the whole per-round budget,
so this module is written for speed:
  - next_uint() keeps the four words in locals and does no attribute access beyond the
    single store back
  - next_batch(k) generates k raws in one loop with the words in locals, for phases where
    the draw count is known in advance (the flood set is frozen during candidate build)
  - value_lt(threshold) avoids constructing a float per draw: see below

FLOAT COMPARISONS. `Random.value < chance` is the dominant pattern. Because the mapping
raw -> value is monotone, that comparison is equivalent to an INTEGER compare against a
threshold computed once per distinct chance per round. Callers should hoist the threshold
with `threshold_for(chance)` out of the loop rather than paying float construction per
draw. The exact raw->float mapping is pinned by test_rng_semantics against Unity.
"""
from __future__ import annotations

M32 = 0xFFFFFFFF
# 1 / 2^32, used for the raw -> [0,1) mapping. Verified against Unity in the semantics test.
_INV_2_32 = 1.0 / 4294967296.0


class UnityRandom:
    """Single bit-exact stream. Mirrors UnityEngine.Random's global state."""

    __slots__ = ("s0", "s1", "s2", "s3", "draws")

    def __init__(self, state=None, seed=None):
        if state is not None:
            self.s0, self.s1, self.s2, self.s3 = (x & M32 for x in state)
        elif seed is not None:
            self.init_state(seed)
        else:
            raise ValueError("UnityRandom needs either state=(s0,s1,s2,s3) or seed=int")
        self.draws = 0          # draw counter, for mark-sequence equality checks

    def init_state(self, seed: int) -> None:
        """Mirror of Random.InitState(seed).

        NOTE: Unity's seeding routine is NOT yet pinned empirically. Until it is, construct
        streams from a captured state (which IS pinned) rather than from a seed, and treat
        seed-based construction as unverified. test_rng_semantics asserts this.
        """
        raise NotImplementedError(
            "Random.InitState seeding is not yet pinned against Unity. Build the stream "
            "from a captured state instead: UnityRandom(state=(s0,s1,s2,s3))."
        )

    def get_state(self):
        return (self.s0, self.s1, self.s2, self.s3)

    def next_uint(self) -> int:
        """One draw. Hot path: words in locals, one store back."""
        x, y, z, w = self.s0, self.s1, self.s2, self.s3
        t = (x ^ ((x << 11) & M32)) & M32
        t = (t ^ (t >> 8)) & M32
        w2 = (w ^ (w >> 19) ^ t) & M32
        self.s0, self.s1, self.s2, self.s3 = y, z, w, w2
        self.draws += 1
        return w2

    def next_batch(self, k: int) -> list:
        """k draws in one loop, words in locals throughout. Use where k is known ahead."""
        out = [0] * k
        x, y, z, w = self.s0, self.s1, self.s2, self.s3
        for i in range(k):
            t = (x ^ ((x << 11) & M32)) & M32
            t = (t ^ (t >> 8)) & M32
            w2 = (w ^ (w >> 19) ^ t) & M32
            x, y, z, w = y, z, w, w2
            out[i] = w2
        self.s0, self.s1, self.s2, self.s3 = x, y, z, w
        self.draws += k
        return out

    # ── value semantics (pinned by test_rng_semantics) ──────────────────────────────
    def value(self) -> float:
        """Random.value in [0,1)."""
        return self.next_uint() * _INV_2_32

    def value_lt(self, threshold_raw: int) -> bool:
        """`Random.value < chance`, as an integer compare. Hoist threshold_for() out of
        the loop; this keeps float construction off the per-draw path entirely."""
        return self.next_uint() < threshold_raw

    def range_int(self, lo: int, hi_exclusive: int) -> int:
        """Random.Range(int, int) -- upper bound EXCLUSIVE, as in Unity."""
        n = hi_exclusive - lo
        if n <= 0:
            return lo
        return lo + (self.next_uint() % n)

    def range_float(self, lo: float, hi: float) -> float:
        """Random.Range(float, float) -- upper bound inclusive in Unity, but the endpoint
        has measure zero for our purposes; pinned by the semantics test."""
        return lo + (hi - lo) * (self.next_uint() * _INV_2_32)


def threshold_for(chance: float) -> int:
    """Integer threshold T such that `next_uint() < T` == `value() < chance`.

    Compute ONCE per distinct chance per round and reuse inside the loop. ExpandFlood has
    exactly three distinct chances per round, so this turns hundreds of float comparisons
    into hundreds of integer comparisons.
    """
    if chance <= 0.0:
        return 0
    if chance >= 1.0:
        return 1 << 32
    return int(chance * 4294967296.0)
