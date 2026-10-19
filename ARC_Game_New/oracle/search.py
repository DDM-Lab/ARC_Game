"""Closed-loop search agents on the surrogate: MCTS and RHEA over one shared set of macro-moves.

At every decision the agent searches from the current state, plays the move it settles on, and
searches again at the next decision -- an agent acting turn by turn, as a model does. Both
algorithms choose among the same MOVES (a turn's tool calls, built by a generator from the
state) and finish every simulation with the tuned rules policy, so they differ only in how they
spend the same budget.

Information. `fair` (the default) gives the search no knowledge of the future: every simulation
runs on a clone of the current state whose random stream is reseeded, so floods, arrivals and
requests are drawn afresh -- the planner knows the rules and the present, not the dice.
`clairvoyant` simulates on the true stream, so it knows exactly what will happen: an upper
reference, not a competitor. Either way the planner sees the full current game state (including
internals the observation does not show); only the future randomness is hidden.

`uct` / `uct-noop` is the from-scratch reference: plain UCT over uniformly sampled whole turns,
with random (or do-nothing) rollouts -- no MOVES, no rules policy, no tuned strategy.
`uct-f4` samples turns with sanity filters 1-4 (_filtered_calls), `uct-f6` adds 5-6, `uct-f7`
also drops Motel / Rapid Response choices; an `e` suffix (uct-f6e, uct-f7e) allows empty turns.
`uct-mast` is uct-f6e with MAST: the generator learns, from the search's own simulations, which
actions tend to end in a high score (no filter 7 -- it has to find the Motel cost itself).

Budget is counted in simulator decision-steps per decision (one executor turn = one step), the
unit of the compute curve.

    python -m oracle.search 7003 --algo mcts --budget 4000 --plan plan.json
    python -m oracle.search 7003 --algo rhea --budget 4000 --info clairvoyant --plan plan.json
    python -m oracle.sim.capture 7003 --calls plan.json     # the score on Unity (unsandboxed)
"""
from __future__ import annotations

import argparse
import json
import math
import random
import sys
import time
import zlib

from oracle.rollout import DECISIONS

# The tuned rules policy's runners-up (bench.baselines.tune finalists, 2026-10-14).
_VARIANTS = [
    {"kitchens": 5, "casework_sites": 1, "max_shelters": 4, "shelter_per_motel": 50, "shelters_first": 2,
     "rrv": "never", "motel": False, "build_until": 10, "hire_trained": False, "partial_food": 0,
     "budget_food": True, "budget_casework": False},
    {"kitchens": 4, "casework_sites": 3, "max_shelters": 9, "shelter_per_motel": 0, "shelters_first": 4,
     "rrv": "never", "motel": False, "build_until": 10, "hire_trained": False, "partial_food": 50,
     "budget_food": True, "budget_casework": True},
]
N_RANDOM = 3
MOVES = (["rules", "var0", "var1", "+kitchen", "+shelter", "+casework", "nobuild", "motel", "rrv", "noop"]
         + [f"rand{k}" for k in range(N_RANDOM)])


def _extra_build(env, calls, btype):
    """`calls` plus one more `btype` building on the free site nearest the communities, and the
    workers to staff it (a trained hire = 2 of the 4 units)."""
    from bench.baselines.rules import _dist
    gs = env.game_state or {}
    taken = {a["site_id"] for t, a in calls if t == "build"}
    sites = [s for s in (gs.get("constructionState") or {}).get("availableSites") or []
             if s.get("isAvailable") and s.get("siteId") not in taken]
    if not sites:
        return calls
    comm = [f.get("position") for f in (gs.get("mapState") or {}).get("facilities") or []
            if f.get("buildingType") == "Community" and f.get("position")]
    site = min(sites, key=lambda s: min((_dist(s.get("position"), p) for p in comm), default=0.0))
    return list(calls) + [("build", {"type": btype, "site_id": int(site["siteId"])}),
                          ("hire", {"kind": "trained", "count": 2})]


def move_calls(env, decision, move):
    """The tool calls `move` makes in this state."""
    from bench.baselines.rules import rules_calls
    if move == "noop":
        return []
    if move.startswith("rand"):
        from bench.baselines.common import tool_calls
        from bench.baselines.explore import random_basket
        rng = random.Random(zlib.crc32(f"{move}/{decision}".encode()))
        return tool_calls(env, random_basket(env, rng))
    if move in ("var0", "var1"):
        return rules_calls(env, decision, DECISIONS, _VARIANTS[int(move[-1])])
    if move == "motel":
        return rules_calls(env, decision, DECISIONS, {"motel": True})
    if move == "rrv":
        return rules_calls(env, decision, DECISIONS, {"rrv": "always"})
    base = rules_calls(env, decision, DECISIONS)
    if move == "nobuild":
        return [c for c in base if c[0] != "build"]
    if move.startswith("+"):
        return _extra_build(env, base, move[1:])
    return base                                                   # "rules"


