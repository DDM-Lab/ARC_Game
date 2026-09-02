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

from struct import pack as _pk, unpack as _up

M32 = 0xFFFFFFFF

# PINNING THE raw -> Random.value MAPPING.
# Unity's Random.value is the LOW 23 bits over 2^23-1, i.e. [0,1] INCLUSIVE -- not
# raw/2^32, not the (raw >> 9) bit-stuff trick, both of which are the usual guesses and
# both of which are wrong here. Fitted against Unity ground truth: two rain-spawn rounds
# logged "Spawned flood at 84/108" and "96/108" at chances 0.82 and 0.90; this mapping
# reproduces 84 and 96 exactly, while raw/2^32 and (raw>>9)/2^23 give 83 and 91. The 0.90
# row is what makes it conclusive -- a 5-tile gap is not a rounding coincidence.
_MANT = 0x7FFFFF                  # 2^23 - 1
_DENOM = 8388607.0                # float(2^23 - 1)

_pack_f32 = lambda x: _pk("f", x)
_unpack_f32 = lambda b: _up("f", b)


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

    # ── value semantics (mapping pinned against Unity, see header note) ─────────────
    def value(self) -> float:
        """`Random.value`, in [0,1] INCLUSIVE.

        Debug/readability path. Hot loops should use value_lt(), which does the same
        comparison without building a float."""
        return f32((self.next_uint() & _MANT) / _DENOM)

    def value_lt(self, threshold_raw: int) -> bool:
        """`Random.value < chance`, as an integer compare. Hoist threshold_for() out of
        the loop; this keeps float construction off the per-draw path entirely."""
        return (self.next_uint() & _MANT) < threshold_raw

    def range_int(self, lo: int, hi_exclusive: int) -> int:
        """Random.Range(int, int) -- upper bound EXCLUSIVE, as in Unity.

        `lo + raw % n`, NOT a scaled Random.value. Pinned by test_flood's
        range_int_is_discriminated: the flood fixture's 22 expansion picks and 4 random
        expansions land on Unity's exact tiles under modulo, and put 5 of 9 rounds on the
        wrong tiles under either scaled variant."""
        n = hi_exclusive - lo
        if n <= 0:
            return lo
        return lo + (self.next_uint() % n)

    def range_float(self, lo: float, hi: float) -> float:
        """Random.Range(float, float) -- upper bound inclusive in Unity."""
        return lo + (hi - lo) * ((self.next_uint() & _MANT) / _DENOM)


def f32(x: float) -> float:
    """Round a Python float to float32, which is what every Unity-side constant and
    product actually is. Chances here differ from their float64 equivalents by ~1e-8,
    and adjacent Random.value outputs are 1.2e-7 apart, so ignoring this flips a
    comparison roughly 8% of the times a draw lands in that window -- rare enough to
    look like a heisenbug, common enough to desync a long episode."""
    return _unpack_f32(_pack_f32(x))[0]


def f32mul(*xs: float) -> float:
    """Left-to-right float32 product, mirroring how C# evaluates `a * b * c`."""
    acc = f32(xs[0])
    for x in xs[1:]:
        acc = f32(acc * f32(x))
    return acc


def f32add(*xs: float) -> float:
    """Left-to-right float32 sum, mirroring how C# evaluates `a + b + c`."""
    acc = f32(xs[0])
    for x in xs[1:]:
        acc = f32(acc + f32(x))
    return acc


def threshold_for(chance: float) -> int:
    """Integer threshold T such that `(next_uint() & _MANT) < T` == `value() < chance`.

    Compute ONCE per distinct chance per round and reuse inside the loop.

    T is found by bisection rather than arithmetic because the mapping rounds to float32:
    T is the smallest mantissa m whose value f32(m / (2^23-1)) is NOT below `chance`."""
    c = f32(chance)
    if c <= 0.0:
        return 0
    if c > 1.0:
        return _MANT + 1
    lo, hi = 0, _MANT + 1          # invariant: value(lo-1) < c <= value(hi)
    while lo < hi:
        mid = (lo + hi) // 2
        if f32(mid / _DENOM) < c:
            lo = mid + 1
        else:
            hi = mid
    return lo


def threshold_le_for(chance: float) -> int:
    """Integer threshold T such that `(next_uint() & _MANT) < T` == `value() <= chance`.

    Needed because Unity writes one of these tests as an early return on the STRICT
    GREATER side (`if (value > chance) return;`), whose complement is `<=`, not `<`.
    Bisecting for the boundary separately is the only way to keep that off-by-one
    honest -- threshold_for(chance) + 1 is right only when `chance` happens to be
    exactly representable as m / (2^23-1)."""
    c = f32(chance)
    if c < 0.0:
        return 0
    if c >= 1.0:
        return _MANT + 1
    lo, hi = 0, _MANT + 1          # invariant: value(lo-1) <= c < value(hi)
    while lo < hi:
        mid = (lo + hi) // 2
        if f32(mid / _DENOM) <= c:
            lo = mid + 1
        else:
            hi = mid
    return lo
