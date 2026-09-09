#!/usr/bin/env python3
"""Acceptance test for the native Unity snapshot: TRAJECTORY equivalence.

WHY NOT A ROUND-TRIP CHECK
Comparing get_game_state before and after a load is necessary but NOT sufficient: the
observation payload is lossy, so a field that exists only inside a system (a construction
counter, a task cooldown, a per-resident timer) can be missing from the snapshot and the
round-trip still passes. The game then diverges several rounds later, which is the hardest
failure mode to notice. This session already produced four tests that passed while testing
nothing; this harness exists so the remaining capture work cannot repeat that.

THE TEST
Replay-restore (cora_sim/searchable_env.py) is the ORACLE -- it is exact by construction and already
verified (reload equivalence, branch isolation, JSON round-trip). So:

    play to round r
    take BOTH a replay token and a native snapshot at the same instant
    from the replay token   : play k rounds -> T_oracle
    from the native snapshot: play k rounds -> T_snapshot
    require T_snapshot == T_oracle, round by round

The per-round fingerprint covers what a divergence would actually show up in: tasks
offered, weather/flood, budget, satisfaction and every reward component. First mismatching
round is reported, because WHERE it diverges points at WHICH system is uncaptured.

USAGE
  python -m cora_sim.snapshot_equivalence [--rounds-before 6] [--rounds-after 6] [--seed 1234]
"""
import argparse, json, os, sys

from cora_sim.searchable_env import SearchableEnv

DEFAULT_EXE = ("Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/"
               "Collaborative Operations And Resource Management with Agentic AI")


def fingerprint(env):
    gs = env._game_state_dict()
    return {
        "day": (gs.get("sessionInfo") or {}).get("currentDay"),
        "round": (gs.get("sessionInfo") or {}).get("currentRound"),
        "budget": (gs.get("satisfactionAndBudget") or {}).get("budget"),
        "satisfaction": (gs.get("satisfactionAndBudget") or {}).get("satisfaction"),
        "tasks": sorted((t.get("taskTitle"), t.get("taskType"), t.get("affectedFacility"))
                        for t in (gs.get("allActiveTasks") or [])),
        "env": gs.get("environmentalConditions"),
        "facilities": sorted((f.get("originalSiteId"), f.get("buildingStatus"),
                              f.get("assignedWorkforce"), f.get("currentPopulation"))
                             for f in ((gs.get("mapState") or {}).get("facilities") or [])),
        "rewardMetrics": gs.get("rewardMetrics"),
    }


def play(env, k):
    out = []
    for _ in range(k):
        env.advance_round()
        out.append(fingerprint(env))
    return out


def first_divergence(a, b):
    for i, (x, y) in enumerate(zip(a, b)):
        if x != y:
            diffs = [key for key in set(x) | set(y) if x.get(key) != y.get(key)]
            return i, diffs
    return (None, []) if len(a) == len(b) else (min(len(a), len(b)), ["<length>"])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default=os.environ.get("ARC_HEADLESS_EXE", DEFAULT_EXE))
    ap.add_argument("--port", type=int, default=9955)
    ap.add_argument("--seed", type=int, default=1234)
    ap.add_argument("--rounds-before", type=int, default=6)
    ap.add_argument("--rounds-after", type=int, default=6)
    ap.add_argument("--build", action="store_true",
                    help="construct a couple of facilities first, so buildings, workers "
                         "and construction counters are actually part of the state")
    a = ap.parse_args()

    env = SearchableEnv(unity_exe_path=a.exe, unity_port=a.port, seed=a.seed,
                        auto_start_unity=True, connection_timeout=90)
    env.reset()
    if a.build:
        cons = [x for x in env.get_valid_actions() if x.get("action_type") == "construction"]
        seen, chosen = set(), []
        for x in cons:
            site = x.get("description", "").split("(")[-1]
            if site not in seen:
                seen.add(site); chosen.append(x)
            if len(chosen) == 2:
                break
        for x in chosen:
            env.execute(json.dumps(x))
    for _ in range(a.rounds_before):
        env.advance_round()

    token = env.save_state()                                   # oracle handle
    snap = env._send_request({"type": "save_state"}).get("state")
    if not snap:
        print("save_state returned nothing"); env.close(); return 1

    env.load_state(token)
    t_oracle = play(env, a.rounds_after)

    env.load_state(token)                                      # clean base for the snapshot
    resp = env._send_request({"type": "load_state", "state": snap})
    if resp.get("type") != "state_loaded":
        print("load_state failed:", resp); env.close(); return 1
    t_snapshot = play(env, a.rounds_after)

    idx, diffs = first_divergence(t_oracle, t_snapshot)
    print(f"\nseed={a.seed}  {a.rounds_before} rounds before, {a.rounds_after} after"
          f"{'  (+2 buildings)' if a.build else ''}")
    print(f"snapshot: {len(snap)} bytes\n")
    if idx is None:
        print(f"  TRAJECTORY EQUIVALENCE: PASS  ({a.rounds_after}/{a.rounds_after} rounds identical)")
        rc = 0
    else:
        print(f"  TRAJECTORY EQUIVALENCE: FAIL")
        print(f"  first divergence at round {idx} of {a.rounds_after}")
        print(f"  differing sections: {sorted(diffs)}")
        for key in sorted(diffs):
            o, sn = t_oracle[idx].get(key), t_snapshot[idx].get(key)
            # For list-valued sections show the SET DIFFERENCE, not a truncated prefix --
            # the interesting part is nearly always past the first few identical entries.
            if isinstance(o, list) and isinstance(sn, list):
                so, ss = set(map(tuple, o)), set(map(tuple, sn))
                print(f"\n  [{key}]  oracle={len(o)} entries, snapshot={len(sn)} entries")
                for x in sorted(so - ss): print(f"    only in ORACLE  : {x}")
                for x in sorted(ss - so): print(f"    only in SNAPSHOT: {x}")
                if so == ss:
                    print("    same set, different multiplicity/order:")
                    from collections import Counter
                    co, cs = Counter(map(tuple, o)), Counter(map(tuple, sn))
                    for k in sorted(set(co) | set(cs)):
                        if co[k] != cs[k]: print(f"      {k}: oracle x{co[k]}  snapshot x{cs[k]}")
            else:
                print(f"\n  [{key}]\n    oracle   : {str(o)[:300]}\n    snapshot : {str(sn)[:300]}")
        rc = 1
    env.close()
    return rc


if __name__ == "__main__":
    sys.exit(main())
