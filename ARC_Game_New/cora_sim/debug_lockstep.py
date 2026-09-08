"""Interactive lockstep debugger: the port against a headless Unity run of the SAME plan,
step by step, with the known open bugs and where each seed first diverges.

    python -m cora_sim.debug_lockstep                       # summary of every validated seed + bug list
    python -m cora_sim.debug_lockstep 5503                  # interactive session on one seed
    python -m cora_sim.debug_lockstep 5503 --cmd "first;b;f;g;d"   # scripted (no TTY needed)

Inputs (per seed N): runs/validate/staff_N.json (the gym trace: before/taken/after per
round) and runs/validate/staff_N.log (Unity's ARC_SNAPSHOT_DEBUG=1 log with [RNGMARK]/
[RNGCTX] draw marks); the plan comes from runs/evo14.jsonl (best row for that seed).
The port is driven with the trace's `taken` actions exactly as Unity was (diag_lockstep.
drive_step), so any difference is a mechanic, not a policy.

Commands inside a session:
    s <n>   go to step n and show its summary        n / p    next / previous step
    first   jump to the first step with any diff     bugs     the open-bug list
    b       task board, port vs Unity (before answering that step)
    f       fleet: port vehicle events vs Unity delivery marks for the step
    g       client groups: port tracker vs Unity's, reconstructed from its log lines
    d       draw stream for the step, both sides, first mismatch
    c       every counter, both sides, for the step (after the round)
    q       quit
"""
import argparse, json, os, random, re, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cora_sim.sim as S                                    # noqa: E402
import cora_sim.diag_marks as D                             # noqa: E402
from cora_sim.actions import CoraActions                    # noqa: E402
from cora_sim.evolve import fresh_world                     # noqa: E402
from cora_sim.floodmap import FloodMap                      # noqa: E402
from cora_sim.diag_lockstep import best_row, drive_step, _KEYS   # noqa: E402
from cora_sim.diag_casework import unity_events             # noqa: E402
from cora_sim.diag_board import board                       # noqa: E402
from cora_sim.test_replay_forward import seed_state         # noqa: E402

BUGS = [
    ("OPEN", "6001", "step 21: two operational kitchens. Unity's kitchen-order choices (0/1) are multi-delivery "
     "(ExecuteMultipleDeliveries -> MultiSourceSingleDest -> FindMultipleSources: kitchens with stock, "
     "FindObjectsOfType order, per-kitchen route check). The port routes every order from the FIRST InUse "
     "kitchen (sim.answer, food path: `_kitchen = next(...)`), so two orders whose route from that kitchen is "
     "flood-cut are rejected while Unity fills them from the other kitchen. Port: sim.py answer(); Unity: "
     "TaskDetailUI.ExecuteMultiSourceSingleDest / FindMultipleSources, DeliverySystem.CreateDeliveryTask."),
    ("OPEN", "7002", "round 22: port foodResolved/foodFulfilled one higher than Unity (16/15 vs 15/14). Step 21-22 "
     "fleet now matches Unity (the epilogue load is deferred past production), so the extra credit is a "
     "resolution rule, not a landing: candidates are the abort/retry path after a failed kitchen load "
     "(Unity orphans the delivery -- Vehicle.LoadCargo sets currentTask=null -- and the parent expires "
     "Incomplete; the port re-dispatches at the next pass) and late-delivery crediting. Port: tasks.py "
     "tick_deliveries_only / roads.run_round abort; Unity: Vehicle.LoadCargo, TaskSystem.OnDeliveryTaskCompleted."),
    ("OPEN", "5503", "round 15: lodgingFulfilled 600 vs 500. The port's vehicle carrying the Community03->"
     "Shelter_0 relocation (task 43) hits a flood tile at (4, 0) on step 15 (`f`: (515, 'collision', 1, 43, "
     "(4, 0))) and the delivery is dropped; Unity's vehicle delivers it (Registered 63 clients at Shelter_0, "
     "lodgingFulfilled +100). Either Unity's A* path for that leg avoids (4, 0) (path computed at leg start "
     "against the flood of that moment) or Unity does not stop a vehicle for a tile that floods under it "
     "mid-leg. Port: roads.run_round collision check + path_cells; Unity: Vehicle.cs movement/flood check "
     "(TriggerRoadBlockageTask) and DeliverySystem.CanCreateDeliveryWithEstimate."),
    ("OPEN", "all", "The 14 calibration captures (scratchpad/cap32b, cap32_fresh) were wiped from the session "
     "scratchpad on 2026-09-08; test_replay_forward / diag_marks have no inputs until they are regenerated "
     "(any deterministic driver works: validate_plan.py writes the same trace format). Until then the "
     "four runs/validate logs are the only oracles; 5901 is exact on all of them and is the regression check."),
    ("FIXED", "5503", "Casework Request mechanic ported (task creation from the tracker, aged at birth; event "
     "re-arm on completion/expiry/choice 2, swept before EVERY tracker pass incl. both rollover passes; "
     "facility-aware client removal; the game's double removal per landing, interleaved unload/complete; "
     "choice 1 = up to 3 newest operational casework sites, quantity split evenly)."),
    ("FIXED", "7002", "A vehicle reaching its source on the epilogue frame loads AFTER the round's production "
     "(roads.run_round epilogue: to_src deferred to the next round's first frame)."),
    ("FIXED", "5503", "Client groups are tagged with the specific facility (Shelter_4, Motel), not the category."),
    ("FIXED", "5503", "diag_lockstep replays the trace's `taken` choices (what Unity was sent), not the gene."),
]


