"""DRAW-FOR-DRAW mark diff: the instrument the counter tests cannot be.

WHY THIS EXISTS. Counter equality on a client-bearing trace is not evidence that the RNG
stream matches. staff_6001 was "exact at every round" while the port consumed 101 draws
where Unity consumed 202 -- the counters agreed by luck about where the two streams had
drifted to. Judging client work by counters therefore reads noise. This compares the
ORDERED SEQUENCE OF DRAW MARKS instead, which is the thing that actually has to match.

Unity's [RNGMARK] lines carry both the mark name and the step tag (s<N>d<D>r<S>f<F>), so
its per-step draw sequence is recoverable from the capture with no rebuild. The port emits
the same names into step_round's `marks` list. Diffing the two per step gives a first
divergence with a POSITION in the stream, not a counter that has already been laundered
through several mechanics.

This is also the missing 135th draw-census fixture: the census chains round exit states to
the next round's entry, and the one interval it never covered is the client-draw round.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.economy import (action_from_id, apply_action, from_game_state)  # noqa: E402
from cora_sim.floodmap import FloodMap                                        # noqa: E402
from cora_sim.rng import UnityRandom                                          # noqa: E402
import cora_sim.sim as S                                                      # noqa: E402
from cora_sim.test_replay_forward import seed_state, _DEFAULT                 # noqa: E402

_MARK = re.compile(r"\[RNGMARK\] s(\d+)d\d+r\d+f\d+ (draw:\S+)")
_SEED = re.compile(r"\[RNGCTX\] s(\d+)d\d+r\d+f\d+ flood:enter")


def seed_step(log_path):
    """The gym step the port is seeded INTO. Its draws are only partly reproducible.

    test_replay_forward seeds from the first flood:enter, which sits mid-step -- after that
    step's generation pass. So the port cannot emit the draws that preceded the seed point
    and the seeding step must be excluded from the diff rather than counted as a divergence.
    """
    for line in open(log_path, errors="ignore"):
        m = _SEED.search(line)
        if m:
            return int(m.group(1))
    return None


def unity_marks(log_path):
    """Ordered draw marks per gym step, straight off the capture."""
    per = {}
    for line in open(log_path, errors="ignore"):
        m = _MARK.search(line)
        if m:
            per.setdefault(int(m.group(1)), []).append(m.group(2))
    return per


def port_marks(trace_path, log_path):
    """Replay the trace exactly as test_replay_forward does, collecting marks per step."""
    trace = json.load(open(trace_path))
    st = seed_state(log_path)
    if st is None:
        return None
    w = S.World(rng=UnityRandom(state=st), weather="Sunny", fmap=FloodMap.load())
    w.use_generation = True
    w.economy = from_game_state(trace[0]["before"])
    w.economy.buildings = type(w.economy).default_prebuilts()
    w.tasks.has_supplier = lambda tag: True
    per = {}
    for step in trace:
        for act in step["taken"]:
            if act.get("error") or act.get("ok") is False:
                continue
            if act.get("kind") == "menu":
                apply_action(w.economy, act.get("payload")
                             or action_from_id(act.get("action_id"), act.get("cost") or 0))
            elif act.get("kind") == "staff":
                a = (act.get("payload") or {}).get("assignment") or {}
                idx = next((i for i, b in enumerate(w.economy.buildings)
                            if w.economy.can_staff(i)), -1)
                w.economy.staff(idx, count=int(a.get("quantity") or 0))
        seen = set()
        for tid, cid in S.open_choices(w):
            if tid in seen:
                continue
            seen.add(tid)
            S.answer(w, tid, cid)
        marks = []
        S.step_round(w, marks=marks)
        # step = trace round + 1, the mapping the replay harness already relies on.
        per[step["round"] + 1] = marks
    return per


def summarise(seq):
    """Collapse a run of identical marks so a 100-draw burst reads as one line."""
    out = []
    for m in seq:
        if out and out[-1][0] == m:
            out[-1][1] += 1
        else:
            out.append([m, 1])
    return out


def main():
    import glob
    paths = sorted(glob.glob(os.environ.get("STAFF_TRACES", _DEFAULT)))
    only = sys.argv[1] if len(sys.argv) > 1 else None
    if only:
        paths = [p for p in paths if only in p]
    print("cora_sim DRAW-FOR-DRAW mark diff (Unity capture vs port replay)")
    bad = 0
    for path in paths:
        log = path.replace(".json", ".log")
        if not os.path.exists(log):
            continue
        u = unity_marks(log)
        p = port_marks(path, log)
        if p is None:
            continue
        name = os.path.basename(path)
        s0 = seed_step(log)
        steps = sorted(k for k in (set(u) | set(p)) if s0 is None or k > s0)
        if not steps:
            print(f"  {name}: no comparable steps after the seed step")
            continue
        first = None
        for s in steps:
            us, ps = u.get(s, []), p.get(s, [])
            if us != ps:
                i = next((k for k in range(min(len(us), len(ps))) if us[k] != ps[k]),
                         min(len(us), len(ps)))
                first = (s, i, len(us), len(ps),
                         us[i] if i < len(us) else "<end>",
                         ps[i] if i < len(ps) else "<end>")
                break
        if first is None:
            print(f"  {name}: draw streams identical across steps {steps[0]}..{steps[-1]}")
        else:
            s, i, nu, np_, mu, mp = first
            bad += 1
            print(f"  {name}: step {s} diverges at draw {i} "
                  f"(unity {nu} draws, port {np_}); unity={mu} port={mp}")
            if os.environ.get("MARKS_VERBOSE"):
                print(f"      unity: {summarise(u[s])}")
                print(f"      port : {summarise(p[s])}")
    print("\nRESULT:", "ALL PASS" if not bad else f"{bad} trace(s) diverge")
    return 0 if not bad else 1


if __name__ == "__main__":
    raise SystemExit(main())
