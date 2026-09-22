"""
Export benchmark episodes to chat-format JSONL for offline finetuning / SFT.

Each saved round is a complete (prompt -> completion) example: the system prompt,
the exact user observation the model saw, and the model's full response. This
reconstructs them into OpenAI-style chat messages, one line per step.

Usage:
  python export_sft.py <results_dir> [--out FILE] [--only-parsed] [--min-reward X]
                       [--min-episode-reward X]

  --only-parsed         keep only steps whose response parsed as valid JSON
  --min-reward X        keep only steps with per-step reward >= X
  --min-episode-reward  keep only steps from episodes whose total reward >= X
                        (behavior-cloning on the better trajectories)

Output line: {"messages":[{role,content}x3], "meta":{model,episode,round,reward,...}}
"""
import json, argparse
from pathlib import Path


def user_content(obs):
    # must match benchmark_models.ask(): "State:\n" + json(state) + "\n\nJSON decision:"
    return "State:\n" + json.dumps(obs) + "\n\nJSON decision:"


# ── Live-session (router) corpus ─────────────────────────────────────────────
# The benchmark path above consumes benchmark_models' episodes.jsonl. LIVE games played
# through the router produce a different artifact: per-session JSONL event logs, pulled
# with `GET /my/sessions/export` (ndjson or tar.gz). Their training-relevant records are
# `agent_turn` rows — one per officer turn, carrying the officer's own filtered
# observation (`subobservation`) and its full response (`llm_raw_response`).

def _iter_session_records(src: Path):
    """Yield JSON records from a session .jsonl, a directory of them, or a .tar.gz
    (i.e. exactly what /my/sessions/export returns in either format)."""
    def _lines(text, origin):
        for line in text.splitlines():
            line = line.strip()
            if not line:
                continue
            try:
                yield json.loads(line), origin
            except json.JSONDecodeError:
                continue

    if src.is_dir():
        for p in sorted(src.rglob("*.jsonl")):
            yield from _lines(p.read_text(), p.name)
    elif src.suffix in (".gz", ".tgz") or "".join(src.suffixes[-2:]) == ".tar.gz":
        import tarfile
        with tarfile.open(src, "r:*") as tf:
            for m in tf.getmembers():
                if not m.isfile() or not m.name.endswith(".jsonl"):
                    continue
                f = tf.extractfile(m)
                if f is None:
                    continue
                yield from _lines(f.read().decode("utf-8", "replace"), m.name)
    else:
        yield from _lines(src.read_text(), src.name)


def _is_turn(rec):
    """An officer turn record. New logs are typed `agent_turn`; older ones predate that
    stamp, so fall back to the structural signature."""
    if rec.get("event_type") == "agent_turn":
        return True
    return rec.get("event_type") is None and "subobservation" in rec and "llm_raw_response" in rec


def export_sessions(args):
    src = Path(args.source)
    out = Path(args.out) if args.out else src.parent / "sft_sessions.jsonl"
    n_rec = n_turn = n_kept = 0
    agents = {}
    with open(out, "w") as ofh:
        for rec, origin in _iter_session_records(src):
            n_rec += 1
            if not _is_turn(rec):
                continue
            n_turn += 1
            raw = (rec.get("llm_raw_response") or "").strip()
            obs = rec.get("subobservation")
            if not raw or not obs:
                continue          # nothing to learn from a turn with no response/observation
            if args.agent and rec.get("agent_name") != args.agent:
                continue
            reward = rec.get("reward")
            if args.min_reward is not None and (reward is None or reward < args.min_reward):
                continue
            name = rec.get("agent_name") or "Officer"
            agents[name] = agents.get(name, 0) + 1
            # NOTE: the exact system prompt is NOT stored per turn, so this reconstructs a
            # minimal role line. For prompt-faithful SFT, prepend the officer's real system
            # prompt from its config (global_prompt_config.json + the agent's system_prompt).
            ofh.write(json.dumps({
                "messages": [
                    {"role": "system", "content": f"You are the {name}."},
                    {"role": "user", "content": json.dumps(obs)},
                    {"role": "assistant", "content": raw},
                ],
                "meta": {"session_id": rec.get("session_id"), "episode_id": rec.get("episode_id"),
                         "agent": name, "actor_type": rec.get("actor_type"),
                         "round": rec.get("round"), "day": rec.get("day"),
                         "reward": reward, "source": origin,
                         "actions_attempted": rec.get("total_actions_attempted"),
                         "actions_succeeded": rec.get("successful_actions")},
            }) + "\n")
            n_kept += 1
    print(f"{n_rec} records seen, {n_turn} officer turns, {n_kept} written -> {out}")
    if agents:
        print("  per-officer:", ", ".join(f"{k}={v}" for k, v in sorted(agents.items())))
    if n_turn and not n_kept:
        print("  (no turns kept — check --min-reward/--agent, or the turns had empty responses)")


