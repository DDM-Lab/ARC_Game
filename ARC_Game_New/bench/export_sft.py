"""Export episodes to chat-format JSONL for finetuning (SFT / behavior cloning).

Benchmark episodes (python -m bench): one line per decision, in the OpenAI fine-tuning format with
tools — the system prompt, the exact user message the policy saw (rebuilt with the same function
the benchmark uses), the assistant's tool calls, and the tool schema:

    {"messages": [system, user, assistant{content, tool_calls}], "tools": [...], "meta": {...}}

Only typed tool calls are exported (LLM episodes, and baselines once they act through tools);
records whose calls are menu indices are skipped. Multi-turn history (--history > 1) is exported
one decision at a time.

Usage:
  python -m bench.export_sft <results_dir> [--out FILE] [--only-parsed] [--min-reward X]
                             [--min-episode-score X] [--with-reasoning]

  --only-parsed          keep only decisions whose calls were all well-formed
  --min-reward X         keep only decisions whose round reward >= X
  --min-episode-score X  keep only decisions from episodes whose final score >= X
  --with-reasoning       include the captured reasoning trace as reasoning_content

Live router sessions (--from-sessions) export the officers' turns instead.
"""
import argparse
import json
from pathlib import Path

from cora.observation import user_message
from cora.records import iter_episodes
from cora.tools import openai_tools


def _menu_decision(call):
    """A decision recorded as a menu index or a raw task/choice pair, not a tool call."""
    args = call.get("args") or {}
    return "index" in args or "taskId" in args


def _assistant_message(rd, with_reasoning):
    calls = rd.get("calls") or []
    msg = {"role": "assistant", "content": rd.get("raw") or None,
           "tool_calls": [{"id": c.get("id") or f"call_{i}", "type": "function",
                           "function": {"name": c["tool"], "arguments": json.dumps(c.get("args") or {})}}
                          for i, c in enumerate(calls)]}
    if with_reasoning and rd.get("reasoningTrace"):
        msg["reasoning_content"] = rd["reasoningTrace"]
    return msg


def export_episodes(args):
    out = Path(args.out) if args.out else Path(args.results_dir) / "sft.jsonl"
    n_ep = n_steps = n_kept = 0
    with open(out, "w") as ofh:
        for r in iter_episodes(args.results_dir):
            rounds = r.get("rounds") or []
            if r.get("error") or not rounds:
                continue
            n_ep += 1
            score = (r.get("summary") or {}).get("finalScore")
            if args.min_episode_score is not None and (score is None or score < args.min_episode_score):
                continue
            tools = openai_tools(manual_transfers=r.get("transfers") == "manual")
            encoding = r.get("obs_encoding") or "compact"
            prev = None
            for rd in rounds:
                n_steps += 1
                obs, prev_obs = rd.get("obs"), prev
                prev = obs
                if obs is None or any(_menu_decision(c) for c in rd.get("calls") or []):
                    continue                    # no observation, or menu decisions (not tool calls)
                if args.only_parsed and rd.get("parsed_ok") is False:
                    continue
                if args.min_reward is not None and (rd.get("reward") is None or rd["reward"] < args.min_reward):
                    continue
                ofh.write(json.dumps({
                    "messages": [
                        {"role": "system", "content": r.get("system_prompt", "")},
                        {"role": "user", "content": user_message(obs, encoding, prev_obs)},
                        _assistant_message(rd, args.with_reasoning),
                    ],
                    "tools": tools,
                    "meta": {"model": r["model"], "policy": r.get("policy"), "episode": r["episode"],
                             "seed": r.get("seed"), "round": rd["r"], "reward": rd.get("reward"),
                             "parsed_ok": rd.get("parsed_ok"), "episode_score": score,
                             "prompt_sha": r.get("prompt_sha"),
                             "calls": [{"tool": c.get("tool"), "status": c.get("status")}
                                       for c in rd.get("calls") or []]},
                }) + "\n")
                n_kept += 1
    print(f"{n_ep} episodes, {n_steps} decisions seen, {n_kept} written -> {out}")


# ── Live-session (router) corpus ─────────────────────────────────────────────
# The benchmark path above consumes the benchmark's episodes.jsonl. LIVE games played
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
        description="Export CORA episodes to chat-format JSONL for SFT. Two sources: a benchmark "
                    "results dir (default), or live router session logs (--from-sessions), i.e. "
                    "what GET /my/sessions/export returns.")
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
    ap.add_argument("--min-episode-score", type=float, default=None)
    ap.add_argument("--with-reasoning", action="store_true")
    args = ap.parse_args()
    if args.source:
        export_sessions(args)
    elif args.results_dir:
        export_episodes(args)
    else:
        ap.error("give a benchmark results_dir, or --from-sessions <file|dir|tar.gz>")


if __name__ == "__main__":
    main()
