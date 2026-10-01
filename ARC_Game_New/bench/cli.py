"""Benchmark CLI: python -m bench [options].

Runs N full games per model (or per baseline policy), each in a fresh headless Unity process, and
writes <out>/episodes.jsonl (one record per game) and <out>/summary.json. Episodes run
concurrently across --workers, each on its own port.

    python -m bench --models qwen3:4b --base-url http://127.0.0.1:11434/v1 --episodes 4 --seed 7000
    python -m bench --policy combined --episodes 8 --seed 7000
    python -m bench --validate          # one 2-decision no-op game: checks the build and ports
"""
from __future__ import annotations

import argparse
import json
import os
import queue
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

from bench.baselines import POLICIES
from bench.episode import RunConfig, run_episode
from bench.images import MAP_GRID_JSON
from bench.llm import LocalOptions
from bench.results import aggregate, log_wandb, print_table
from cora import prompts as cora_prompts
from cora.env.unity_process import default_exe
from cora.llm import Provider, ProviderSpec, client_for

BASE_PORT = 9900

# Cross-vendor flagships available on the CMU gateway (edit via --models).
DEFAULT_MODELS = [
    "us.anthropic.claude-opus-4-8",
    "us.anthropic.claude-sonnet-4-6",
    "gpt-5.5",
    "gpt-5.4-pro",
    "gemini/gemini-3.1-pro-preview",
    "gemini-2.5-pro",
]


def parse_args(argv=None):
    ap = argparse.ArgumentParser(prog="python -m bench", description=__doc__.split("\n\n")[0])
    ap.add_argument("--policy", choices=["llm", "noop", *POLICIES], default="llm",
                    help="llm = benchmark the --models; otherwise a baseline (no API): "
                         + ", ".join(POLICIES) + "; noop does nothing")
    ap.add_argument("--models", default=",".join(DEFAULT_MODELS))
    ap.add_argument("--episodes", type=int, default=20)
    ap.add_argument("--rounds", type=int, default=40,
                    help="decision cap per game; a full game is 36 decisions and ends on its own")
    ap.add_argument("--workers", type=int, default=1)
    ap.add_argument("--seed", type=int, default=None,
                    help="base seed; episode i plays seed+i, so two runs with one --seed play the same "
                         "scenarios (paired comparisons). Omit for unseeded games.")
    ap.add_argument("--out", default="benchmark_results")
    ap.add_argument("--validate", action="store_true")
    # prompt and observation
    ap.add_argument("--prompt", default=cora_prompts.DEFAULT_PACK,
                    help="prompt pack: a name in prompts/ (" + ", ".join(cora_prompts.list_packs())
                         + ") or a path to a pack JSON; recorded per episode with its prompt_sha")
    ap.add_argument("--ablate", default="",
                    help="rule ablation of the rendered prompt: R07 | R07,R09 | R07_P1_direct "
                         "(cora/prompt_ablation.py)")
    ap.add_argument("--no-impacts", dest="impacts", action="store_false",
                    help="hide task choices' impacts from the observation (ablation)")
    ap.add_argument("--obs_encoding", choices=["json", "compact", "delta"], default="compact",
                    help="compact = the text observation (default); json = the observation dict as "
                         "JSON; delta = compact with the facilities block diffed against the previous "
                         "turn (for --history > 1; at K=1 it is compact)")
    ap.add_argument("--history", type=int, default=1,
                    help="turns the model sees including the current one; 1 = stateless")
    ap.add_argument("--image_mode", choices=["none", "synthetic", "real"], default="none",
                    help="decision-time image: synthetic dashboard, or a real frame (render build)")
    ap.add_argument("--transfers", choices=["manual", "task_only"], default="task_only",
                    help="task_only (default) = transfers only through task choices, as in the GUI; "
                         "manual = standalone transfer tool as well")
    ap.add_argument("--map-config", default=None,
                    help="map JSON for every game, or 'none' for the scene's built-in layout "
                         "(default: the build's pinned map)")
    # model endpoint and sampling
    ap.add_argument("--base-url", default=None,
                    help="OpenAI-compatible endpoint (vLLM, Ollama); default = the CMU gateway")
    ap.add_argument("--api-key", default=None,
                    help="key for --base-url (or set ARC_API_KEY, which `ps` does not show)")
    ap.add_argument("--temperature", type=float, default=None,
                    help="sampling temperature (clamped to 1.0 for Anthropic models); default = vendor's")
    ap.add_argument("--reasoning_effort", choices=["none", "low", "medium", "high", "xhigh"], default="low",
                    help="local servers (--base-url) only: forwarded as reasoning_effort; 'none' "
                         "turns thinking off on Ollama reasoning models")
    ap.add_argument("--max_tokens", type=int, default=None,
                    help="local servers only: total generation budget (reasoning + answer)")
    ap.add_argument("--no-thinking", action="store_true",
                    help="local servers only: chat_template_kwargs enable_thinking=false (Qwen "
                         "templates move the reasoning out of the visible content)")
    ap.add_argument("--base-port", type=int, default=BASE_PORT,
                    help="first gym port; workers use base..base+workers-1")
    ap.add_argument("--wandb", action="store_true", help="log results to Weights & Biases")
    ap.add_argument("--wandb-project", default="cpulling/CORA_RL")
    return ap.parse_args(argv)


