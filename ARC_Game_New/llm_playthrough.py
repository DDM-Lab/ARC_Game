"""ARC gym LLM playthrough — single-model sanity / eval harness.

Drives the headless gym with ONE flagship LLM (via the CMU AI gateway) one round at a
time: render observation -> LLM picks actions + task choices -> execute -> advance,
printing per-round reward / satisfaction / budget. Verifies the full action/observation/
reward loop with a real policy (NOT RL training; the multi-model matrix is
benchmark_models.py). All reward scoring lives in arc_game_gym_env_tcp.compute_score.

The reusable library code lives in appropriately named modules and is imported directly
here: system prompts -> `cora_prompts`, command parser -> `cmd_parser`, observation
adapters -> `obs_adapters`, gateway config -> `llm_gateway`. (`llm_smoke_test` is a thin
back-compat shim re-exporting those names for callers that still import the old path.)

Usage:
  1. Start the headless gym server (gym-server on port 9876).
  2. python llm_playthrough.py [model] [rounds]
     (OPENAI_API_KEY is read from the environment or a local .env file.)
"""
import os
import sys
import json
import re
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from arc_game_gym_env_tcp import ARCGameGymEnv
import openai

from cmd_parser import parse_commands
from cora_prompts import SYSTEM_PROMPT, CMD_SYSTEM_PROMPT
from obs_adapters import summarize, summarize_commands
from llm_gateway import GATEWAY_BASE, load_env_key

# ── Script config ───────────────────────────────────────────────────────────
DEFAULT_MODEL = "us.anthropic.claude-opus-4-8"
PORT = 9876

# Action format toggle for THIS script: "menu" (enumerated idx list) vs "commands"
# (state-only obs + command tags). See obs_adapters / cmd_parser for the shared surface.
ACTION_FORMAT = os.environ.get("ARC_ACTION_FORMAT", "menu").strip().lower()
_CMD = ACTION_FORMAT == "commands"


def ask(client, model, state):
    r = client.chat.completions.create(
        model=model, max_tokens=1400,
        messages=[{"role": "system", "content": SYSTEM_PROMPT},
                  {"role": "user", "content": "State:\n" + json.dumps(state) + "\n\nJSON decision:"}])
    m = re.search(r"\{.*\}", r.choices[0].message.content, re.S)
    return json.loads(m.group(0)) if m else {"choices": [], "actions": []}


def ask_commands(client, model, state, env):
    """Command-tag turn: send the state-only obs + command grammar, parse the emitted tags into
    (actions, choices) via parse_commands. Mirrors ask()'s return shape ({choices, actions, note,
    reasoning}) so callers are format-agnostic."""
    r = client.chat.completions.create(
        model=model, max_tokens=1400,
        messages=[{"role": "system", "content": CMD_SYSTEM_PROMPT},
                  {"role": "user", "content": "State:\n" + json.dumps(state) + "\n\nCommands:"}])
    text = r.choices[0].message.content or ""
    pc = parse_commands(text, env)
    reason = ""
    mr = re.search(r"REASONING:\s*(.+)", text)
    if mr:
        reason = mr.group(1).splitlines()[0].strip()
    return {"choices": pc["choices"], "actions": pc["actions"], "reasoning": reason,
            "note": " ".join(pc["parsed"])[:120], "errors": pc["errors"]}


def main():
    model = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_MODEL
    rounds = int(sys.argv[2]) if len(sys.argv) > 2 else 18
    client = openai.OpenAI(api_key=load_env_key(), base_url=GATEWAY_BASE)

    env = ARCGameGymEnv(unity_exe_path=None, unity_port=PORT, auto_start_unity=False, max_episode_steps=rounds + 5)
    obs, info = env.reset()
    print(f"=== LLM playthrough (rules-only prompt, rich obs): {model} ===")
    total = 0.0
    if _CMD:
        print("  [action format: COMMANDS — state-only obs + command tags]")
    for rnd in range(rounds):
        state = summarize_commands(env, rounds_left=rounds - rnd) if _CMD else summarize(env)
        try:
            dec = ask_commands(client, model, state, env) if _CMD else ask(client, model, state)
        except Exception as e:
            print(f" r{rnd}: LLM error: {e}")
            break
        nsel = 0
        for c in dec.get("choices", []):
            try:
                if env.select_task_choice(int(c["taskId"]), int(c["choiceId"])):
                    nsel += 1
            except Exception:
                pass
        acts = ",".join(str(int(a)) for a in dec.get("actions", []) if str(a).lstrip("-").isdigit())
        obs, reward, term, trunc, info = env.step(acts)
        total += reward
        rm = info.get("reward_metrics") or {}
        print(f" r{rnd:2d}: rew={reward:+.3f} sumR={total:+.2f} sat={info['satisfaction']:.0f} bud={info['budget']:.0f} | "
              f"food {rm.get('foodFulfilled')}/{rm.get('foodResolved')} lodg {rm.get('lodgingFulfilled')}/{rm.get('lodgingResolved')} "
              f"satS={info['satisfaction_score']:.2f} cost={info['cost_efficiency']:.2f} sel={nsel} | {dec.get('note', '')[:60]}")
        if term or trunc:
            print("  EPISODE END")
            break
    env.close()
    print(f"\nTOTAL reward: {total:+.3f}")


if __name__ == "__main__":
    main()