class Sim:
    """The planner's view: clones of the current state, reseeded unless clairvoyant, and the
    step counter the budget is charged to."""

    def __init__(self, env, decision, info, rng):
        self.env, self.decision, self.info, self.rng, self.steps = env, decision, info, rng, 0
        # Training data (run(..., data=...)): the root's candidates and their statistics, and a
        # sample of rollout states with the final score parts they led to. Sampling draws from
        # its own stream, so logging never changes what the search does.
        self.root, self.samples, self.sample_p = None, None, 0.0
        self.srng = random.Random(decision)
        # Common random numbers (run(..., crn=K)): fair worlds come from a pool of K bases, one
        # stream per random source and round (oracle.sim.rng.StreamedRandom), used round-robin,
        # so sibling turns are compared on the same imagined futures. 0 = a fresh world each time.
        self.crn, self.pool, self.k, self.last_base = 0, None, 0, None
        self.tree_info = None          # a dict to fill: uct_move then summarises its tree there

    def world(self):
        from oracle.sim.rng import StreamedRandom, UnityRandom
        e = self.env.clone()
        if self.info == "fair" and self.crn:
            if self.pool is None:
                self.pool = [self.rng.getrandbits(32) for _ in range(self.crn)]
            self.last_base = self.pool[self.k % self.crn]
            self.k += 1
            e.world.rng = StreamedRandom(self.last_base)
        elif self.info == "fair":
            e.world.rng = UnityRandom(state=tuple(self.rng.getrandbits(32) for _ in range(4)))
        return e

    def play(self, e, d, move):
        """One decision of `move`; True when the game is over."""
        from cora import executor
        self.steps += 1
        _, (_, _, term, trunc, _) = executor.execute_turn(e, move_calls(e, d, move))
        return term or trunc

    def play_calls(self, e, calls):
        """One decision of explicit tool calls; True when the game is over."""
        from cora import executor
        self.steps += 1
        _, (_, _, term, trunc, _) = executor.execute_turn(e, list(calls))
        return term or trunc

    def finish(self, e, d, rollout="rules", rr=None, gen=None, trace=None):
        """Play `rollout` from decision d to the end; the final score. "rules" is the tuned rules
        policy; "random" a random legal basket each turn from `gen` (default: uniform);
        "noop" nothing at all. `trace`, when given, collects the rollout's calls."""
        from cora.scoring import score_components
        pending = []
        while d < DECISIONS:
            if self.samples is not None and self.srng.random() < self.sample_p:
                from cora.observation import observe
                pending.append({"decision": d, "obs": observe(e.game_state, e.valid_actions)})
            if rollout == "rules":
                over = self.play(e, d, "rules")
            else:
                calls = _thaw((gen or _random_calls)(e, rr)) if rollout == "random" else []
                if trace is not None:
                    trace.append(calls)
                over = self.play_calls(e, calls)
            if over:
                break
            d += 1
        parts = score_components(e.game_state.get("rewardMetrics"))
        for p in pending:
            p["parts"] = parts
        if pending:
            self.samples.extend(pending)
        return parts["score"]

    def evaluate(self, moves):
        """A fresh world: `moves` from the current decision, then rules to the end."""
        e, d = self.world(), self.decision
        for m in moves:
            if d >= DECISIONS or self.play(e, d, m):
                return self.finish(e, DECISIONS)
            d += 1
        return self.finish(e, d)


def _reroot(tree, stats, kids, best):
    """Tree reuse: keep the subtree under the move played as the next decision's tree."""
    if tree is None:
        return
    n = len(best)
    tree["stats"] = {k[n:]: v for k, v in stats.items() if k[:n] == best}
    tree["stats"].setdefault((), [0, 0.0])
    if kids is not None:
        tree["kids"] = {k[n:]: [ch[n:] for ch in v] for k, v in kids.items() if k[:n] == best}


def mcts_move(sim, budget, c=0.25, tree=None):
    """Open-loop UCT over move sequences (a node is a sequence, not a state: with reseeded
    worlds the same sequence reaches different states). Returns the most-visited root move.
    `tree` (a dict kept across decisions) turns on tree reuse: the search starts from the
    subtree under the previous decision's move instead of from nothing."""
    stats = (tree or {}).get("stats") or {(): [0, 0.0]}     # sequence -> [visits, total score]
    while sim.steps < budget:
        seq = ()
        while True:                           # select / expand
            kids = [seq + (m,) for m in MOVES]
            fresh = [k for k in kids if k not in stats]
            if fresh:
                seq = sim.rng.choice(fresh)
                stats[seq] = [0, 0.0]
                break
            n = stats[seq][0]
            seq = max(kids, key=lambda k: stats[k][1] / stats[k][0]
                      + c * math.sqrt(math.log(n + 1) / stats[k][0]))
            if sim.decision + len(seq) >= DECISIONS:
                break
        v = sim.evaluate(seq)
        for i in range(len(seq) + 1):
            s = stats[seq[:i]]
            s[0] += 1; s[1] += v
    root = [((m,), stats.get((m,), [0, 0.0])) for m in MOVES]
    sim.root = [(k[0], n, tot) for k, (n, tot) in root]
    best = max(root, key=lambda ks: (ks[1][0], ks[1][1]))[0]
    sim.reuse = (stats.get(best, [0])[0], stats[()][0])
    _reroot(tree, stats, None, best)
    return best[0]


