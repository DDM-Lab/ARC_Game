"""Headless multi-agent test harness.

REAL scoped continuous officers (Haiku over the CMU gateway), REAL scoping +
concurrency + commit-lock, driving the REAL headless Unity game engine — with an
auto/stub director. This is the cheap shake-out pass for real-LLM / gateway /
scoping / concurrency issues BEFORE the live interactive Unity pass.

Why a bridge? The headless Unity build speaks the GYM's TCP protocol
(arc_game_gym_env_tcp.ARCGameGymEnv ↔ GymServerManager), NOT the router's
WebSocket. So we run a websocket-less router `Session` for the officers and route
every `execute_action` frame it emits into the gym env's TCP execute. Both the gym
and the router already speak the identical `{"type":"execute_action","action":...}`
Unity frame — the only reconciliation is: the gym encodes `action` as a JSON string
and reads new state from a separate `get_game_state`, whereas the router expects the
action_result to CARRY `game_state`. The bridge does exactly that reconciliation.

Director: the config's manual "Player" is flipped to actor_type="auto" at runtime.
get_agent_order() excludes directors from the per-round order, so an auto director
never takes a proactive turn — it acts ONLY reactively, resolving any officer
`propose_choices` inline (LLM pick → execute via the same bridged frames). Between
rounds THIS driver is the stub director: it advances the round via `advance_time`
to roll the world's dynamics, exactly as ending a human director's turn would.

Run (clear proxies, venv python; network → unsandboxed):
  env -u ALL_PROXY -u all_proxy -u HTTPS_PROXY -u https_proxy -u HTTP_PROXY \
      -u http_proxy ./.venv/bin/python headless_multiagent_harness.py --rounds 2
"""
import argparse
import asyncio
import json
import os
import sys
import time

CONFIG = "config/continuous_agents_domain.json"
EXE = "Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/ARC_DisasterSimulation"


def load_env_file(path=".env"):
    """Minimal .env loader (no python-dotenv dependency). The router reads the LLM
    key from os.environ[api_key_env]; run_router.sh sources .env in prod."""
    if not os.path.exists(path):
        return
    with open(path) as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            os.environ.setdefault(k.strip(), v.strip().strip('"').strip("'"))


load_env_file()

import agent_router  # noqa: E402
from agent_router import Session  # noqa: E402
from agent_config import load_config  # noqa: E402
from agent_filters import _action_matches_entry  # noqa: E402
from arc_game_gym_env_tcp import ARCGameGymEnv  # noqa: E402


def _sat_budget(gs):
    sb = gs.get("satisfactionAndBudget", {}) or {}
    return sb.get("satisfaction"), sb.get("budget")