class Session:
    def __init__(self, seed, evo_log="runs/evo14.jsonl"):
        self.seed = seed
        self.trace_path = f"runs/validate/staff_{seed}.json"
        self.log_path = self.trace_path.replace(".json", ".log")
        self.trace = json.load(open(self.trace_path))
        self.row = best_row(evo_log, seed)
        self.um = D.unity_marks(self.log_path)
        self.s0 = D.seed_step(self.log_path)
        self.uev = unity_events(self.log_path)
        self._drive()

    def _drive(self):
        w = fresh_world(seed_state(self.log_path), FloodMap.load())
        m = CoraActions(random.Random(0))
        self.before, self.after, self.marks, self.fleet, self.diffs = [], [], [], [], []
        for i, step in enumerate(self.trace):
            gene = self.row["plan"][i] if i < len(self.row["plan"]) else {"choices": {}, "menu": []}
            self.before.append(w.clone())
            drive_step(w, m, step, gene)
            w.tasks.fleet.events = []
            marks = []
            S.step_round(w, marks=marks)
            self.fleet.append(list(w.tasks.fleet.events)); w.tasks.fleet.events = None
            self.marks.append(marks)
            self.after.append(w.clone())
            um, sm = step["after"]["rewardMetrics"], w.economy.metrics()
            d = {k: (um.get(k), sm.get(k)) for k in _KEYS if um.get(k) != sm.get(k)}
            ub = step["after"]["satisfactionAndBudget"]["budget"]
            if ub != w.economy.budget:
                d["budget"] = (ub, w.economy.budget)
            self.diffs.append(d)
        # concatenated draw streams (Unity labels choice-time draws with the previous step)
        unity = [(s, x) for s in sorted(self.um) if self.s0 is not None and s > self.s0 for x in self.um[s]]
        port = [(i + 1, x) for i, ms in enumerate(self.marks) for x in ms]
        k = next((j for j, (a, b) in enumerate(zip(unity, port)) if a[1] != b[1]), None)
        self.draw_div = None if k is None and len(unity) == len(port) else (k, unity[k] if k is not None and k < len(unity) else None, port[k] if k is not None and k < len(port) else None)
        self.first_diff = next((i for i, d in enumerate(self.diffs) if d), None)

    # ── views ──────────────────────────────────────────────────────────────────
    def summary(self, i):
        step = self.trace[i]; w = self.after[i]
        um, sm = step["after"]["rewardMetrics"], w.economy.metrics()
        print(f"--- seed {self.seed} step {i} (port day {w.day} seg {w.segment}) "
              f"budget U={step['after']['satisfactionAndBudget']['budget']} P={w.economy.budget}")
        if self.diffs[i]:
            print("   DIFF:", {k: f"U={u} P={p}" for k, (u, p) in self.diffs[i].items()})
        else:
            print("   counters and budget identical")
        nu = len(self.um.get(i + 1 + (self.s0 or 0), [])) if self.s0 is not None else "?"
        print(f"   draws this step: unity {nu} port {len(self.marks[i])}; "
              f"unity taken: {[(x.get('kind'), x.get('stableTaskId') or x.get('action_id'), x.get('choiceId')) for x in step['taken']]}")

    def counters(self, i):
        step = self.trace[i]; um, sm = step["after"]["rewardMetrics"], self.after[i].economy.metrics()
        for k in _KEYS:
            flag = "   " if um.get(k) == sm.get(k) else " <-"
            print(f"   {k:22s} unity {str(um.get(k)):>10}  port {str(sm.get(k)):>10}{flag}")

    def show_board(self, i):
        step = self.trace[i]
        print("   port (before answering):", board(self.before[i]))
        print("   unity (before answering):", [f"{x.get('taskId')} {x.get('stableTaskId') or x.get('taskTitle')}@{x.get('facilityName') or x.get('affectedFacility')} r={x.get('roundsRemaining')}"
                                             for x in (step["before"].get("allActiveTasks") or [])])

    def show_fleet(self, i):
        print("   port pending at step start:", [(p[0], p[1]) for p in self.before[i].tasks.pending])
        for ev in self.fleet[i]:
            print("   port fleet:", ev)
        for line in self.uev.get(i + 1, []):          # Unity's step tag is the port's + 1
            if "delivery:" in line and "delivery:leg" not in line:
                print("   unity:", line)

    def show_groups(self, i):
        w = self.after[i]
        print("   port groups (gid, facility, count, need, arrival, flagged):",
              [(g.gid, g.facility, g.count, g.with_need, g.arrival_round, g.casework_generated) for g in w.clients.groups])
        for line in self.uev.get(i + 1, []):
            if any(k in line for k in ("Registered", "Removed", "removed", "departed", "generated casework", "re-enabled")):
                print("   unity:", line)

    def show_draws(self, i):
        u = self.um.get(i + 1 + (self.s0 or 0), []) if self.s0 is not None else []
        p = self.marks[i]
        k = next((j for j, (a, b) in enumerate(zip(u, p)) if a != b), None)
        print(f"   unity {len(u)} draws, port {len(p)}; first per-step mismatch at {k} "
              f"(per-step labels can differ at the choice boundary; the concatenated stream is the truth)")
        if k is not None:
            print("   unity around it:", u[max(0, k - 3):k + 4])
            print("   port  around it:", p[max(0, k - 3):k + 4])
        elif len(u) != len(p):
            print("   tail unity:", u[len(p):len(p) + 6], " tail port:", p[len(u):len(u) + 6])

    def headline(self):
        fd = self.first_diff
        dd = self.draw_div
        return (f"seed {self.seed}: first counter/budget diff at step {fd} {self.diffs[fd] if fd is not None else ''}; "
                f"draw stream {'identical' if dd is None else 'diverges at concatenated index %s: unity %s port %s' % dd}")


