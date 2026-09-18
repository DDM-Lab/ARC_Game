#!/usr/bin/env python3
"""Per-turn diagnostic: is a weak policy failing because of the OBSERVATION or the MODEL?

The episode-level benchmark answers "how well did it score", which cannot separate
"it never understood the state" from "it understood and chose badly". This harness
answers the narrower question by pairing the two policies AT THE SAME STATE.

DESIGN

A reference policy (default `combined`, the strongest non-learning baseline) DRIVES the
episode. At every turn, before the reference action is executed, the LLM is queried as a
SHADOW on the identical env — same round, same budget, same facilities, byte-identical
observation. Nothing the shadow says is executed, so the trajectory is exactly the
reference policy's and the comparison is paired at the state.

That pairing is what makes the diagnosis possible: the two policies never drift into
different situations, so any disagreement is attributable to the decision, not to
having reached a different game.

Both baseline policies are pure readers of env.game_state / env.valid_actions (verified:
every list they append to is local), so shadow-querying cannot perturb the episode.

WHAT IT MEASURES, per turn

  available      which action types are legal right now (from env.get_valid_actions()
                 plus any active tasks) -- so "never used `train`" can be separated from
                 "`train` was never available"
  ref            the reference policy's action set
  llm            the shadow's action set, sampled N times to expose sampling variance
  resolvable     did the shadow's tool calls resolve to executable actions (indices in
                 range / valid task+choice ids). This is the ACTION-VALIDITY signal and
                 needs no execution -- parse_commands resolves against the live env.
  agreement      exact (same action types AND targets) / category (same action types)

THE DISCRIMINATOR

  MODEL problem       the shadow reliably produces resolvable actions but picks
                      different ones than the reference, in BOTH encodings.
  OBSERVATION problem the shadow's resolvable-rate or agreement moves when the same
                      state is re-encoded (--encodings compact,json). Same model, same
                      state, same sampler: only the rendering changed.

Run the SAME states under both encodings to make that an ablation rather than an
anecdote. `--encodings compact,json` re-queries every state once per encoding.

USAGE
  ./.venv/bin/python probe_actions.py --model qwen3:4b --rounds 6 --samples 1 \
      --encodings compact --out probe_smoke            # harness smoke test (~7 min)

  ./.venv/bin/python probe_actions.py --model qwen3:4b --rounds 16 --samples 3 \
      --encodings compact,json --out probe_full        # the experiment

Emits <out>/turns.jsonl (one record per turn) and prints a per-action-type summary.
"""
import argparse
import json
import os
import sys
import time
from pathlib import Path

import openai

import benchmark_models as bm
import llm_smoke_test as smoke
from arc_game_gym_env_tcp import ARCGameGymEnv


def action_types_available(acts_enum, state):
    """The set of action types legal this turn, from the enumerated valid actions plus
    active tasks. Without this, 'the model never called train' is unreadable -- it may
    simply never have been offered."""
    types = {a.get("action_type") or "?" for a in acts_enum}
    if state.get("tasks"):
        types.add("task")
    return types