def _client(args):
    """The LLM client (None for baselines). ARC_ANTHROPIC_NATIVE=1 uses the native Anthropic SDK,
    which supports prompt caching."""
    if args.policy != "llm":
        return None
    api_key = args.api_key or os.environ.get("ARC_API_KEY")
    if os.environ.get("ARC_ANTHROPIC_NATIVE") == "1":
        return client_for(Provider.anthropic, api_key)
    if args.base_url:
        return client_for(ProviderSpec("openai", args.base_url, None), api_key)
    return client_for(Provider.cmu_gateway, api_key)


def _wandb_condition(args):
    """The experiment cell, so each cell is its own WandB run group."""
    return (f"img-{args.image_mode}_tools"
            + ("" if args.impacts else "_noimpacts")
            + ("" if args.reasoning_effort == "low" else f"_eff-{args.reasoning_effort}")
            + ("" if args.transfers == "manual" else "_xfer-task_only")
            + f"_prompt-{os.path.splitext(os.path.basename(args.prompt))[0]}"
            + (f"_ablate-{args.ablate}" if args.ablate else "")
            + ("" if args.obs_encoding == "json" else f"_obs-{args.obs_encoding}")
            + ("" if args.history == 1 else f"_k{args.history}")
            + ("" if args.temperature is None else f"_temp-{args.temperature}"))


def main(argv=None):
    args = parse_args(argv)
    need_render = args.image_mode == "real" and args.policy == "llm"
    if not Path(default_exe(render=need_render)).exists():
        sys.exit(f"Build not found: {default_exe(render=need_render)}")
    if args.image_mode == "synthetic" and not Path(MAP_GRID_JSON).exists():
        sys.exit(f"Synthetic mode needs the tile grid: {MAP_GRID_JSON} (python -m bench.export_map_grid)")

    outdir = Path(args.out); outdir.mkdir(parents=True, exist_ok=True)
    ulog_dir = outdir / "unity_logs"; ulog_dir.mkdir(exist_ok=True)
    local = None
    if args.base_url:
        local = LocalOptions(reasoning_effort=args.reasoning_effort, max_tokens=args.max_tokens,
                             chat_template_kwargs={"enable_thinking": False} if args.no_thinking else None)
    cfg = RunConfig(policy="noop" if args.validate else args.policy,
                    rounds=2 if args.validate else args.rounds,
                    prompt=args.prompt, ablation=args.ablate, show_impacts=args.impacts,
                    manual_transfers=args.transfers == "manual", obs_encoding=args.obs_encoding,
                    history=args.history, image_mode=args.image_mode, temperature=args.temperature,
                    local=local, base_seed=args.seed, map_config=args.map_config, log_dir=str(ulog_dir))
    port_pool = queue.Queue()
    for w in range(args.workers):
        port_pool.put(args.base_port + w)

    if args.validate:
        rec = run_episode("validate", 0, cfg, None, port_pool)
        print(json.dumps(rec.get("summary") or {"error": rec.get("error")}, indent=2))
        return

    client = _client(args)
    models = ([m.strip() for m in args.models.split(",") if m.strip()] if args.policy == "llm"
              else [args.policy])
    jobs = [(m, e) for m in models for e in range(args.episodes)]
    print(f"=== Benchmark: {len(models)} model(s) x {args.episodes} episodes = {len(jobs)} games, "
          f"{args.workers} worker(s) ===")
    print(f"    models: {models}")
    if args.policy == "llm":
        print(f"    prompt: {args.prompt}{' (ablation ' + args.ablate + ')' if args.ablate else ''}; "
              f"observation: {args.obs_encoding}, history {args.history}, image {args.image_mode}; "
              f"transfers: {args.transfers}")
        print(f"    endpoint: {args.base_url or 'CMU gateway'}"
              + (f" (reasoning_effort={local.reasoning_effort}, max_tokens={local.max_tokens}"
                 f"{', enable_thinking=false' if args.no_thinking else ''})" if local else ""))

    jsonl = outdir / "episodes.jsonl"
    records = []
    with open(jsonl, "w") as fh, ThreadPoolExecutor(max_workers=args.workers) as ex:
        futs = {ex.submit(run_episode, model, ep, cfg, client, port_pool): (model, ep) for model, ep in jobs}
        for fut in as_completed(futs):
            model, ep = futs[fut]
            rec = fut.result()
            records.append(rec)
            fh.write(json.dumps(rec) + "\n"); fh.flush()
            s = rec.get("summary") or {}
            err = rec["error"].splitlines()[0] if rec.get("error") else ""
            print(f"  done {model} ep{ep}: score={s.get('finalScore')} sat={s.get('finalSat')} "
                  f"food={s.get('foodFulfillRate')} lodg={s.get('lodgingFulfillRate')} {'ERR: ' + err if err else ''}")

    agg = aggregate(records)
    (outdir / "summary.json").write_text(json.dumps(agg, indent=2))
    print_table(agg)
    print(f"\nPer-episode: {jsonl}\nSummary:     {outdir / 'summary.json'}")
    if args.wandb:
        log_wandb(records, args.wandb_project, _wandb_condition(args), args.episodes, args.rounds)


if __name__ == "__main__":
    main()
