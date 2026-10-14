"""Replay recorded benchmark games on the surrogate and report where they part from Unity.

Each bench episode record (bench/episode.py) keeps, per round, every tool call with Unity's
outcome and the game's budget, satisfaction, score and fulfilment counters after the round. The
calls are fed back through cora.executor on SimEnv with the same seed, so a model's game -- its
invalid calls, refused choices and odd orderings included -- is checked round by round.

    python -m oracle.sim.bench_parity results/<run>/episodes.jsonl [...]

Prints one line per game (EXACT, or the first round that differs and how) and a tally.
"""
from __future__ import annotations

import json
import sys

# What each round is checked on. Outcomes, budget, satisfaction and score are what the model,
# search and RL see; the counters are the benchmark's reported fulfilment rates.
_COUNTERS = (("foodFul", "foodFulfilled"), ("foodRes", "foodResolved"),
             ("lodgFul", "lodgingFulfilled"), ("lodgRes", "lodgingResolved"))


def _keyed(outcomes) -> list:
    return sorted(([t, json.dumps(a, sort_keys=True), st] for t, a, st in outcomes), key=lambda x: x[:2])


def check_episode(rec: dict, counters: bool = True) -> list:
    """[(round, [differences])] for the first round that differs; [] when the replay is exact."""
    from cora import executor
    from cora.scoring import score_components
    from oracle.rollout import DECISIONS
    from oracle.sim.env import SimEnv
    env = SimEnv(seed=rec["seed"], max_episode_steps=DECISIONS + 4,
                 manual_transfers=rec.get("transfers") == "manual")
    env.reset()
    rounds = rec["rounds"]
    for i, rd in enumerate(rounds):
        calls = rd.get("calls") or []
        res, (_, _, term, trunc, info) = executor.execute_turn(env, [(c["tool"], c["args"]) for c in calls])
        got = _keyed([r.tool, r.args, r.status] for r in res)
        want = _keyed([c["tool"], c["args"], c["status"]] for c in calls)
        diffs = [f"{w[0]} {w[1]}: Unity {w[2]}, port {g[2]}" for w, g in zip(want, got) if w != g]
        if len(got) != len(want):
            diffs.append(f"{len(want)} calls in Unity, {len(got)} on the port")
        rm = info.get("reward_metrics") or env.game_state.get("rewardMetrics") or {}
        score = round(score_components(rm)["score"], 4)
        for k, g in (("budget", info.get("budget")), ("sat", info.get("satisfaction"))):
            if rd.get(k) is not None and g != rd[k]:
                diffs.append(f"{k}: Unity {rd[k]} | port {g}")
        if rd.get("comps") and abs(rd["comps"].get("score", 0.0) - score) > 1e-4:
            diffs.append(f"score: Unity {rd['comps']['score']} | port {score}")
        if counters:
            for k, src in _COUNTERS:
                if rd.get(k) is not None and rm.get(src) != rd[k]:
                    diffs.append(f"{k}: Unity {rd[k]} | port {rm.get(src)}")
        # The board the model sees next: Unity's is the next round's observation. A task left
        # open on one side only is a divergence even before it changes an outcome.
        nxt = rounds[i + 1].get("obs") if i + 1 < len(rounds) else None
        if nxt and isinstance(nxt.get("tasks"), list):
            want_ids = sorted(t.get("taskId") for t in nxt["tasks"])
            got_ids = sorted(t.get("taskId") for t in env.game_state.get("allActiveTasks") or [])
            if want_ids != got_ids:
                diffs.append(f"board: Unity only {sorted(set(want_ids) - set(got_ids))}, "
                             f"port only {sorted(set(got_ids) - set(want_ids))}")
        if diffs:
            return [(rd["r"], diffs)]
        if term or trunc:
            break
    return []


def main(argv):
    counters = "--no-counters" not in argv
    paths = [a for a in argv if not a.startswith("--")]
    n = exact = score_only = 0
    for path in paths:
        for line in open(path):
            rec = json.loads(line)
            if rec.get("error") and not rec.get("rounds"):
                continue
            n += 1
            try:
                out = check_episode(rec, counters)
            except Exception as e:                      # a crash is a finding too
                out = [(None, [f"replay raised {type(e).__name__}: {e}"])]
            tag = f"{rec.get('model')} seed {rec['seed']}"
            if not out:
                exact += 1
                print(f"EXACT  {tag}")
                continue
            rnd, diffs = out[0]
            if all(d.split(":")[0] in {k for k, _ in _COUNTERS} for d in diffs):
                score_only += 1
            print(f"DIFF   {tag} round {rnd}: " + "; ".join(diffs)[:400])
    print(f"\n{exact}/{n} exact; {score_only} more differ only in the reported counters")


if __name__ == "__main__":
    main(sys.argv[1:])
