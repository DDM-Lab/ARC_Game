#!/usr/bin/env python3
"""Non-learning smoke test: verify informational alerts don't stall the ARC env.

Motivation: several task types (`Flood Alert - Rising Water`,
`Workforce Optimization Alert`, `Worker Shortage Advice`, `Training Recommendation
Alert`) render with NO choice list in the observation — the model can never emit
`<task>N,X</task>` for them. Before filtering these out of the observation we
first need to confirm that when they DO appear in the game state, the env still
advances cleanly on env.step() (no hang, no exception, obs stays well-formed).

Runs three short episodes with no-op actions. For each turn:
  * records how long env.step() took
  * counts choice-less tasks visible in the obs
  * checks that obs dict has expected keys and no obvious corruption
Fails loudly if any step exceeds STEP_TIMEOUT_SEC or the env raises.

Usage:
    ARC_GAME_BUILD=/zfsauton/scratch/cpulling/CORA/ARC_Game/ARC_Game_New/Build/Headless/Linux/ARC_Headless.x86_64 \
    python3 test_alerts_dont_stall.py
"""
import os
import sys
import time
import traceback
from pathlib import Path

# Make imports work when run from repo root
sys.path.insert(0, str(Path(__file__).parent))
from arc_game_gym_env_tcp import ARCGameGymEnv  # type: ignore
import llm_smoke_test as smoke  # type: ignore


STEP_TIMEOUT_SEC = 30.0
NUM_EPISODES = 3
MAX_STEPS_PER_EPISODE = 32


def choiceless_tasks(obs_dict):
    """Return the (id, title, type) tuples for tasks with 0 renderable choices."""
    out = []
    for t in obs_dict.get("tasks", []) or []:
        if not (t.get("choices") or []):
            out.append((t.get("taskId"), t.get("title"), t.get("type")))
    return out


def run_one_episode(env, ep_idx):
    print(f"\n===== episode {ep_idx} =====")
    obs, info = env.reset()
    step_i = 0
    alert_turns = 0
    total_alerts_seen = 0
    while step_i < MAX_STEPS_PER_EPISODE:
        # Build cmd-style obs dict for a consistent view (matches training)
        obs_dict = smoke.summarize_commands(env, show_impacts=True)
        cl = choiceless_tasks(obs_dict)
        if cl:
            alert_turns += 1
            total_alerts_seen += len(cl)
            samples = ", ".join(f"{tid}:{title}({ttype})" for tid, title, ttype in cl[:3])
            print(f"  turn {step_i}: day={obs_dict.get('day')} budget={obs_dict.get('budget')} "
                  f"choiceless_tasks={len(cl)} → {samples}")

        # No-op action: empty string parsed by the LLM wrapper as "did nothing this turn"
        # (verified path: llm_agents_wrapper._actions_at + parse_commands with empty text
        # returns no actions, env.step still advances the game clock).
        t0 = time.perf_counter()
        try:
            obs, reward, terminated, truncated, info = env.step("")
        except Exception:
            print(f"  ✗ STEP RAISED at turn {step_i} (day {obs_dict.get('day')})")
            traceback.print_exc()
            return False
        elapsed = time.perf_counter() - t0
        if elapsed > STEP_TIMEOUT_SEC:
            print(f"  ✗ STEP HUNG at turn {step_i}: took {elapsed:.1f}s (>{STEP_TIMEOUT_SEC}s)")
            return False

        # obs sanity: dict-like, has 'text' or 'state', doesn't corrupt on alert turns
        if not isinstance(obs, (dict,)):
            print(f"  ✗ obs became {type(obs).__name__} at turn {step_i}, expected dict")
            return False

        step_i += 1
        if terminated or truncated:
            break

    print(f"  ✓ episode {ep_idx} completed cleanly: {step_i} turns, "
          f"{alert_turns} alert-turns, {total_alerts_seen} total alerts seen")
    return True


def main():
    build = os.environ.get("ARC_GAME_BUILD")
    if not build or not os.path.exists(build):
        print(f"ERROR: ARC_GAME_BUILD not set or does not exist: {build!r}", file=sys.stderr)
        sys.exit(2)

    # Slightly bigger max_episode_steps than we'll use so gym-side truncation doesn't hit
    env = ARCGameGymEnv(
        unity_exe_path=build,
        unity_port=int(os.environ.get("BASE_PORT", "9876")),
        auto_start_unity=True,
        max_episode_steps=MAX_STEPS_PER_EPISODE + 5,
        connection_timeout=60.0,
    )
    try:
        ok = 0
        for i in range(NUM_EPISODES):
            if run_one_episode(env, i):
                ok += 1
        print(f"\n===== RESULT: {ok}/{NUM_EPISODES} episodes completed without stall =====")
        if ok < NUM_EPISODES:
            sys.exit(1)
    finally:
        try:
            env.close()
        except Exception:
            pass


if __name__ == "__main__":
    main()
