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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("results_dir")
    ap.add_argument("--out", default=None)
    ap.add_argument("--only-parsed", action="store_true")
    ap.add_argument("--min-reward", type=float, default=None)
    ap.add_argument("--min-episode-reward", type=float, default=None)
    args = ap.parse_args()

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
