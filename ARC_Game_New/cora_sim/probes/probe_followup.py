"""Drive seed 5801 on the merged build with the recorded actions, but answer every shelter/motel
FIRST food request with the 'double' choice and every follow-up with choice 0, to see what the
follow-up does when the first request already covered it. Community requests: choice 0."""
import json, os, sys
sys.path.insert(0, '/Users/cpulling/Work/CORA/ARC_Game/ARC_Game_New')
os.chdir('/Users/cpulling/Work/CORA/ARC_Game/ARC_Game_New')
from cora_sim.searchable_env import SearchableEnv
from cora_sim.validate_plan import EXE
SP = 'cora_sim/runs/probes'  # unity logs land here (untracked)
os.makedirs(SP, exist_ok=True)
seed, port, rounds = 5801, int(sys.argv[1]), int(sys.argv[2])
replay = json.load(open('cora_sim/runs/validate/staff_5801.json'))
os.environ["ARC_SNAPSHOT_DEBUG"] = "1"
env = SearchableEnv(unity_exe_path=EXE, unity_port=port, seed=seed, auto_start_unity=True,
                    connection_timeout=120, unity_log_path=f"{SP}/probe_followup2.log")
env.reset()
for i in range(rounds):
    before = env._game_state_dict()
    rec = replay[i]["taken"] if i < len(replay) else []
    by_type = {}
    for x in rec:
        if x.get("kind") == "choice" and "choiceId" in x:
            by_type.setdefault(x.get("stableTaskId") or "", []).append(x["choiceId"])
    sent = []
    for t in (before.get("allActiveTasks") or []):
        choices = t.get("choices") or []
        cids = [c.get("choiceId") for c in choices]
        if not cids: continue
        title = str(t.get("taskTitle"))
        sid = t.get("stableTaskId") or ""
        if "Follow-up" in title:
            want = cids[0]
        elif "Food Request" in title and ("Motel" in title or "Shelter" in title):
            dbl = [c["choiceId"] for c in choices if "double" in str(c.get("choiceText")).lower()]
            want = dbl[0] if dbl else cids[0]
        elif "Food Request" in title:
            want = cids[0]
        else:
            q = by_type.get(sid); want = q.pop(0) if q else cids[0]
            want = want if want in cids else cids[0]
        try:
            r = env.choose(t.get("taskId"), want); sent.append((title[:34], want, str(r)[:160]))
        except Exception as e:
            sent.append((title[:34], f"ERR {e}"))
    for x in rec:
        if x.get("kind") in ("staff", "menu") and x.get("payload"):
            try: env.execute(json.dumps(x["payload"]))
            except Exception as e: sent.append(("menu", f"ERR {e}"))
    env.advance_round()
    after = env._game_state_dict()
    si = after.get("sessionInfo") or {}
    print(f"r{i:02d} -> day {si.get('currentDay')} round {si.get('currentRound')} sent={sent}", flush=True)
env.close()
print("done")