def decision_signature(dec, acts_enum):
    """Canonical, order-insensitive form of a decision, for comparing two policies.

    Returns (exact, category):
      exact    frozenset of (action_type, target) -- target is the enumerated action's
               own identity, so two policies agree only if they picked the same thing.
      category frozenset of action_type -- the weaker 'same KIND of move' comparison.
    """
    exact, cats = set(), set()
    for a in dec.get("actions", []) or []:
        try:
            i = int(a)
        except (TypeError, ValueError):
            continue
        if 0 <= i < len(acts_enum):
            e = acts_enum[i]
            at = e.get("action_type") or "?"
            tgt = e.get("building_name") or e.get("target") or e.get("site_id") or e.get("description") or ""
            exact.add((at, str(tgt)[:40]))
            cats.add(at)
    for c in dec.get("choices", []) or []:
        try:
            exact.add(("task", f"{int(c['taskId'])}/{int(c['choiceId'])}"))
            cats.add("task")
        except (TypeError, ValueError, KeyError):
            pass
    return frozenset(exact), frozenset(cats)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--model", default="qwen3:4b")
    ap.add_argument("--rounds", type=int, default=16)
    ap.add_argument("--samples", type=int, default=1, help="LLM queries per state per encoding")
    ap.add_argument("--encodings", default="compact", help="comma list: compact,json,delta")
    ap.add_argument("--reference", default="combined",
                    choices=["combined", "greedy", "build-potential", "choice-lookahead"])
    ap.add_argument("--base-url", default="http://localhost:11434/v1")
    ap.add_argument("--api-key", default="ollama")
    ap.add_argument("--max-tokens", type=int, default=6000)
    ap.add_argument("--reasoning-effort", default="none")
    ap.add_argument("--system-prompt", default="minimal")
    ap.add_argument("--transfers", default="task_only", choices=["manual", "task_only"])
    ap.add_argument("--port", type=int, default=9990)
    ap.add_argument("--out", default="probe_out")
    a = ap.parse_args()

    encodings = [e.strip() for e in a.encodings.split(",") if e.strip()]
    outdir = Path(a.out)
    outdir.mkdir(parents=True, exist_ok=True)

    # Local-server knobs: without these the tools path silently uses its own 2000-token
    # cap and lets Ollama auto-enable thinking, which truncates this model mid-prose
    # before it ever emits a tool call.
    bm._set_local_reasoning_effort(a.reasoning_effort)
    bm._set_local_max_tokens(a.max_tokens)
    client = openai.OpenAI(api_key=a.api_key, base_url=a.base_url)

    manual_transfers = (a.transfers == "manual")
    env = ARCGameGymEnv(unity_exe_path=bm.HEADLESS_EXE, unity_port=a.port,
                        auto_start_unity=True, max_episode_steps=a.rounds + 5,
                        unity_log_path=str(outdir / "unity.log"),
                        manual_transfers=manual_transfers)
    # Exact function names as dispatched in benchmark_models.run_episode, so the reference
    # here is the same policy the episode benchmark scores.
    ref_fn = {"combined": lambda e, r: bm.combined_decision(e, r, a.rounds),
              "greedy": lambda e, r: bm.greedy_decision(e),
              "build-potential": lambda e, r: bm.potential_decision(e, r, a.rounds),
              "choice-lookahead": lambda e, r: bm.improved_rules_based_decision(e, r, a.rounds)}[a.reference]

    turns_path = outdir / "turns.jsonl"
    fout = open(turns_path, "w")
    t0 = time.time()
    try:
        env.reset()
        for rnd in range(a.rounds):
            acts_enum = env.get_valid_actions()
            state = smoke.summarize_commands(env, show_impacts=True, rounds_left=a.rounds - rnd)
            avail = action_types_available(acts_enum, state)

            ref_dec = ref_fn(env, rnd)
            ref_exact, ref_cat = decision_signature(ref_dec, acts_enum)

            samples = []
            for enc in encodings:
                for s in range(a.samples):
                    t1 = time.time()
                    try:
                        dec, raw, _, _, parsed_ok = bm.ask_tools(
                            client, a.model, state, env, None, "none", a.reasoning_effort,
                            a.system_prompt, None, enc, None, None)
                        err = None
                    except Exception as e:                      # network/API failure only
                        dec, raw, parsed_ok, err = {}, "", False, str(e)[:200]
                    ex, ct = decision_signature(dec, acts_enum)
                    samples.append({
                        "encoding": enc, "sample": s, "secs": round(time.time() - t1, 1),
                        "error": err,
                        "n_actions": len(dec.get("actions") or []),
                        "n_choices": len(dec.get("choices") or []),
                        # resolvable = the tool calls became executable actions against the
                        # LIVE env. This is action-validity WITHOUT executing anything.
                        "resolvable": bool(ex),
                        "parsed_ok": bool(parsed_ok),
                        "cmd_errors": dec.get("errors") or [],
                        "note": (dec.get("note") or "")[:120],
                        "cats": sorted(ct),
                        "exact_match_ref": (ex == ref_exact),
                        "cat_match_ref": (ct == ref_cat),
                        "cat_overlap_ref": sorted(ct & ref_cat),
                        "raw_len": len(raw or ""),
                    })

            rec = {"round": rnd, "budget": env.game_state.get("budget"),
                   "available": sorted(avail), "n_valid": len(acts_enum),
                   "ref_note": (ref_dec.get("note") or "")[:80],
                   "ref_cats": sorted(ref_cat), "ref_exact": sorted(f"{t}:{g}" for t, g in ref_exact),
                   "samples": samples}
            fout.write(json.dumps(rec) + "\n"); fout.flush()
            done = sum(1 for s in samples if s["resolvable"])
            print(f"r{rnd:>2} avail={sorted(avail)} ref={sorted(ref_cat)} "
                  f"llm_resolvable={done}/{len(samples)} "
                  f"({time.time()-t0:.0f}s)", flush=True)

            # Execute the REFERENCE decision so the trajectory stays the reference policy's.
            for c in ref_dec.get("choices", []) or []:
                try:
                    env.select_task_choice(int(c["taskId"]), int(c["choiceId"]))
                except Exception:
                    pass
            req = [int(x) for x in (ref_dec.get("actions") or []) if str(x).lstrip("-").isdigit()]
            _, _, term, trunc, _ = env.step(",".join(str(x) for x in req))
            if term or trunc:
                print(f"episode ended at r{rnd}")
                break
    finally:
        fout.close()
        try:
            env.close()
        except Exception:
            pass
    summarize(turns_path)


