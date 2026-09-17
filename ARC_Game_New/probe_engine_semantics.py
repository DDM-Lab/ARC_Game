"""Engine-semantics probes for the A2 execute-spine unification.

The A2 plan removes the auto/choices/coach path's LOCAL pre-validation (budget /
worker / construction-site skip in agent_router._execute_validated_actions) in favor of
execute-as-chosen: send every chosen action to Unity and let the engine judge. That is
only safe if Unity's own behavior matches what the local pre-check assumed. These two
probes establish the engine truth BEFORE any pre-validation is deleted:

  Probe 1 — OVER-BUDGET: the prompt says "Budget is finite and may go negative". Does the
            engine actually EXECUTE a spend that exceeds the current budget (budget goes
            negative), or does it REJECT it? If it executes, the local budget pre-skip was
            blocking spends the game allows — pure divergence, safe to remove.

  Probe 2 — SITE CONFLICT: two constructions targeting the SAME site in one planning phase.
            Does the engine cleanly REJECT the second (site occupied), so we can drop the
            local used_construction_sites skip and let the failure be policy signal?

Run against a live headless gym on :9876:
    ARC_HEADLESS_EXE="/path/to/ARC_Headless.app/Contents/MacOS/<exe>" \
        ./.venv/bin/python probe_engine_semantics.py
(omit ARC_HEADLESS_EXE to connect to an already-running gym on the port below).

This is READ-ONLY diagnostics — it never edits game code. Interpret the printed verdicts,
then wire the A2 pre-validation removal to match the engine's actual behavior.
"""
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from arc_game_gym_env_tcp import ARCGameGymEnv

PORT = int(os.environ.get("ARC_GYM_PORT", "9876"))
EXE = os.environ.get("ARC_HEADLESS_EXE")  # None -> connect to a running gym


def _budget(info, env):
    """Best-effort current budget from info or the freshest game_state."""
    if isinstance(info, dict) and info.get("budget") is not None:
        return float(info["budget"])
    gs = getattr(env, "game_state", {}) or {}
    for k in ("budget", "Budget", "currentBudget"):
        if gs.get(k) is not None:
            return float(gs[k])
    return None


def _build_tags_at_same_site(env):
    """Find two DISTINCT building types that can target the SAME site id, from valid_actions.
    Returns (site_id, tagA, tagB) or (None, None, None) if the action space doesn't offer it."""
    acts = env.get_valid_actions() if hasattr(env, "get_valid_actions") else (env.valid_actions or [])
    by_site = {}  # site_id -> set(types)
    for a in acts:
        if a.get("action_type") == "construction":
            c = a.get("construction") or {}
            sid, btype = c.get("site_id"), c.get("building_type") or c.get("type")
            if sid is not None and btype:
                by_site.setdefault(sid, set()).add(str(btype).lower())
    for sid, types in by_site.items():
        if len(types) >= 2:
            t = sorted(types)
            return sid, f"<build>{t[0]},{sid}</build>", f"<build>{t[1]},{sid}</build>"
    # Fall back: same type twice at one site (still a same-site conflict for probe 2).
    for sid, types in by_site.items():
        t = sorted(types)[0]
        return sid, f"<build>{t},{sid}</build>", f"<build>{t},{sid}</build>"
    return None, None, None


def probe_over_budget(env):
    print("\n─── PROBE 1 · over-budget spend ───")
    obs, info = env.reset()
    b0 = _budget(info, env)
    print(f"  initial budget: {b0}")
    if b0 is None:
        print("  VERDICT: INCONCLUSIVE — could not read budget from info/game_state")
        return
    # Hire far more untrained workers ($100 each) than the budget can cover, in ONE phase.
    n = int(abs(b0) / 100) + 100
    cmd = f"<hire>untrained,{n}</hire>"
    print(f"  attempting {cmd}  (~${n*100:,} vs budget ${b0:,.0f})")
    obs, reward, term, trunc, info = env.step(cmd)
    b1 = _budget(info, env)
    print(f"  budget after: {b1}")
    if b1 is not None and b1 < 0:
        print("  VERDICT: ENGINE EXECUTES INTO NEGATIVE  → local budget pre-skip blocks spends "
              "the game allows; pure divergence, SAFE to remove.")
    elif b1 is not None and 0 <= b1 < b0:
        print("  VERDICT: ENGINE ENFORCES BUDGET — spent down to affordability then rejected the "
              "rest (floored at ~0, not negative). The local budget pre-skip is REDUNDANT with "
              "engine enforcement → SAFE to remove; execute-as-chosen records the over-budget "
              "action as engine-truth 'rejected' (policy signal), not a locally-predicted 'skip'.")
    else:
        print("  VERDICT: INCONCLUSIVE — inspect whether the hire was bundle-capped vs budget-capped.")


def probe_site_conflict(env):
    """Budget-delta method: two builds at the SAME site in one phase. If only ~1× the build
    cost is spent, the engine rejected the second (site conflict enforced). Iterates candidate
    sites (resetting between) until one produces a successful build to measure."""
    print("\n─── PROBE 2 · duplicate-site construction (budget-delta method) ───")
    obs, info = env.reset()
    acts = env.get_valid_actions() if hasattr(env, "get_valid_actions") else (env.valid_actions or [])
    builds = {}  # site_id -> (type, cost)
    for a in acts:
        if a.get("action_type") == "construction":
            c = a.get("construction") or {}
            sid, btype, cost = c.get("site_id"), c.get("building_type") or c.get("type"), a.get("cost", 0)
            if sid is not None and btype and cost:
                builds.setdefault(sid, (str(btype).lower(), cost))
    if not builds:
        print("  VERDICT: INCONCLUSIVE — no priced construction actions offered")
        return
    for sid, (btype, cost) in builds.items():
        obs, info = env.reset()
        b0 = _budget(info, env)
        cmd = f"<build>{btype},{sid}</build> <build>{btype},{sid}</build>"
        obs, reward, term, trunc, info = env.step(cmd)
        b1 = _budget(info, env)
        if b0 is None or b1 is None:
            continue
        spent = b0 - b1
        landed = round(spent / cost) if cost else 0
        print(f"  site {sid}: two <build>{btype}> @ ${cost} each; budget ${b0:.0f}->${b1:.0f} "
              f"(spent ${spent:.0f} ≈ {landed} build(s))")
        if landed == 1:
            print("  VERDICT: ENGINE REJECTS the second same-site build → SAFE to drop the local "
                  "used_construction_sites skip; the rejection is engine-truth policy signal.")
            return
        if landed >= 2:
            print("  VERDICT: ENGINE ACCEPTS BOTH → the local site-conflict skip enforces a rule "
                  "the engine does NOT; decide whether it belongs in the engine.")
            return
        # landed == 0 → this site wasn't buildable; try the next candidate
    print("  VERDICT: INCONCLUSIVE — no candidate site produced a successful build to test against")


def main():
    env = ARCGameGymEnv(unity_exe_path=EXE, unity_port=PORT,
                        auto_start_unity=bool(EXE), max_episode_steps=8)
    try:
        probe_over_budget(env)
        probe_site_conflict(env)
    finally:
        env.close()
    print("\nDone. Feed both verdicts into the A2 pre-validation removal.")


if __name__ == "__main__":
    main()