def _random_calls(env, rr):
    """A uniformly random legal turn (bench.baselines.explore.random_basket) as tool calls, frozen
    so it can key a tree node. Calls name tasks by token and facilities by name, so the same
    basket can be replayed in another sampled world (where the game may refuse parts of it)."""
    from bench.baselines.common import tool_calls
    from bench.baselines.explore import random_basket
    return tuple((t, tuple(sorted(a.items()))) for t, a in tool_calls(env, random_basket(env, rr)))


class Mast:
    """Move-Average Sampling Technique (Finnsson & Bjornsson 2008): the mean final score of every
    simulation an action took part in, kept for the whole game, turned into Gibbs sampling weights
    exp((Q(a) - mean) / tau) for the turn generator -- the tree's new children and the rollouts
    both prefer actions that have done well. No game knowledge: an action is its tool call, with a
    task named by its kind (FOOD, RELOC, CASEWORK, BUDGET...) and choice id, so "relocation choice
    1" pools across communities and rounds. Unseen actions get the mean (weight 1)."""

    def __init__(self, tau=0.05):
        self.q, self.tau, self.n, self.tot = {}, tau, 0, 0.0

    @staticmethod
    def atom(name, args):
        if name == "task":
            return ("task", str(args["task_id"]).split("#")[0].split("_")[0], int(args["choice_id"]))
        return (name,) + tuple(sorted(args.items()))

    def weight(self, name, args):
        if not self.n:
            return 1.0
        mean = self.tot / self.n
        q = self.q.get(self.atom(name, args))
        z = ((q[1] / q[0]) - mean) / self.tau if q else 0.0
        return math.exp(max(-20.0, min(20.0, z)))

    def update(self, turns, v):
        for a in {self.atom(t, dict(args)) for calls in turns for t, args in calls}:
            q = self.q.setdefault(a, [0, 0.0])
            q[0] += 1; q[1] += v
        self.n += 1; self.tot += v


def _filtered_calls(env, rr, strategy=False, max_actions=4, empty=False, premium=True, mast=None):
    """A random legal turn with the wasted and contradictory parts filtered out:
      1. at most one hire per turn (the menu offers ten alternative counts);
      2. every open task answered, with a uniformly random choice;
      3. a building is staffed only when it needs workers and the free pool covers its gap
         (staff always staffs fully);
      4. at most one build per site.
    `strategy` adds two filters that carry some game knowledge:
      5. no deconstruction;
      6. a hire only up to what this turn's staffing and builds still need, net of free and
         arriving workers.
    `empty` lets a turn take no menu action at all (0..max_actions instead of 1..max_actions),
    so a turn can be just the task answers. `premium=False` is filter 7: no Motel and no Rapid
    Response choices (a task offering only those is left unanswered). `mast` (a Mast) weights
    every task's choice and the order menu actions are drawn in.
    Everything else -- which tasks' choices, what to build where, when to hire -- stays random."""
    from cora.actions import as_tool_call
    from cora.observation import task_tokens
    from bench.baselines.explore import _MENU_TYPES
    gs = env.game_state or {}
    tasks = gs.get("allActiveTasks") or []
    tokens = task_tokens(tasks)
    calls = []
    for t in tasks:
        opts = [c["choiceId"] for c in t.get("choices") or []
                if premium or not any(k in (c.get("choiceText") or "").lower() for k in ("motel", "rapid response"))]
        if opts:
            tid = tokens.get(t["taskId"], str(t["taskId"]))
            w = [mast.weight("task", {"task_id": tid, "choice_id": o}) for o in opts] if mast else None
            calls.append(("task", {"task_id": tid, "choice_id": int(rr.choices(opts, w)[0] if w else rr.choice(opts))}))
    wf = gs.get("workforceState") or {}
    free = 2 * (wf.get("freeTrainedWorkers") or 0) + (wf.get("freeUntrainedWorkers") or 0)
    arriving = 2 * (wf.get("trainedWorkersNotArrived") or 0) + (wf.get("untrainedWorkersNotArrived") or 0)
    gap = {f["facilityName"]: max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
           for f in (gs.get("mapState") or {}).get("facilities") or []
           if f.get("buildingStatus") == "NeedWorker" and f.get("facilityName")}
    menu = [a for a in env.valid_actions or [] if a.get("action_type") in _MENU_TYPES
            and not (strategy and a["action_type"] == "deconstruction")]
    if mast:     # weighted order without replacement (Efraimidis-Spirakis keys u ** (1 / w))
        menu.sort(key=lambda a: -(rr.random() ** (1.0 / mast.weight(*as_tool_call(a)))))
    else:
        rr.shuffle(menu)
    want, picked, sites, staffed, torn, hire = rr.randint(0 if empty else 1, max_actions), [], set(), set(), set(), None
    for a in menu:
        if len(picked) + (hire is not None) >= want:
            break
        t, name, args = a["action_type"], *as_tool_call(a)
        if name == "hire":
            if hire is None:
                hire = a
            continue
        if name == "build":
            if args["site_id"] in sites:
                continue
            sites.add(args["site_id"])
        elif name == "staff":
            g = gap.get(args["site"], 0)
            if args["site"] in staffed or not 0 < g <= free:
                continue
            staffed.add(args["site"])
            free -= g
        elif name == "deconstruct":
            if args["site"] in torn:
                continue
            torn.add(args["site"])
        picked.append((name, args))
    if hire is not None and strategy:
        short = (sum(g for s, g in gap.items() if s not in staffed) + 4 * len(sites)) - free - arriving
        units = lambda a: a["worker"]["quantity"] * (2 if a["worker"]["worker_action_type"] == "hire_trained" else 1)
        ok = [a for a in env.valid_actions or [] if a.get("action_type") == "worker"
              and a["worker"]["worker_action_type"].startswith("hire") and units(a) <= short]
        hire = hire if hire in ok else (rr.choice(ok) if ok else None)
    if hire is not None:
        picked.append(as_tool_call(hire))
    return tuple((t, tuple(sorted(a.items()))) for t, a in calls + picked)


