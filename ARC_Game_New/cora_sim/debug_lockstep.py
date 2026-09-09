"""Interactive lockstep debugger: the port against a headless Unity run of the SAME plan,
step by step, with the known open bugs and where each seed first diverges.

    python -m cora_sim.debug_lockstep                       # summary of every validated seed + bug list
    python -m cora_sim.debug_lockstep 5503                  # interactive session on one seed
    python -m cora_sim.debug_lockstep 5503 --cmd "first;b;f;g;d"   # scripted (no TTY needed)

Inputs (per seed N): cora_sim/runs/validate/staff_N.json (the gym trace: before/taken/after per
round) and cora_sim/runs/validate/staff_N.log (Unity's ARC_SNAPSHOT_DEBUG=1 log with [RNGMARK]/
[RNGCTX] draw marks); the plan comes from cora_sim/runs/evo14.jsonl (best row for that seed).
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
from cora_sim import paths as P

BUGS = [
    ("EXACT", "5503 5901 6001 7002", "all four validated seeds: draw stream identical for 32 rounds, every "
     "reward counter, the budget and the final score equal at every round (2026-09-08)."),
    ("OPEN", "all", "The 14 calibration captures (scratchpad/cap32b, cap32_fresh) were wiped from the session "
     "scratchpad on 2026-09-08, so test_replay_forward / diag_marks have no inputs. The four runs/validate "
     "logs are the only oracles; regenerate captures with validate_plan.py (same trace format) before "
     "trusting any further rule change beyond these four seeds."),
    ("UNVERIFIED", "-", "Transcribed from the C# without a capture exercising them: Road Blockage choices for food "
     "cargo and for not-yet-loaded population (treated as inert, as the C# source lookup fails); casework "
     "choice 2 (wait, -10); casework deliveries split across 2-3 sites; kitchen orders from 2+ reachable "
     "kitchens both shipping the full quantity (6001 s22 shows the queue lines, the landings were not "
     "compared); a vehicle that reaches its source on the epilogue frame and finds it empty."),
    ("KNOWN GAP", "-", "The port debits a relocation's source when the trip LANDS; Unity debits at LOAD. Counters "
     "agree, but mid-flight populations differ (5503 step 13: Motel 235 vs 215), which a population "
     "threshold trigger evaluated in that window could see."),
    ("FIXED", "5503", "Casework Request mechanic (task from the tracker, aged at birth; event re-arm swept before "
     "every tracker pass; facility-aware double removal, interleaved unload/complete; up to 3 newest sites)."),
    ("FIXED", "5503", "Flood blockage chain: stopped delivery credits its nominal quantity as fulfilled (late "
     "delivery to a closed parent), Road Blockage Emergency task (-20 on expiry, -30 more if clients were "
     "aboard), $1500 immediate transport to shelters with space."),
    ("FIXED", "5503", "Multi-delivery 'Send to Shelters' ships the full quantity split across up to 3 shelters "
     "with space, uncapped by space or source; immediate variant moves min(per, source, space) per shelter."),
    ("FIXED", "7002", "Epilogue-frame loads happen after production; an empty-source abort orphans the trip so "
     "the parent can only expire; food expiry credits nothing; tracker round is stale on segment 4."),
    ("FIXED", "6001", "Kitchen orders are multi-source: one full order per reachable stocked kitchen (newest "
     "first, up to 3), each trip loading from its own kitchen; shelter-food incomplete penalty; the action "
     "model allows debt like the game (allowNegativeBudget)."),
]


class Session:
    def __init__(self, seed, evo_log=None):
        self.seed = seed
        evo_log = evo_log or P.EVO_LOG
        self.trace_path = os.path.join(P.VALIDATE, f"staff_{seed}.json")
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
    ap.add_argument("--log", default=P.EVO_LOG)
    ap.add_argument("--cmd", default=None, help="semicolon-separated commands, then exit")
    a = ap.parse_args()
    if a.seed is None:
        seeds = sorted(int(re.match(r"staff_(\d+)\.json", f).group(1)) for f in os.listdir(P.VALIDATE)
                       if re.match(r"staff_(\d+)\.json", f) and os.path.exists(os.path.join(P.VALIDATE, f"{f[:-5]}.log")))
        for s in seeds:
            print(Session(s, a.log).headline())
        print(); print_bugs()
        return 0
    run_session(Session(a.seed, a.log), a.cmd)
    return 0


if __name__ == "__main__":
    sys.exit(main())
