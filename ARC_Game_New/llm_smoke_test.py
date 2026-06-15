"""
LLM smoke test for the ARC gym environment.

Drives the headless gym with a flagship LLM (via the CMU AI gateway) one round at
a time: summarize observation -> LLM picks actions + task choices -> execute ->
advance. Verifies the full action/observation/reward loop with a real policy
(NOT for RL training — this is a sanity/eval harness).

Design notes:
  * The system prompt is RULES + I/O FORMAT ONLY — deliberately no strategy or
    sequencing guidance, so the run reflects what the agent can infer from the
    observation alone. If results look bad, suspect the OBSERVATION first
    (summarize() below) before concluding anything about the model.
  * All reward scoring lives in arc_game_gym_env_tcp.compute_score (Python-side).

Usage:
  1. Start the headless gym server (see build/run notes), e.g.:
       Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/ARC_DisasterSimulation \
         -batchmode -nographics -gym-server -gym-port 9876 -logFile /tmp/arc.log
  2. python llm_smoke_test.py [model] [rounds]
     (OPENAI_API_KEY is read from the environment or a local .env file.)
"""
import os, sys, json, re
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from arc_game_gym_env_tcp import ARCGameGymEnv
import openai

# ── Config ────────────────────────────────────────────────────────────────
GATEWAY_BASE = "https://ai-gateway.andrew.cmu.edu/v1"
DEFAULT_MODEL = "us.anthropic.claude-opus-4-8"
PORT = 9876


def load_env_key():
    """OPENAI_API_KEY from the environment, falling back to a local .env file."""
    key = os.environ.get("OPENAI_API_KEY")
    if key:
        return key
    env_path = Path(__file__).parent / ".env"
    if env_path.exists():
        for line in env_path.read_text().splitlines():
            if line.startswith("OPENAI_API_KEY="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    raise RuntimeError("OPENAI_API_KEY not found in environment or .env")


# ── Prompt: rules + format only (NO strategy) ───────────────────────────────
SYSTEM_PROMPT = """You are the director in a turn-based disaster-response resource game.

ENTITIES & RULES (mechanics only — no strategy is given):
- Satisfaction (0-100) and Budget ($) are your tracked metrics.
- Tasks: each active task may carry numbered choices. Selecting a choice commits that
  option. A task has `roundsLeft`; if not resolved by then it expires. Demand/Emergency
  tasks represent community needs (food, or population relocation/lodging).
- Buildings have `status`: UnderConstruction -> NeedWorker -> InUse. A building becomes
  InUse only when `workers >= needWorkers`. Kitchens hold/produce food (foodPacks);
  Shelters hold population (capacity).
- Workers: trained (2 workforce, $500) or untrained (1 workforce, $100). State is free,
  working (assigned to a building), in-training, or not-yet-arrived. You may hire, train
  (untrained->trained), and assign workers to buildings.
- Actions (provided each round with cost & params): construction, worker (hire/train),
  worker_assignment, resource_transfer, deconstruction.
- Each round you submit actions + choices; then time advances one round.

RESPOND ONLY with JSON:
{"reasoning":"<your step-by-step rationale for THIS round's decision>",
 "choices":[{"taskId":<int>,"choiceId":<int>}...], "actions":[<action_index>...], "note":"<=20 words"}
"""


def compact_action(i, a):
    o = {"i": i, "type": a.get("action_type"), "desc": a.get("description", "")[:48], "cost": a.get("cost")}
    if a.get("action_type") == "construction":
        o["required_workers"] = a.get("required_workers", 4)
    return o


def summarize(env, show_impacts=True):
    """Compress the game state into the observation the LLM sees.

    NOTE: this is the main lever for giving the model a fair view of the game.
    If the agent seems confused, enrich this before blaming the model.

    show_impacts: when True, each task choice includes its sparse impacts list
    (e.g. Budget +5000, Satisfaction +10) so the agent can reason about funding /
    cost tradeoffs. Toggle off for the no-observation-impacts ablation. (Unity always
    sends impacts in the payload; this only controls what the model is shown.)
    """
    gs = env.game_state
    sb = gs.get("satisfactionAndBudget", {})
    wf = gs.get("workforceState", {})
    facs = []
    for f in gs.get("mapState", {}).get("facilities", []):
        facs.append({"name": f.get("facilityName"), "type": f.get("buildingType"),
                     "status": f.get("buildingStatus"), "workers": f.get("assignedWorkforce"),
                     "needWorkers": f.get("requiredWorkforce"),
                     "food": (f.get("resources") or {}).get("foodPacks"),
                     "pop": f.get("currentPopulation"), "cap": f.get("populationCapacity")})
    tasks = []
    for t in gs.get("allActiveTasks", []):
        ch = []
        for c in (t.get("choices") or []):
            o = {"choiceId": c["choiceId"], "text": c["choiceText"][:70]}
            if show_impacts and c.get("impacts"):
                # compact: e.g. {"Budget": 5000, "Satisfaction": 10}
                o["impacts"] = {i["type"]: i["value"] for i in c["impacts"]}
            ch.append(o)
        tasks.append({"taskId": t["taskId"], "type": t["taskType"], "title": t["taskTitle"],
                      "roundsLeft": t.get("roundsRemaining"), "choices": ch})
    acts = env.get_valid_actions()
    return {
        "day": gs.get("sessionInfo", {}).get("currentDay"),
        "budget": sb.get("budget"), "satisfaction": sb.get("satisfaction"),
        "workers": {"freeTrained": wf.get("freeTrainedWorkers"), "freeUntrained": wf.get("freeUntrainedWorkers"),
                    "working": wf.get("workingTrainedWorkers", 0) + wf.get("workingUntrainedWorkers", 0),
                    "inTraining": wf.get("untrainedWorkersInTraining")},
        "logistics": {"vehiclesFree": gs.get("logistics", {}).get("availableVehicles")},
        "facilities": facs,
        "tasks": tasks,
        "actions": [compact_action(i, a) for i, a in enumerate(acts)],
    }


def ask(client, model, state):
    r = client.chat.completions.create(
        model=model, max_tokens=1400,
        messages=[{"role": "system", "content": SYSTEM_PROMPT},
                  {"role": "user", "content": "State:\n" + json.dumps(state) + "\n\nJSON decision:"}])
    m = re.search(r"\{.*\}", r.choices[0].message.content, re.S)
    return json.loads(m.group(0)) if m else {"choices": [], "actions": []}


def main():
    model = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_MODEL
    rounds = int(sys.argv[2]) if len(sys.argv) > 2 else 18
    client = openai.OpenAI(api_key=load_env_key(), base_url=GATEWAY_BASE)

    env = ARCGameGymEnv(unity_exe_path=None, unity_port=PORT, auto_start_unity=False, max_episode_steps=rounds + 5)
    obs, info = env.reset()
    print(f"=== LLM smoke test (rules-only prompt, rich obs): {model} ===")
    total = 0.0
    for rnd in range(rounds):
        state = summarize(env)
        try:
            dec = ask(client, model, state)
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