async def run(rounds, model, exe, unity_port):
    # ── Config: scoped officers as authored; flip the manual director → auto so it
    #    resolves any propose_choices without a human, and never hangs on _pending_choice.
    cfg = load_config(CONFIG)
    director = next(a for a in cfg.agents if a.role == "director")
    director.actor_type = "auto"
    director.llm_provider = "openai"
    director.llm_model = model
    director.llm_endpoint = "https://ai-gateway.andrew.cmu.edu/v1"
    director.api_key_env = "OPENAI_API_KEY"
    if director.num_choices is None:
        director.num_choices = 3

    officers = {a.subagent_name: a for a in cfg.agents if a.role == "subagent"}
    print(f"[harness] officers: {list(officers)}")
    if not os.environ.get(officers[next(iter(officers))].api_key_env or "OPENAI_API_KEY"):
        print("[harness] ⚠️  no gateway API key in env — officers will fail to auth.")

    # ── Boot the REAL headless Unity game engine over gym-TCP.
    print("[harness] launching headless Unity …")
    env = ARCGameGymEnv(unity_exe_path=exe, unity_port=unity_port,
                        auto_start_unity=True, manual_transfers=True)
    obs, info = env.reset()
    print(f"[harness] Unity up. day={info.get('day')} sat={info.get('satisfaction')} "
          f"budget={info.get('budget')} actions={info.get('valid_action_count')}")

    os.makedirs("logs/sessions", exist_ok=True)
    sess = Session(cfg, "sess-headless", "test",
                   "logs/sessions/headless_multiagent.jsonl", websocket=None)

    gym_lock = asyncio.Lock()          # single TCP socket ⇒ serialize all gym I/O
    executed = {}                       # agent_name -> [action dicts] (for scope audit)
    frames = {"execute_action": 0, "director_turn": 0, "other": 0}

    async def gym_call(req):
        async with gym_lock:
            return await asyncio.to_thread(env._send_request, req)

    # ── The bridge: route the Session's Unity frames into the gym env.
    async def bridge_send(payload):
        t = payload.get("type")
        if t == "execute_action":
            frames["execute_action"] += 1
            action = payload["action"]
            agent_name = payload.get("agent_name", "?")
            executed.setdefault(agent_name, []).append(action)
            try:
                # gym encodes `action` as a JSON string (proven headless path).
                resp = await gym_call({"type": "execute_action",
                                       "action": json.dumps(action)})
                # execute_action carries no state on this path — refresh explicitly so
                # the router's _publish_state keeps concurrent officers monotone-fresh.
                st = await gym_call({"type": "get_game_state"})
                gs = (json.loads(st.get("game_state", "{}"))
                      if st.get("type") == "game_state" else {})
            except Exception as e:  # noqa: BLE001 — surface, don't crash the round
                resp, gs = {"success": False, "error": str(e)}, {}
                print(f"[bridge] execute error for {agent_name}: {e}")
            ok = bool(resp.get("success"))
            print(f"[bridge] {agent_name} → {action.get('action_id')} "
                  f"[{'ok' if ok else 'FAIL: ' + str(resp.get('error'))}]")
            await sess._handle_action_result({
                "success": ok,
                "action_id": action.get("action_id"),
                "error_message": resp.get("error"),
                "game_state": gs,
            })
        elif t == "select_task_choice":
            frames.setdefault("select_task_choice", 0)
            frames["select_task_choice"] += 1
            tid, cid = payload.get("taskId"), payload.get("choiceId")
            try:
                resp = await gym_call({"type": "select_task_choice",
                                       "taskId": tid, "choiceId": cid})
                st = await gym_call({"type": "get_game_state"})
                gs = (json.loads(st.get("game_state", "{}"))
                      if st.get("type") == "game_state" else {})
            except Exception as e:  # noqa: BLE001 — surface, don't crash the round
                resp, gs = {"success": False, "error": str(e)}, {}
                print(f"[bridge] choice error t{tid} c{cid}: {e}")
            ok = bool(resp.get("success"))
            print(f"[bridge] select_task_choice t{tid} c{cid} "
                  f"[{'ok' if ok else 'FAIL: ' + str(resp.get('error'))}]")
            await sess._handle_action_result({
                "success": ok,
                "action_id": f"choice_{tid}_{cid}",
                "error_message": resp.get("error"),
                "game_state": gs,
            })
        elif t == "director_turn":
            frames["director_turn"] += 1
        else:
            frames["other"] += 1
            print(f"[bridge] frame: {t}")

    sess._send = bridge_send
    # The gym-TCP bridge CAN execute task-choice answers (GymServerManager has
    # HandleSelectTaskChoice); tell the Session so execute_commands sends them.
    sess._task_choice_supported = True

    # ── Rounds. Officers run concurrently (real LLMs); driver-as-director advances.
    t0 = time.time()
    for r in range(rounds):
        gs = env.game_state
        sess_info = gs.get("sessionInfo", {})
        sat, bud = _sat_budget(gs)
        print(f"\n[harness] ===== ROUND {r} | day {sess_info.get('currentDay')} "
              f"seg {sess_info.get('currentTimeSegment')} | sat={sat} budget={bud} =====")
        await sess._handle_begin_round({
            "type": "begin_round",
            "day": sess_info.get("currentDay", 1),
            "segment": sess_info.get("currentTimeSegment", 0),
            "game_state": gs,
        })
        # Stub director ends its turn → advance the world's dynamics.
        adv = await gym_call({"type": "advance_time"})
        if adv.get("type") == "game_over":
            print("[harness] game over — stopping.")
            break
        env.game_state = json.loads(adv.get("game_state", "{}"))
        env._enumerate_valid_actions()

    # ── Scope audit: every officer must have executed ONLY in-scope actions.
    print("\n[harness] ===== SCOPE AUDIT =====")
    violations = []
    for name, acts in executed.items():
        if name not in officers:
            print(f"  {name}: {len(acts)} exec (director/other — not scope-checked)")
            continue
        space = officers[name].subaction_space
        bad = [a for a in acts
               if not any(_action_matches_entry(a, e) for e in space)]
        ids = [a.get("action_id") for a in acts]
        print(f"  {name}: {len(acts)} exec {ids}")
        for a in bad:
            violations.append((name, a.get("action_id"), a.get("action_type")))
    for name in officers:
        executed.setdefault(name, [])

    print(f"\n[harness] frames: {frames}")
    print(f"[harness] elapsed {time.time() - t0:.1f}s")
    final_sat, final_bud = _sat_budget(env.game_state)
    print(f"[harness] final: sat={final_sat} budget={final_bud}")

    try:
        env.close()
    except Exception:
        pass

    if violations:
        print(f"\n[harness] ❌ SCOPE VIOLATIONS: {violations}")
        return 1
    print("\n[harness] ✓ no scope violations; all officers stayed in their lane.")
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rounds", type=int, default=2)
    ap.add_argument("--model", default="claude-haiku-4-5-20251001-v1:0")
    ap.add_argument("--exe", default=EXE)
    ap.add_argument("--unity-port", type=int, default=9876,
                    help="gym-TCP port for the headless Unity (use a free port if a "
                         "router is already on 9876)")
    args = ap.parse_args()
    rc = asyncio.run(run(args.rounds, args.model, args.exe, args.unity_port))
    sys.exit(rc)


if __name__ == "__main__":
    main()