def _thaw(calls):
    return [(t, dict(a)) for t, a in calls]


def turn_tags(env, calls):
    """What a turn does, as counts: build types, hires, training, staffing, deconstruction, task
    answers by task kind, Motel / Rapid Response choices, tasks left unanswered, and `empty` (no
    menu action). Read off the state the turn was generated in."""
    from collections import Counter
    from cora.observation import task_tokens
    tasks = (env.game_state or {}).get("allActiveTasks") or []
    toks = task_tokens(tasks)
    by_tok = {toks.get(t["taskId"], str(t["taskId"])): t for t in tasks}
    tags, answered, menu = Counter(), set(), 0
    for name, args in calls:
        args = dict(args)
        if name == "task":
            tok = str(args["task_id"])
            answered.add(tok)
            tags["task_" + tok.split("#")[0].split("_")[0].lower()] += 1
            t = by_tok.get(tok) or {}
            text = next((c.get("choiceText") or "" for c in t.get("choices") or []
                         if c.get("choiceId") == args["choice_id"]), "").lower()
            tags["motel"] += "motel" in text
            tags["rrv"] += "rapid response" in text
        else:
            menu += 1
            if name == "build":
                tags["build_" + str(args.get("type"))] += 1
            elif name == "hire":
                tags["hire_" + str(args.get("kind"))] += int(args.get("count") or 1)
            elif name == "train":
                tags["train"] += int(args.get("count") or 1)
            else:
                tags[name] += 1
    tags["unanswered"] = sum(1 for tok, t in by_tok.items() if t.get("choices") and tok not in answered)
    tags["empty"] = int(menu == 0)
    return {k: v for k, v in tags.items() if v}


def tree_summary(stats, kids, tags, leaves, depth0):
    """How much of the game tree one decision's search covered, and where: node and visit counts
    by depth, the root's spread of visits, the principal (most-visited) line, the action mix of
    every expanded turn by depth, and the leaf values the simulations ended on."""
    from collections import Counter, defaultdict
    nodes, visits, mix, by_depth = Counter(), Counter(), defaultdict(Counter), defaultdict(list)
    for dep, v in leaves:
        by_depth[dep].append(v)
    for seq, (n, _) in stats.items():
        if seq:
            nodes[len(seq)] += 1
            visits[len(seq)] += n
            for k, v in (tags.get(seq) or {}).items():
                mix[len(seq)][k] += v
    root = sorted(((stats[k][0], stats[k][1] / max(1, stats[k][0]), k) for k in kids.get((), [])), reverse=True)
    tot = sum(n for n, _, _ in root) or 1
    ent = -sum(n / tot * math.log(n / tot) for n, _, _ in root if n)
    pv, seq = [], ()
    while kids.get(seq):
        nxt = max(kids[seq], key=lambda k: stats[k][0])
        if not stats[nxt][0]:
            break
        pv.append({"tags": tags.get(nxt) or {}, "n": stats[nxt][0], "share": stats[nxt][0] / max(1, stats[seq][0]),
                   "q": stats[nxt][1] / stats[nxt][0]})
        seq = nxt
    return {"decision": depth0, "sims": stats[()][0], "nodes_by_depth": dict(sorted(nodes.items())),
            "visits_by_depth": dict(sorted(visits.items())), "max_depth": max(nodes, default=0),
            "root_children": len(root), "root_entropy": ent, "root_eff_branching": math.exp(ent),
            "root": [{"n": n, "q": q, "tags": tags.get(k) or {}} for n, q, k in root[:8]],
            "pv": pv, "mix_by_depth": {d: dict(c) for d, c in sorted(mix.items())},
            "leaves_by_depth": {d: {"n": len(vs), "mean": sum(vs) / len(vs), "min": min(vs), "max": max(vs)}
                                for d, vs in sorted(by_depth.items())}}