def print_bugs():
    print("KNOWN BUGS / STATE")
    for status, seed, text in BUGS:
        print(f"  [{status}] ({seed}) {text}")


def run_session(sess, script=None):
    i = sess.first_diff if sess.first_diff is not None else 0
    print(sess.headline()); sess.summary(i)
    cmds = iter(script.split(";")) if script else None
    while True:
        try:
            cmd = next(cmds).strip() if cmds else input(f"[{sess.seed} step {i}] > ").strip()
        except (StopIteration, EOFError):
            return
        if not cmd:
            continue
        op, *arg = cmd.split()
        if op == "q":
            return
        elif op == "n":
            i = min(i + 1, len(sess.trace) - 1); sess.summary(i)
        elif op == "p":
            i = max(i - 1, 0); sess.summary(i)
        elif op == "s" and arg:
            i = max(0, min(int(arg[0]), len(sess.trace) - 1)); sess.summary(i)
        elif op == "first":
            i = sess.first_diff if sess.first_diff is not None else 0; sess.summary(i)
        elif op == "bugs":
            print_bugs()
        elif op == "b":
            sess.show_board(i)
        elif op == "f":
            sess.show_fleet(i)
        elif op == "g":
            sess.show_groups(i)
        elif op == "d":
            sess.show_draws(i)
        elif op == "c":
            sess.counters(i)
        else:
            print("   commands: s <n> | n | p | first | bugs | b | f | g | d | c | q")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("seed", nargs="?", type=int)
    ap.add_argument("--log", default="runs/evo14.jsonl")
    ap.add_argument("--cmd", default=None, help="semicolon-separated commands, then exit")
    a = ap.parse_args()
    if a.seed is None:
        seeds = sorted(int(re.match(r"staff_(\d+)\.json", f).group(1)) for f in os.listdir("runs/validate")
                       if re.match(r"staff_(\d+)\.json", f) and os.path.exists(f"runs/validate/{f[:-5]}.log"))
        for s in seeds:
            print(Session(s, a.log).headline())
        print(); print_bugs()
        return 0
    run_session(Session(a.seed, a.log), a.cmd)
    return 0


if __name__ == "__main__":
    sys.exit(main())
