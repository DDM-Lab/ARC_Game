"""Fresh game, seed 5801: build+staff one kitchen and one shelter, move ONE group of 100 people to the shelter,
answer the motel's first food request with the 'double' choice, and watch what the follow-up
request does (email: 'if both are fulfilled at once, the follow-up task will not spawn')."""
import json, os, sys
sys.path.insert(0, '/Users/cpulling/Work/CORA/ARC_Game/ARC_Game_New')
os.chdir('/Users/cpulling/Work/CORA/ARC_Game/ARC_Game_New')
from cora_sim.searchable_env import SearchableEnv
from cora_sim.validate_plan import EXE
SP = 'cora_sim/runs/probes'  # unity logs land here (untracked)
os.makedirs(SP, exist_ok=True)
port, rounds = int(sys.argv[1]), int(sys.argv[2])
os.environ["ARC_SNAPSHOT_DEBUG"] = "1"
env = SearchableEnv(unity_exe_path=EXE, unity_port=port, seed=5801, auto_start_unity=True,
                    connection_timeout=120, unity_log_path=f'{SP}/probe_covered_shelter.log')
env.reset()
built = staffed = relocated = False
for i in range(rounds):
    gs = env._game_state_dict()
    sent = []
    acts = {a.get("action_id"): a for a in (env.get_valid_actions() or [])}
    if not built:
        used = set()
        for prefix in ("build_Kitchen", "build_Shelter"):
            b = next((a for aid, a in acts.items() if aid.startswith(prefix) and aid.rsplit("_", 1)[-1] not in used), None)
            if b:
                r = env.execute(json.dumps(b)); used.add(b["action_id"].rsplit("_", 1)[-1])
                sent.append((b["action_id"], r.get("success"), str(r.get("error"))[:60]))
        h = acts.get("hire_untrained_5")   # pool is 5 untrained; kitchen + shelter need 4 each
        if h:
            r = env.execute(json.dumps(h)); sent.append(("hire_untrained_5", r.get("success"), str(r.get("error"))[:60]))
        built = True
    if built:
        facs = (gs.get("mapState") or {}).get("facilities") or []
        # kitchen first: the worker pool is small and the shelter would otherwise take it all
        for f in sorted(facs, key=lambda f: 0 if f.get("buildingType") == "Kitchen" else 1):
            if f.get("buildingType") in ("Kitchen", "Shelter") and f.get("buildingStatus") == "NeedWorker":
                r = env.execute(json.dumps({"action_type": "worker_assignment", "action_id": "staff_k", "cost": 0,
                                            "assignment": {"building_name": f.get("facilityName"), "quantity": 4}}))
                staffed = bool(r and r.get("success", True)); sent.append(("staff", f.get("facilityName"), staffed, str(r.get("error"))[:80]))
    for t in (gs.get("allActiveTasks") or []):
        choices = t.get("choices") or []
        if not choices: continue
        title = str(t.get("taskTitle")); want = None
        sid = str(t.get("stableTaskId"))
        if "Relocation Request" in title and not relocated:
            m = [c for c in choices if "shelter" in str(c.get("choiceText")).lower()]
            if m: want = m[0]["choiceId"]; relocated = True
        elif sid == "Shelter_FoodRequest_Second":
            want = choices[0]["choiceId"]
        elif sid == "Shelter_FoodRequest_First":
            d = [c for c in choices if "double" in str(c.get("choiceText")).lower()]
            want = d[0]["choiceId"] if d else None
        elif "Daily Budget" in title or "Funding" in title:
            want = choices[0]["choiceId"]
        if want is None: continue
        r = env.choose(t.get("taskId"), want)
        sent.append((title[:30], want, [c.get("choiceText")[:50] + f" q={c.get('deliveryQuantity')}" for c in choices if c["choiceId"] == want][0], r.get("success"), str(r.get("error"))[:90]))
    env.advance_round()
    si = env._game_state_dict().get("sessionInfo") or {}
    print(f"r{i:02d} -> day {si.get('currentDay')} round {si.get('currentRound')} {sent}", flush=True)
env.close(); print("done")
