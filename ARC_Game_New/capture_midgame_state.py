#!/usr/bin/env python3
"""Drive a few rounds with a greedy build/hire/staff policy to reach a realistic
mid-game state (some facilities built + staffed, partial fulfillment, budget
spent, communities in varying deficit), then dump the full game_state JSON for
the synthetic dashboard renderer."""
import json, os
from arc_game_gym_env_tcp import ARCGameGymEnv

EXE = "Build/HeadlessRender/macOS/ARC_HeadlessRender.app/Contents/MacOS/ARC_DisasterSimulation"


def pick(actions):
    """Greedy: build one facility, hire/train some workers, staff what exists."""
    chosen, used_types = [], set()
    # one construction (prefer Kitchen, else Shelter, else first)
    cons = [i for i, a in enumerate(actions) if a.get("action_type") == "construction"]
    def btype(i): return (actions[i].get("construction") or {}).get("building_type", "")
    for pref in ("Kitchen", "Shelter", "CaseworkSite"):
        m = [i for i in cons if btype(i) == pref]
        if m:
            chosen.append(m[0]); break
    else:
        if cons: chosen.append(cons[0])
    # a couple worker actions (hire/train)
    workers = [i for i, a in enumerate(actions) if a.get("action_type") == "worker"]
    chosen += workers[:2]
    # staff every assignment available
    chosen += [i for i, a in enumerate(actions) if a.get("action_type") == "worker_assignment"]
    return sorted(set(chosen))


def main():
    env = ARCGameGymEnv(unity_exe_path=EXE, unity_port=10934,
                        auto_start_unity=True, max_episode_steps=40)
    obs, info = env.reset()
    last = obs
    for rnd in range(10):
        acts = env.get_valid_actions()
        idxs = pick(acts)
        csv = ",".join(str(i) for i in idxs)
        obs, r, term, trunc, info = env.step(csv)
        last = obs
        sb = obs.get("satisfactionAndBudget", {})
        # resolve any pending task choice (pick first) to keep things moving
        tc = obs.get("taskContext", {})
        if tc.get("choices"):
            try: env.select_task_choice(tc.get("taskId", 0), tc["choices"][0].get("choiceId", 0))
            except Exception: pass
        print(f"round {rnd}: acted [{csv}]  sat={sb.get('satisfaction')} budget={sb.get('budget')}")
        if term or trunc:
            break
    env.close()
    out = os.path.join(os.getcwd(), "arc_midgame_state.json")
    json.dump(last, open(out, "w"), indent=2)
    print(f"\nDumped mid-game state -> {out}")
    # quick peek at what varies
    for f in last.get("mapState", {}).get("facilities", []):
        res = f.get("resources", {})
        print(f"  {f.get('buildingType'):10s} {f.get('facilityName'):22s} "
              f"food {res.get('foodPacks')}/{res.get('foodPacksCapacity')} "
              f"pop {res.get('population')}/{res.get('populationCapacity')} "
              f"staff {f.get('assignedWorkforce')}/{f.get('requiredWorkforce')}")


if __name__ == "__main__":
    main()