def main():
    ap = argparse.ArgumentParser(
        description="Export CORA rollouts to chat-format JSONL for SFT. Two sources: a "
                    "benchmark results dir (default), or live router session logs "
                    "(--from-sessions), i.e. what GET /my/sessions/export returns.")
    ap.add_argument("results_dir", nargs="?", default=None,
                    help="benchmark results dir containing episodes.jsonl")
    ap.add_argument("--from-sessions", dest="source", default=None,
                    help="live-session source: a .jsonl, a directory of them, or a .tar.gz "
                         "(as returned by GET /my/sessions/export?format=tar)")
    ap.add_argument("--agent", default=None,
                    help="with --from-sessions: keep only this officer's turns")
    ap.add_argument("--out", default=None)
    ap.add_argument("--only-parsed", action="store_true")
    ap.add_argument("--min-reward", type=float, default=None)
    ap.add_argument("--min-episode-reward", type=float, default=None)
    args = ap.parse_args()

    if args.source:
        export_sessions(args)
        return
    if not args.results_dir:
        ap.error("give a benchmark results_dir, or --from-sessions <file|dir|tar.gz>")

    src = Path(args.results_dir) / "episodes.jsonl"
    out = Path(args.out) if args.out else Path(args.results_dir) / "sft.jsonl"
    n_steps = n_kept = n_ep = 0
    with open(src) as fh, open(out, "w") as ofh:
        for line in fh:
            r = json.loads(line)
            rounds = r.get("rounds") or []
            if not rounds:
                continue
            n_ep += 1
            sys_prompt = r.get("system_prompt", "")
            ep_reward = (r.get("summary") or {}).get("totalReward")
            if args.min_episode_reward is not None and (ep_reward is None or ep_reward < args.min_episode_reward):
                continue
            for rd in rounds:
                n_steps += 1
                if "obs" not in rd or not rd.get("raw"):
                    continue  # pre-finetuning-logging records lack obs/full raw
                if args.only_parsed and not rd.get("parsed_ok"):
                    continue
                if args.min_reward is not None and (rd.get("reward") is None or rd["reward"] < args.min_reward):
                    continue
                ofh.write(json.dumps({
                    "messages": [
                        {"role": "system", "content": sys_prompt},
                        {"role": "user", "content": user_content(rd["obs"])},
                        {"role": "assistant", "content": rd["raw"]},
                    ],
                    "meta": {"model": r["model"], "episode": r["episode"], "round": rd["r"],
                             "reward": rd.get("reward"), "parsed_ok": rd.get("parsed_ok"),
                             "episode_reward": ep_reward, "condition": "impacts" if r.get("show_impacts") else "no_impacts"},
                }) + "\n")
                n_kept += 1
    print(f"{n_ep} episodes, {n_steps} steps seen, {n_kept} steps written -> {out}")


if __name__ == "__main__":
    main()