def _leaf(sim, e, d, over, leaf, depth, value, gen, rr, roll, cache, seq):
    """A simulation's value once the tree is left (uct_move `leaf`): "rules" / "random" finish
    the game with that rollout; "model", "partial" and "const" first play `depth` sampled turns,
    then read the state -- a decision model's value (`value(env)`, on the score scale), the score
    so far, or a constant (widening with no value information, the control). A game that ends
    first is worth its real score. With common random numbers and depth 0, a model's value is
    cached by (sequence, world): the same turns on the same world reach the same state."""
    from cora.scoring import score_components
    if leaf in ("rules", "random"):
        return sim.finish(e, DECISIONS if over else d, leaf, roll, gen)
    for _ in range(depth):
        if over or d >= DECISIONS:
            break
        over = sim.play_calls(e, _thaw(gen(e, rr)))
        d += 1
    if over or d >= DECISIONS:
        return score_components(e.game_state.get("rewardMetrics"))["score"]
    if leaf == "const":
        return 1.5
    if leaf == "partial":
        return score_components(e.game_state.get("rewardMetrics"))["score"]
    key = (seq, sim.last_base) if sim.crn and not depth else None
    if key is None or key not in cache:
        v = value(e)
        if key is None:
            return v
        cache[key] = v
    return cache[key]


def uct_move(sim, budget, rollout="random", gen=None, c=0.25, alpha=0.5, noop_child=False, mast=None,
             tree=None, leaf=None, leaf_depth=0, value=None, sims=0):
    """Planning from scratch: plain open-loop UCT with no game knowledge. A node's children are
    whole turns (baskets of calls) sampled uniformly from the legal menu, and a node visited n
    times may hold ceil((n+1)**alpha) of them (progressive widening); every simulation finishes
    with `rollout` ("random" or "noop"). `gen` samples the turns, for both the tree and the
    rollout: _random_calls (uniform, the default) or a _filtered_calls variant. `noop_child`
    makes the empty turn (no calls at all) every node's first child. `mast`, when given, is
    updated with every simulation's turns and final score. `tree` (kept across decisions) turns
    on tree reuse. With common random numbers (sim.crn) a rollout's own randomness is tied to its
    world, so siblings on the same world also share rollouts. Returns the
    most-visited root basket as tool calls."""
    gen = gen or _random_calls
    rr = random.Random(sim.rng.getrandbits(32))
    stats = (tree or {}).get("stats") or {(): [0, 0.0]}
    kids = (tree or {}).get("kids") or {}
    logging, tags, leaves, vcache = sim.tree_info is not None, {}, [], {}
    lo, hi, it = math.inf, -math.inf, 0           # leaf-value range, for min-max normalised Q
    while (it < sims) if sims else (sim.steps < budget):
        it += 1
        e, d, seq, over = sim.world(), sim.decision, (), False
        while d < DECISIONS and not over:
            ch = kids.setdefault(seq, [])
            if len(ch) < math.ceil((stats[seq][0] + 1) ** alpha):      # widen: a new sampled turn
                turn = () if noop_child and not ch else gen(e, rr)
                child = seq + (turn,)
                fresh = child not in stats
                if fresh:
                    stats[child] = [0, 0.0]
                    ch.append(child)
                    if logging:
                        tags[child] = turn_tags(e, turn)
            elif leaf is None:
                n = stats[seq][0]
                child, fresh = max(ch, key=lambda k: stats[k][1] / stats[k][0]
                                   + c * math.sqrt(math.log(n + 1) / stats[k][0])), False
            else:                       # leaf modes: Q min-max normalised over the tree (MuZero)
                n, span = stats[seq][0], hi - lo
                child, fresh = max(ch, key=lambda k: ((stats[k][1] / stats[k][0] - lo) / span if span > 0 else 0.5)
                                   + c * math.sqrt(math.log(n + 1) / stats[k][0])), False
            seq = child
            over = sim.play_calls(e, _thaw(seq[-1]))
            d += 1
            if fresh:
                break
        trace = [] if mast else None
        roll = random.Random(sim.last_base) if sim.crn and sim.last_base is not None else rr
        if leaf is None:
            v = sim.finish(e, DECISIONS if over else d, rollout, roll, gen, trace)
        else:
            v = _leaf(sim, e, d, over, leaf, leaf_depth, value, gen, rr, roll, vcache, seq)
            lo, hi = min(lo, v), max(hi, v)
        if logging:
            leaves.append((len(seq), v))
        if mast:
            mast.update([_thaw(t) for t in seq] + trace, v)
        for i in range(len(seq) + 1):
            s = stats[seq[:i]]
            s[0] += 1; s[1] += v
    if logging:
        sim.tree_info = tree_summary(stats, kids, tags, leaves, sim.decision)
    best = max(kids.get((), []), key=lambda k: (stats[k][0], stats[k][1]), default=((),))
    sim.root = [(_thaw(k[-1]), stats[k][0], stats[k][1]) for k in kids.get((), [])]
    sim.reuse = (stats[best][0] if best in stats else 0, stats[()][0])   # visits a reused tree would keep
    if best in stats:
        _reroot(tree, stats, kids, best)
    elif tree is not None:
        tree.clear()
    return _thaw(best[-1])


