#!/usr/bin/env python3
"""Read a benchmark episode transcript round by round.

Every benchmark run writes ONE episodes.jsonl per model, e.g.

    benchmark_results/oss_wide_v3_0814/qwen3_4b/episodes.jsonl

Each LINE is one episode (a JSON object). The per-round record lives in
`rounds[]`, and the fields that matter when you're debugging a policy are:

    raw          the model's full response text, verbatim  <- the transcript
    obs          the state dict actually sent to the model that round
    actCats      action categories the engine accepted
    cmdErrors    per-command rejection reasons
    reasoningTrace / reasoningTokens
                 hidden chain-of-thought, when the provider surfaces it
                 (local "thinking" models; None when thinking is off)

Usage
-----
  python view_transcript.py <episodes.jsonl>                 # summary of all rounds
  python view_transcript.py <path> --round 4                 # full text of one round
  python view_transcript.py <path> --round 4 --episode 2
  python view_transcript.py <path> --tags                    # where each command tag sits
  python view_transcript.py <path> --errors                  # only rounds with rejections
  python view_transcript.py <path> --obs --round 4           # also dump the observation

--tags is the one to reach for when commands execute more times than expected:
it prints each tag's position as a % through the response plus the text leading
into it, which distinguishes a real emission from the model merely quoting a
command mid-deliberation.
"""
import argparse
import json
import re
import sys

TAG_RE = re.compile(r"<(\w+)>([^<]*)</\1>")


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
            errs = r.get("cmdErrors") or []
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
            print(f"\n--- ACCEPTED: {r.get('actCats')}")
            for x in (r.get("cmdErrors") or []):
                print(f"--- REJECTED: {x}")


def tags(eps, rnd):
    for i, e in enumerate(eps):
        for r in e.get("rounds", []):
            if rnd is not None and r.get("r") != rnd:
                continue
            raw = r.get("raw") or ""
            found = list(TAG_RE.finditer(raw))
            if not found:
                continue
            print(f"\n--- episode {i} round {r.get('r')}  ({len(raw)} chars, {len(found)} tags)")
            for m in found:
                pct = 100 * m.start() / max(len(raw), 1)
                lead = raw[max(0, m.start() - 55):m.start()].replace("\n", " ")
                print(f"  {pct:5.1f}%  {m.group(0)[:36]:<38} ...{lead[-46:]!r}")


def errors(eps):
    for i, e in enumerate(eps):
        for r in e.get("rounds", []):
            errs = r.get("cmdErrors") or []
            if not errs:
                continue
            print(f"\n--- episode {i} round {r.get('r')}  accepted={r.get('actCats')}")
            for x in errs:
                print(f"    {x[:160]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("path", help="path to an episodes.jsonl")
    ap.add_argument("--episode", type=int, default=None, help="0-indexed; default all")
    ap.add_argument("--round", type=int, default=None)
    ap.add_argument("--tags", action="store_true", help="show each tag's position in the response")
    ap.add_argument("--errors", action="store_true", help="only rounds with rejected commands")
    ap.add_argument("--obs", action="store_true", help="also print the observation sent")
    a = ap.parse_args()

    eps = pick(load(a.path), a.episode)
    if not eps:
        sys.exit("no episodes found (wrong path, or --episode out of range)")

    if a.tags:
        tags(eps, a.round)
    elif a.errors:
        errors(eps)
    elif a.round is not None:
        one_round(eps, a.round, a.obs)
    else:
        summary(eps)


if __name__ == "__main__":
    main()
