#!/usr/bin/env python3
"""Read a benchmark episode transcript round by round.

Every benchmark run writes ONE episodes.jsonl per model, e.g.

    benchmark_results/oss_wide_v3_0814/qwen3_4b/episodes.jsonl

Each LINE is one episode (a JSON object). The per-round record lives in
`rounds[]`, and the fields that matter when you're debugging a policy are:

    raw          the model's response text (reasoning before its tool calls), verbatim
    calls        every tool call and its outcome: executed / refused / invalid, with the reason
    obs          the state dict actually sent to the model that round
    actCats      action categories the engine accepted
    reasoningTrace / reasoningTokens
                 hidden chain-of-thought, when the provider surfaces it
                 (local "thinking" models; None when thinking is off)

Usage
-----
  python view_transcript.py <episodes.jsonl>                 # summary of all rounds
  python view_transcript.py <path> --round 4                 # full text of one round
  python view_transcript.py <path> --round 4 --episode 2
  python view_transcript.py <path> --calls                   # every tool call and its outcome
  python view_transcript.py <path> --errors                  # only rounds with refused/invalid calls
  python view_transcript.py <path> --obs --round 4           # also dump the observation

"""
import argparse
import json
import sys


def load(path):
    eps = []
    with open(path) as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    eps.append(json.loads(line))
                except json.JSONDecodeError:
                    pass
    return eps


def pick(eps, want):
    """Episodes are 0-indexed in file order; None = all."""
    return eps if want is None else [eps[want]] if want < len(eps) else []


def summary(eps):
    for i, e in enumerate(eps):
        err = e.get("error")
        print(f"\n=== episode {i}  model={e.get('model')}  "
              f"effort={e.get('reasoning_effort')}  max_tokens={e.get('max_tokens')}"
              + (f"  ERROR={str(err).splitlines()[0][:60]}" if err else ""))
        print(f"{'rnd':>4} {'chars':>7} {'rTok':>6}  {'actions':<34} errors")
        for r in e.get("rounds", []):
            raw = r.get("raw") or ""
            cats = r.get("actCats") or {}
            errs = _failed(r)
            print(f"{str(r.get('r')):>4} {len(raw):>7} {str(r.get('reasoningTokens') or '-'):>6}  "
                  f"{str(cats)[:34]:<34} {len(errs)}")


def one_round(eps, rnd, show_obs):
    for i, e in enumerate(eps):
        for r in e.get("rounds", []):
            if r.get("r") != rnd:
                continue
            print(f"\n{'='*72}\nepisode {i}  round {rnd}  model={e.get('model')}\n{'='*72}")
            if show_obs:
                obs = r.get("obs")
                obs = json.loads(obs) if isinstance(obs, str) else obs
                print("--- OBSERVATION SENT ---")
                print(json.dumps(obs, indent=1)[:4000])
            trace = r.get("reasoningTrace")
            if trace:
                print(f"\n--- HIDDEN REASONING ({r.get('reasoningTokens')} tokens) ---")
                print(trace)
            print("\n--- RESPONSE ---")
            print(r.get("raw") or "(empty)")
            print("\n--- CALLS ---")
            for c in r.get("calls") or []:
                print(f"  {_call_line(c)}")


def _call_line(c):
    args = ", ".join(f"{k}={v}" for k, v in (c.get("args") or {}).items())
    reason = f"  ({c['reason']})" if c.get("reason") else ""
    return f"{c.get('status', '?'):<9} {c.get('tool')}({args}){reason}"


def _failed(r):
    return [c for c in r.get("calls") or [] if c.get("status") != "executed"]


def calls(eps, rnd):
    for i, e in enumerate(eps):
        for r in e.get("rounds", []):
            if rnd is not None and r.get("r") != rnd:
                continue
            print(f"\n--- episode {i} round {r.get('r')}  reward {r.get('reward')}")
            for c in r.get("calls") or []:
                print(f"  {_call_line(c)}")
            if not r.get("calls"):
                print("  (no calls)")


def show_prompt(eps):
    """Print the stored system prompt as readable text.

    episodes.jsonl holds it as a single JSON string, so reading it raw means scrolling one
    enormous line of literal \\n -- unusable for reviewing what a model was actually told.
    This renders the newlines, numbers the lines, and marks the blank-line section breaks so
    the prompt's structure (rules / HOW TO ACT / grammar) is visible at a glance.
    """
    e = eps[0]
    txt = e.get("system_prompt") or ""
    if not txt:
        print("no system_prompt stored on this episode")
        return
    import hashlib
    print("=" * 78)
    print(f"model         : {e.get('model')}")
    print(f"variant       : {e.get('system_variant')}   pack: {e.get('prompt_pack')}")
    print(f"prompt_sha    : {e.get('prompt_sha')}  (recomputed: "
          f"{hashlib.sha1(txt.encode()).hexdigest()[:12]})")
    print(f"action_format : {e.get('action_format')}   obs_encoding: {e.get('obs_encoding')}")
    print(f"length        : {len(txt)} chars, {len(txt.splitlines())} lines")
    print("=" * 78)
    sec = 1
    prev_blank = True
    for i, line in enumerate(txt.splitlines(), 1):
        if line.strip() and prev_blank:
            print(f"\n  ── section {sec} " + "─" * 52)
            sec += 1
        prev_blank = not line.strip()
        print(f"  {i:>3} | {line}")
    print("=" * 78)


def errors(eps):
    for i, e in enumerate(eps):
        for r in e.get("rounds", []):
            errs = _failed(r)
            if not errs:
                continue
            print(f"\n--- episode {i} round {r.get('r')}  accepted={r.get('actCats')}")
            for c in errs:
                print(f"    {_call_line(c)[:160]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("path", help="path to an episodes.jsonl")
    ap.add_argument("--episode", type=int, default=None, help="0-indexed; default all")
    ap.add_argument("--round", type=int, default=None)
    ap.add_argument("--calls", action="store_true", help="every tool call and its outcome")
    ap.add_argument("--errors", action="store_true", help="only rounds with refused/invalid calls")
    ap.add_argument("--obs", action="store_true", help="also print the observation sent")
    ap.add_argument("--prompt", action="store_true",
                    help="print the stored system prompt as TEXT, with newlines rendered and "
                         "sections numbered, instead of the one-line JSON blob")
    a = ap.parse_args()

    eps = pick(load(a.path), a.episode)
    if not eps:
        sys.exit("no episodes found (wrong path, or --episode out of range)")

    if a.prompt:
        show_prompt(eps)
    elif a.calls:
        calls(eps, a.round)
    elif a.errors:
        errors(eps)
    elif a.round is not None:
        one_round(eps, a.round, a.obs)
    else:
        summary(eps)


if __name__ == "__main__":
    main()