def greedy_atoms(env):
    """Every single action open this turn as one tool call: each choice of each open task, and
    each menu entry (build, hire, train, staff, deconstruct)."""
    from cora.actions import as_tool_call
    from cora.observation import task_tokens
    from bench.baselines.explore import _MENU_TYPES
    gs = env.game_state or {}
    tasks = gs.get("allActiveTasks") or []
    tokens = task_tokens(tasks)
    atoms = [("task", (("choice_id", int(c["choiceId"])), ("task_id", tokens.get(t["taskId"], str(t["taskId"])))))
             for t in tasks for c in t.get("choices") or []]
    for a in env.valid_actions or []:
        if a.get("action_type") in _MENU_TYPES and a["action_type"] != "resource_transfer":
            name, args = as_tool_call(a)
            atoms.append((name, tuple(sorted(args.items()))))
    return list(dict.fromkeys(atoms))


def greedy_move(sim, horizon=1, worlds=4):
    """Myopic greedy: the basket of actions for this turn that maximises the score `horizon`
    decisions from now, with nothing done after this turn. CORA turns are multi-action, so the
    basket is searched, not a single action: forward selection (add the action that raises the
    score most, given the basket so far, until none does), then local search over the basket --
    drop an action, or switch a task to another of its choices (or to no answer) -- until no
    single drop or switch raises it. That reaches a local optimum; the exact best basket is out
    of reach (every task's choices times every subset of ~50 menu entries).

    Every basket is scored on the same `worlds` imagined futures (common random numbers; the true
    future when clairvoyant). At horizon 1 an investment (a build, a hire, training) shows only
    its cost, so greedy-1 never builds; a relocation's van lands the round after it is sent, so
    greedy-1 also rarely relocates. Longer horizons see more of what a turn's actions earn."""
    from cora import executor
    from cora.scoring import score_components
    from oracle.sim.rng import StreamedRandom
    bases = [None] if sim.info == "clairvoyant" else [sim.rng.getrandbits(32) for _ in range(worlds)]
    cache = {}

    def value(calls):
        key = tuple(sorted(calls))
        if key in cache:
            return cache[key]
        tot = 0.0
        for b in bases:
            e = sim.env.clone()
            if b is not None:
                e.world.rng = StreamedRandom(b)
            turn = _thaw(calls)
            for h in range(horizon):
                sim.steps += 1
                _, (_, _, term, trunc, _) = executor.execute_turn(e, turn)
                if term or trunc:
                    break
                turn = []
            tot += score_components(e.game_state.get("rewardMetrics"))["score"]
        cache[key] = tot / len(bases)
        return cache[key]

    def task_of(a):
        return dict(a[1])["task_id"] if a[0] == "task" else None

    atoms = greedy_atoms(sim.env)
    chosen, best = (), value(())
    while True:                                  # forward selection
        used = {task_of(x) for x in chosen} - {None}
        cands = [a for a in atoms if a not in chosen and task_of(a) not in used]
        scored = [(value(chosen + (a,)), a) for a in cands]
        if not scored:
            break
        v, a = max(scored, key=lambda x: x[0])
        if v <= best + 1e-9:
            break
        chosen, best = chosen + (a,), v
    improved = True
    while improved:                              # local search: drop, or switch a task's answer
        improved = False
        neigh = [tuple(x for x in chosen if x != a) for a in chosen]
        for a in chosen:
            if a[0] == "task":
                rest = tuple(x for x in chosen if x != a)
                neigh += [rest + (b,) for b in atoms if b != a and task_of(b) == task_of(a)]
        used = {task_of(x) for x in chosen} - {None}
        neigh += [chosen + (b,) for b in atoms if b not in chosen and task_of(b) not in used]
        for n in neigh:
            v = value(n)
            if v > best + 1e-9:
                chosen, best, improved = n, v, True
                break
    sim.root = [(chosen, 1, best)]
    return _thaw(chosen)


def rhea_move(sim, budget, carry, horizon=8, pop=8, mut=None):
    """Rolling-horizon evolution: a population of `horizon`-move sequences, each scored on a
    fresh world (then rules to the end), evolved by elitism, uniform crossover and mutation.
    `carry` is the previous decision's population, shifted one move (the shift buffer)."""
    h = min(horizon, DECISIONS - sim.decision)
    mut = mut or 1.0 / h
    rnd = lambda: sim.rng.choice(MOVES)
    popl = [list(g[1:h + 1]) + [rnd() for _ in range(h - len(g[1:h + 1]))] for g in (carry or [])][:pop]
    popl.append(["rules"] * h)                # the rollout policy itself is always a candidate
    while len(popl) < pop:
        popl.append([rnd() for _ in range(h)])
    fit = {}
    def score(g):
        k = tuple(g)
        if k not in fit or sim.info == "fair":            # fair: every visit is a new future
            v = sim.evaluate(g)
            n, tot = fit.get(k, (0, 0.0))
            fit[k] = (n + 1, tot + v)
        n, tot = fit[k]
        return tot / n
    scored = sorted(((score(g), g) for g in popl), key=lambda x: -x[0])
    # Clairvoyant scores are cached (the future is fixed), and a cached sequence costs no budget,
    # so a population that only re-breeds sequences it has seen would never spend the budget:
    # duplicates are re-mutated, and the search stops after `patience` generations with nothing
    # new to evaluate (it has converged). Fair evaluation always simulates, so it always spends.
    stale, patience = 0, 50
    while sim.steps < budget and stale < patience:
        elite = scored[0][1]
        nxt = [elite]
        before = sim.steps
        while len(nxt) < pop:
            a, b = (max(sim.rng.sample(scored, 2), key=lambda x: x[0])[1] for _ in range(2))
            child = [x if sim.rng.random() < 0.5 else y for x, y in zip(a, b)]
            child = [rnd() if sim.rng.random() < mut else m for m in child]
            for _ in range(8):
                if sim.info == "fair" or tuple(child) not in fit:
                    break
                child = [rnd() if sim.rng.random() < max(mut, 0.25) else m for m in child]
            nxt.append(child)
        scored = sorted(((score(g), g) for g in nxt), key=lambda x: -x[0])
        stale = 0 if sim.steps > before else stale + 1
    # The move to play: the best sequence by its MEAN over every evaluation it got.
    best = max(fit.items(), key=lambda kv: (kv[1][1] / kv[1][0], kv[1][0]))[0]
    return best[0], [g for _, g in scored]