def summarize(turns_path):
    """Per-action-type availability vs use, and agreement -- split by encoding so the
    encoding column IS the observation-vs-model ablation."""
    import collections
    recs = [json.loads(l) for l in open(turns_path)]
    if not recs:
        print("no turns recorded")
        return
    print("\n" + "=" * 78)
    print(f"{len(recs)} turns")

    by_enc = collections.defaultdict(list)
    for r in recs:
        for s in r["samples"]:
            by_enc[s["encoding"]].append((r, s))

    print(f"\n{'encoding':<10} {'n':>4} {'resolvable':>11} {'exact=ref':>10} {'cat=ref':>8} "
          f"{'cat overlap':>12} {'errs/turn':>10} {'med chars':>10}")
    for enc, pairs in sorted(by_enc.items()):
        n = len(pairs)
        res = sum(1 for _, s in pairs if s["resolvable"])
        ex = sum(1 for _, s in pairs if s["exact_match_ref"])
        ct = sum(1 for _, s in pairs if s["cat_match_ref"])
        ov = sum(len(s["cat_overlap_ref"]) for _, s in pairs)
        er = sum(len(s["cmd_errors"]) for _, s in pairs)
        lens = sorted(s["raw_len"] for _, s in pairs)
        print(f"{enc:<10} {n:>4} {res/n:>10.0%} {ex/n:>9.0%} {ct/n:>7.0%} "
              f"{ov/n:>11.2f} {er/n:>9.2f} {lens[n//2]:>10}")

    # Availability vs use: the key per-action table. A type the model never used is only
    # interesting if it was actually offered.
    print(f"\n{'action type':<16} {'turns avail':>12} {'ref used':>9} {'llm used':>9} {'llm/avail':>10}")
    avail_c, ref_c, llm_c = collections.Counter(), collections.Counter(), collections.Counter()
    for r in recs:
        for t in r["available"]:
            avail_c[t] += 1
        for t in r["ref_cats"]:
            ref_c[t] += 1
        used = set()
        for s in r["samples"]:
            used |= set(s["cats"])
        for t in used:
            llm_c[t] += 1
    for t in sorted(avail_c, key=lambda x: -avail_c[x]):
        av = avail_c[t]
        print(f"{t:<16} {av:>12} {ref_c[t]:>9} {llm_c[t]:>9} {llm_c[t]/av:>9.0%}")

    unused = [t for t in avail_c if llm_c[t] == 0]
    if unused:
        print(f"\n  NEVER used despite being available: {unused}")


if __name__ == "__main__":
    main()
