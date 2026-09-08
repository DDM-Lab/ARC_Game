"""Casework lifecycle, side by side: what Unity's ClientStayTracker generated / re-armed /
closed per step against what the port did, driving the port exactly as diag_lockstep does.

    python -m cora_sim.diag_casework runs/evo14.jsonl 5901 [--from 8 --to 14]
"""
import argparse, json, os, random, re, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cora_sim.sim as S                                    # noqa: E402
from cora_sim.actions import CoraActions                    # noqa: E402
from cora_sim.evolve import fresh_world                     # noqa: E402
from cora_sim.floodmap import FloodMap                      # noqa: E402
from cora_sim.diag_lockstep import best_row, drive_step                 # noqa: E402
from cora_sim.test_replay_forward import seed_state         # noqa: E402

_STEP = re.compile(r"\] s(\d+)d\d+r\d+f\d+ round:advance")
_LINES = ("generated casework task", "re-enabled for group", "Casework Request",
          "Removed entire group", "Partially removed", "Registered ", "without casework departed",
          "delivery:unload", "delivery:complete", "delivery:queue", "delivery:dispatch",
          "delivery:leg")


def unity_events(log):
    """step -> lines. A step's events sit between its round:advance mark and the next."""
    out, step = {}, 0
    for line in open(log, errors="ignore"):
        m = _STEP.search(line)
        if m:
            step = int(m.group(1)); continue
        if any(k in line for k in _LINES) and "TextMeshPro" not in line:
            if "delivery:" in line:
                line = re.sub(r'\{"s0".*?\} ', "", line)          # drop the RNG state
            out.setdefault(step, []).append(line.strip()[:150])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("log"); ap.add_argument("unity_seed", type=int)
    ap.add_argument("--from", dest="lo", type=int, default=0)
    ap.add_argument("--to", dest="hi", type=int, default=32)
    ap.add_argument("--fleet", action="store_true", help="also print vehicle events both sides")
    a = ap.parse_args()
    trace = f"runs/validate/staff_{a.unity_seed}.json"; ulog = trace.replace(".json", ".log")
    row = best_row(a.log, a.unity_seed)
    t = json.load(open(trace))
    ue = unity_events(ulog)
    w = fresh_world(seed_state(ulog), FloodMap.load())
    m = CoraActions(random.Random(0))
    port = []
    _create, _rearm = S._create_casework_task, type(w.clients).rearm
    def create(w_, gid, fac, need):
        g = w_.clients.group(gid)
        port.append(f"port: generated gid={gid} {fac} need={need} count={g.count} Y={S._unity_round(w_)-g.arrival_round}")
        return _create(w_, gid, fac, need)
    def rearm(self, gid):
        port.append(f"port: rearm gid={gid} ({'gone' if self.group(gid) is None else 'live'})")
        return _rearm(self, gid)
    _ph = type(w.clients).process_home
    def process_home(self, facility, quantity, counters):
        before = [(g.gid, g.count) for g in self.groups if g.facility == facility]
        got = _ph(self, facility, quantity, counters)
        port.append(f"port: remove {quantity} from {facility}: removed {got}; groups there were {before}")
        return got
    type(w.clients).process_home = process_home
    S._create_casework_task = create; type(w.clients).rearm = rearm
    # Unity's tracker, reconstructed from its own log lines: name -> [facility, count].
    ug, order = {}, []
    _reg = re.compile(r"Registered (\d+) clients at (\S+) \(Group: (\S+), Round: (\d+)\)")
    _ent = re.compile(r"Removed entire group (\S+) \((\d+) clients\)")
    _par = re.compile(r"Partially removed (\d+) clients from group (\S+)")
    _dep = re.compile(r"Group (\S+): (\d+) clients without casework departed")
    def apply_unity(line):
        m = _reg.search(line)
        if m:
            ug[m.group(3)] = [m.group(2), int(m.group(1)), int(m.group(4))]; order.append(m.group(3)); return
        m = _ent.search(line)
        if m and m.group(1) in ug:
            del ug[m.group(1)]; order.remove(m.group(1)); return
        m = _par.search(line)
        if m and m.group(2) in ug:
            ug[m.group(2)][1] -= int(m.group(1)); return
        m = _dep.search(line)
        if m and m.group(1) in ug:
            ug[m.group(1)][1] -= int(m.group(2))
    for i, step in enumerate(t):
        gene = row["plan"][i] if i < len(row["plan"]) else {"choices": {}, "menu": []}
        port.clear()
        drive_step(w, m, step, gene)
        if a.fleet:
            w.tasks.fleet.events = []
            port.append("port pending: " + str([(p[0], p[1]) for p in w.tasks.pending]))
        S.step_round(w)
        if a.fleet:
            for ev in w.tasks.fleet.events:
                port.append("port fleet: " + str(ev))
            w.tasks.fleet.events = None
        if not (a.lo <= i <= a.hi):
            for line in ue.get(i, []):
                apply_unity(line)
            continue
        print(f"--- step {i} (port day {w.day} seg {w.segment}) caseworkRequested unity="
              f"{step['after']['rewardMetrics'].get('caseworkRequested')} port={w.economy.counters['caseworkRequested']}")
        for line in ue.get(i, []):
            print("  unity:", line)
            apply_unity(line)
        print("   unity groups:", [(n, ug[n][0], ug[n][1], ug[n][2]) for n in order])
        for line in port:
            print("  ", line)
        groups = [(g.gid, g.facility, g.count, g.with_need, g.arrival_round, g.casework_generated) for g in w.clients.groups]
        print("   port groups:", groups)
    return 0


if __name__ == "__main__":
    sys.exit(main())