def run(seed, algo, budget, info, search_seed=0, log=None, data=None, sample_p=0.02, crn=0, reuse=False,
        leaf=None, leaf_depth=0, sims=0, c=None, value=None, tree_log=None, alpha=0.5):
    """One game on the surrogate driven by the agent. Returns (plan, final score, moves).

    `data` (an open text file) receives one JSON line per decision: the observation the models
    see (cora.observation.observe), the root's candidate turns as tool calls with their visit
    counts and mean final score, the turn played, a `sample_p` sample of rollout states with
    the score parts each led to, and -- once the game ends -- the game's own final score parts.
    `crn` (pool size, 0 = off) and `reuse` switch on common random numbers and tree reuse for
    MCTS and UCT.

    `leaf` (UCT only) replaces the rollout after the tree (uct_move / _leaf): "rules", "random",
    "model" (the decision model `value`), "partial" (the score so far) or "const"; leaf modes use
    min-max normalised Q with exploration constant `c`, and `sims` counts the budget in
    simulations instead of simulator steps. `alpha` (UCT only) is the progressive-widening exponent:
    a node visited n times holds ceil((n+1)**alpha) turns, so a lower alpha samples fewer turns per
    node and grows a deeper tree. `tree_log` (an open text file) receives one JSON line
    per decision: the turn played (as tags) and, for UCT, how its tree covered the game tree."""
    from cora import executor
    from cora.scoring import score_components
    from oracle.sim.env import SimEnv
    env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4, manual_transfers=False)
    env.reset()
    rng = random.Random(search_seed * 1_000_003 + seed)
    plan, moves, carry, mast = [], [], None, None
    records, tree = [], ({} if reuse else None)
    for d in range(DECISIONS):
        sim = Sim(env, d, info, rng)
        sim.crn = crn
        if data is not None:
            sim.samples, sim.sample_p = [], sample_p
        if tree_log is not None:
            sim.tree_info = {}
        t0 = time.time()
        calls0 = getattr(value, "calls", 0)
        if algo == "mcts":
            m = mcts_move(sim, budget, tree=tree)
        elif algo == "rhea":
            m, carry = rhea_move(sim, budget, carry)
        elif algo == "rules":
            m = "rules"
        elif algo.startswith("greedy"):  # greedy1 / greedy2 / greedy3: the best basket N decisions ahead
            calls, m = greedy_move(sim, horizon=int(algo[6:] or 1)), "basket"
        elif algo.startswith("uct"):     # uct / uct-noop (uniform turns), uct-f4 / uct-f6 (filtered)
            # suffix "e": empty turns allowed (0 menu actions, and the empty turn as a child)
            # uct-mast: filters 1-6 + empty turns, with MAST weights (kept for the whole game)
            f7, empty = algo.startswith("uct-f7"), algo.endswith("e") or algo == "uct-mast"
            if algo == "uct-mast" and mast is None:
                mast = Mast()
            gen = None
            if algo.startswith(("uct-f4", "uct-f6", "uct-f7", "uct-mast")):
                gen = (lambda e, r, s=not algo.startswith("uct-f4"):
                       _filtered_calls(e, r, strategy=s, empty=empty, premium=not f7, mast=mast))
            kw = {} if leaf is None else {"leaf": leaf, "leaf_depth": leaf_depth, "value": value,
                                          "c": 0.5 if c is None else c}
            if c is not None:
                kw["c"] = c
            calls = uct_move(sim, budget, "noop" if algo == "uct-noop" else "random", gen,
                             noop_child=empty, mast=mast, tree=tree, sims=sims, alpha=alpha, **kw)
            m = "basket"
        else:
            raise ValueError(algo)
        if m != "basket":
            calls = move_calls(env, d, m)
        plan.append([[t, a] for t, a in calls])
        moves.append(m)
        if data is not None:
            from cora.observation import observe
            root = [(move_calls(env, d, c) if isinstance(c, str) else c, n, tot) for c, n, tot in sim.root or []]
            records.append({"seed": seed, "algo": algo, "budget": budget, "info": info,
                            "search_seed": search_seed, "decision": d,
                            "obs": observe(env.game_state, env.valid_actions),
                            "parts_now": score_components(env.game_state.get("rewardMetrics")),
                            "root": [{"calls": [[t, a] for t, a in c], "n": n, "q": tot / n if n else None}
                                     for c, n, tot in root],
                            "chosen": [[t, a] for t, a in calls], "move": m,
                            "reuse": getattr(sim, "reuse", None), "samples": sim.samples})
        if tree_log is not None:
            info_t = sim.tree_info or {}
            info_t.update({"seed": seed, "algo": algo, "leaf": leaf, "decision": d, "steps": sim.steps,
                           "seconds": round(time.time() - t0, 2), "model_calls": getattr(value, "calls", 0) - calls0,
                           "played": turn_tags(env, calls)})
            tree_log.write(json.dumps(info_t) + "\n")
            tree_log.flush()
        _, (_, _, term, trunc, _) = executor.execute_turn(env, calls)
        if log:
            print(f"  d{d:02d} {m:9s} steps {sim.steps:6d} {time.time() - t0:5.1f}s "
                  f"score {score_components(env.game_state.get('rewardMetrics'))['score']:.4f}",
                  file=log, flush=True)
        if term or trunc:
            break
    final = score_components(env.game_state.get("rewardMetrics"))
    for r in records:
        r["final"] = final
        data.write(json.dumps(r) + "\n")
    return plan, final["score"], moves


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("seed", type=int)
    ap.add_argument("--algo", choices=["mcts", "rhea", "rules", "greedy1", "greedy2", "greedy3", "uct", "uct-noop", "uct-f4", "uct-f6", "uct-f6e", "uct-f7", "uct-f7e", "uct-mast"], default="mcts")
    ap.add_argument("--budget", type=int, default=2000, help="simulator steps per decision")
    ap.add_argument("--info", choices=["fair", "clairvoyant"], default="fair")
    ap.add_argument("--search-seed", type=int, default=0)
    ap.add_argument("--plan", help="write the realized calls (capture.py --calls format)")
    ap.add_argument("--quiet", action="store_true")
    ap.add_argument("--crn", type=int, default=0,
                    help="common random numbers: a pool of this many imagined worlds per decision (0 = off)")
    ap.add_argument("--reuse", action="store_true", help="keep the subtree under the move played (tree reuse)")
    ap.add_argument("--log-data", help="write training data (one JSON line per decision; .gz ok)")
    ap.add_argument("--sample-p", type=float, default=0.02, help="share of rollout turns logged")
    ap.add_argument("--leaf", choices=["rules", "random", "model", "partial", "const"],
                    help="UCT: what values a simulation once it leaves the tree (default: the algo's rollout)")
    ap.add_argument("--leaf-depth", type=int, default=0, help="sampled turns played before a model/partial/const leaf")
    ap.add_argument("--sims", type=int, default=0, help="budget in simulations per decision instead of steps")
    ap.add_argument("--c", type=float, help="UCT exploration constant (default 0.25; 0.5 with --leaf)")
    ap.add_argument("--dm-url", help="decision-model server for --leaf model (POST /v1/systemone)")
    ap.add_argument("--dm-variant", default="final", help="oracle.dm_leaf value question")
    ap.add_argument("--alpha", type=float, default=0.5,
                    help="UCT progressive widening: a node visited n times holds ceil((n+1)**alpha) turns")
    ap.add_argument("--tree-log", help="one JSON line per decision: the turn played and the tree's coverage")
    a = ap.parse_args(argv)
    value = None
    if a.leaf == "model":
        from bench.decision import Client
        from oracle.dm_leaf import LeafValue
        value = LeafValue(Client(a.dm_url, timeout=600), a.dm_variant)
    tlog = open(a.tree_log, "w") if a.tree_log else None
    t0 = time.time()
    import gzip
    data = (gzip.open(a.log_data, "wt") if a.log_data.endswith(".gz") else open(a.log_data, "w")) if a.log_data else None
    plan, score, moves = run(a.seed, a.algo, a.budget, a.info, a.search_seed,
                             None if a.quiet else sys.stderr, data, a.sample_p, a.crn, a.reuse,
                             a.leaf, a.leaf_depth, a.sims, a.c, value, tlog, a.alpha)
    if data:
        data.close()
    if tlog:
        tlog.close()
    out = {"seed": a.seed, "algo": a.algo, "budget": a.budget, "info": a.info,
           "search_seed": a.search_seed, "crn": a.crn, "reuse": a.reuse, "leaf": a.leaf,
           "leaf_depth": a.leaf_depth, "sims": a.sims, "c": a.c, "alpha": a.alpha, "dm_variant": a.dm_variant if a.leaf == "model" else None,
           "model_calls": getattr(value, "calls", None),
           "model_seconds": round(getattr(value, "seconds", 0.0), 1), "surrogate_score": round(score, 4), "moves": moves,
           "seconds": round(time.time() - t0, 1)}
    if a.plan:
        with open(a.plan, "w") as f:
            json.dump(plan, f)
    print(json.dumps(out))


if __name__ == "__main__":
    main(sys.argv[1:])
