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
import obs_encoder  # SHARED canonical encoder (this module is its source of truth; router uses it too)
# SHARED command-grammar parser (source of truth is cmd_parser; the live router imports it too).
# Re-exported here so existing callers (benchmark_models.py) keep importing from llm_smoke_test.
from cmd_parser import (  # noqa: F401
    _BUILD_ALIASES, _TRANSFER_RESOURCE, _action_index, _bundle_indices, _CMD_RE, parse_commands,
)
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


# ── Prompt version toggle (for A/B testing observation/prompt changes) ──────
# ARC_PROMPT_VERSION=old -> the original mechanics-only prompt + lean obs (no objective,
#   no horizon, no spend/motel signals, truncated choice/action text). =new (default) ->
# the enriched prompt + obs. Both arms otherwise share identical code so the only variable
# is what the model is shown.
PROMPT_VERSION = os.environ.get("ARC_PROMPT_VERSION", "new").strip().lower()
_NEW = PROMPT_VERSION != "old"

# ── minimal_v2 fix layer (A/B against plain `minimal`) ──────────────────────
# When ON, the encoding carries the round of prompt/observation fixes that go WITH the
# `minimal_v2` system prompt, so the `minimal` vs `minimal_v2` arms differ ONLY by this layer:
#   * facility `status` renders "Passive" (not "-") for pre-built fixtures (Communities/Motel),
#   * the dead `transfers: (none)` affordance line is dropped (task_only has no usable transfers),
#   * task `affects` is shown only when it names a real facility (drops dangling placeholders),
#   * choice text is no longer truncated at 90 chars (keeps the "$200/person/day ongoing" tail etc).
# OFF reproduces the plain `minimal` behavior. Set per-run from system_variant via _set_v2().
_V2 = False


def _set_v2(on):
    """Toggle the minimal_v2 fix layer (encoding side). Set once per run before obs is built."""
    global _V2
    _V2 = bool(on)

# Original ("before") prompt — mechanics only, no objective/horizon/lodging-economics/casework.
OLD_SYSTEM_PROMPT = """You are the director in a turn-based disaster-response resource game.

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

# Enriched ("after") prompt — rules + objective/horizon/lodging-economics/casework.
NEW_SYSTEM_PROMPT = """You are the director in a turn-based disaster-response resource game.

OBJECTIVE: maximize cumulative score over the WHOLE game (a fixed number of rounds; each
observation gives `roundsLeft`). Score each round rewards meeting community needs — food,
lodging, and casework (return-home) — and subtracts cost-inefficiency (spending a lot per unit
of need served). Budget is finite and may go negative; sustained overspending and large negative
budgets are heavily penalized. Plan across the full horizon, not just the current round.

ENTITIES & RULES (mechanics; the strategy is up to you):
- Satisfaction (0-100) and Budget ($) are tracked metrics. Each observation also gives a
  cumulative `spend` breakdown (food/lodging/worker/casework) and `roundsLeft`.
- Tasks carry numbered choices; selecting one commits it. A task has `roundsLeft`; if unresolved
  by then it expires. Demand/Emergency tasks are community needs (food, or population
  relocation/lodging); Advisory tasks include "Casework Request" (see CASEWORK).
- LODGING — two ways to house relocated population, with very different economics:
    * Shelters: one-time build (~$1000) + workers to staff. Once InUse they cost $0/day.
    * Motel (prebuilt, large capacity): $0 to build, BUT charges ~$200 per resident per day,
      every day they remain — a recurring drain that is NOT shown on the choice and compounds
      over all remaining rounds. The observation reports current `motelDailyCost`.
  Over many rounds the motel is far more expensive than building shelters.
- CASEWORK / return-home: clients housed for many rounds raise an Advisory "Casework Request".
  Resolving it ("send to casework site") frees their lodging and is scored — but it ONLY works
  if you have already BUILT and STAFFED a CaseworkSite. Build one early if you expect this.
- Buildings have `status`: UnderConstruction -> NeedWorker -> InUse (InUse only when
  `workers >= needWorkers`). Construction takes ~1 day before a building is usable.
  Kitchens hold/produce food (foodPacks); Shelters/Motel hold population (capacity).
- Workers: trained (2 workforce, $500) or untrained (1 workforce, $100): free, working, in-
  training, or not-yet-arrived. Hire/train/assign them. Free (unassigned) workers do no work and
  are wasted, but hiring beyond need wastes budget. Worker demand shifts as you build/operate.
- Actions (provided each round with cost & params): construction, worker (hire/train),
  worker_assignment, resource_transfer, deconstruction. Reference game actions by their index `i`
  (valid for THIS round only — the list is re-enumerated every round); task choices by
  {taskId, choiceId}. Each round you submit actions + choices, then time advances one round.

RESPOND ONLY with JSON:
{"reasoning":"<your step-by-step rationale for THIS round's decision>",
 "choices":[{"taskId":<int>,"choiceId":<int>}...], "actions":[<action_index>...], "note":"<=20 words"}
"""

SYSTEM_PROMPT = NEW_SYSTEM_PROMPT if _NEW else OLD_SYSTEM_PROMPT

# PIMMUR minimal-control variant (idx). Same factual mechanics + neutral OBJECTIVE as the original
# prompt, but every line of *strategy* is removed: no lodging-economics ranking (motel-vs-shelter),
# no "build casework early" prescription, no horizon nudge, and no "negative budget heavily
# penalized" claim (which also misstated the reward). It discloses the WHAT (objective + action
# surface) but never the HOW. This is the minimal-control / unawareness arm (arXiv 2509.18052).
MINIMAL_SYSTEM_PROMPT = """You are the director in a turn-based disaster-response resource game.

OBJECTIVE: maximize cumulative score over the WHOLE game (a fixed number of rounds; each
observation gives `roundsLeft`). Each round the score rewards meeting community needs — food,
lodging, and casework (return-home) — and subtracts cost-inefficiency (spend per unit of need
served). Budget ($) is finite and may go negative.

ENTITIES & RULES (mechanics only — no strategy is given):
- Satisfaction (0-100) and Budget ($) are tracked metrics. Each observation also gives a
  cumulative `spend` breakdown (food/lodging/worker/casework) and `roundsLeft`.
- Tasks carry numbered choices; selecting one commits it. A task has `roundsLeft`; if unresolved
  by then it expires. Demand/Emergency tasks are community needs (food, or population
  relocation/lodging); Advisory tasks include "Casework Request".
- LODGING: relocated population can be housed in Shelters or in the prebuilt Motel. A Shelter must
  be built (~$1000) and staffed before it is InUse. The Motel needs no construction; it charges a
  per-resident daily cost reported as `motelDailyCost`.
- CASEWORK / return-home: a "Casework Request" can be resolved only if a CaseworkSite has already
  been built and staffed.
- Buildings have `status`: UnderConstruction -> NeedWorker -> InUse (InUse only when
  `workers >= needWorkers`). Construction takes ~1 day. Kitchens hold/produce food (foodPacks);
  Shelters/Motel hold population (capacity).
- Workers: trained (2 workforce, $500) or untrained (1 workforce, $100): free, working, in-
  training, or not-yet-arrived. You may hire, train (untrained->trained), and assign workers to
  buildings.
- Actions (provided each round with cost & params): construction, worker (hire/train),
  worker_assignment, resource_transfer, deconstruction. Reference game actions by their index `i`
  (valid for THIS round only — the list is re-enumerated every round); task choices by
  {taskId, choiceId}. Each round you submit actions + choices, then time advances one round.

RESPOND ONLY with JSON:
{"reasoning":"<your step-by-step rationale for THIS round's decision>",
 "choices":[{"taskId":<int>,"choiceId":<int>}...], "actions":[<action_index>...], "note":"<=20 words"}
"""


def idx_system_prompt(variant="original"):
    """idx-format system prompt by ablation variant: 'original' = the strategy-laden default
    (SYSTEM_PROMPT); 'minimal' = the PIMMUR minimal-control prompt (mechanics + objective, no
    strategy)."""
    return MINIMAL_SYSTEM_PROMPT if variant == "minimal" else SYSTEM_PROMPT

# ── Action format toggle: "menu" (default, enumerated index list) vs "commands" ──
# ARC_ACTION_FORMAT=commands -> the model is shown game STATE only (no flat action menu) and
# emits compact command tags (<build>/<hire>/<train>/<staff>/<task>/<deconstruct>) that a parser
# maps back to enumerated action indices. Collapses the ~70-action menu to a fixed grammar:
# far fewer prompt tokens and a stable, factored action surface for RL. "menu" leaves the
# original enumerated-index path (and the non-learning baselines) completely untouched.
ACTION_FORMAT = os.environ.get("ARC_ACTION_FORMAT", "menu").strip().lower()
_CMD = ACTION_FORMAT == "commands"

# _BUILD_ALIASES / _TRANSFER_RESOURCE now live in cmd_parser (imported above, re-exported for back-compat).

CMD_SYSTEM_PROMPT = """You are the director in a turn-based disaster-response resource game.

OBJECTIVE: maximize cumulative score over the WHOLE game (`roundsLeft` rounds remain). Each round
rewards meeting community needs — food, lodging, and casework (return-home) — and subtracts
cost-inefficiency (spending a lot per unit of need served). Budget is finite and may go negative;
sustained overspending and large negative budgets are heavily penalized. Plan across the full horizon.

ENTITIES & RULES (mechanics; the strategy is up to you):
- Satisfaction (0-100) and Budget ($) are tracked. The state gives a cumulative `spend` breakdown and
  `roundsLeft`.
- Tasks carry numbered choices; committing one resolves it. A task has `roundsLeft`; if unresolved it
  expires. Demand/Emergency tasks are community needs (food, or population relocation/lodging);
  Advisory tasks include "Casework Request".
- LODGING — two ways to house relocated population, very different economics:
    * Shelters: one-time build (~$1000) + ~4 workers to staff. Once InUse they cost $0/day.
    * Motel (prebuilt, large): $0 to build, BUT ~$200 per resident per DAY, every remaining day — a
      recurring drain NOT shown on the choice. The state reports `motelDailyCost`.
- CASEWORK / return-home: long-housed clients raise an Advisory "Casework Request". Resolving it frees
  their lodging and is scored — but ONLY if you have already BUILT and STAFFED a CaseworkSite.
- Buildings have `status`: UnderConstruction -> NeedWorker -> InUse (InUse only when workers >=
  needWorkers). Construction takes ~1 day. Kitchens produce/hold food; Shelters/Motel hold population.
- `status: Passive` marks pre-built fixtures (the Communities and the Motel). They are always there,
  cannot be built or deconstructed, and need no staffing — they exist only to hold population/food.
- Workers: untrained (1 workforce, $100) or trained (2 workforce, $300). Free workers do no work
  (wasted) until assigned; hiring beyond need wastes budget.

HOW TO ACT — emit COMMAND TAGS. You see the game STATE only (no action list); choose any number of
commands from this fixed grammar:
  <build>TYPE,SITE_ID</build>     TYPE = kitchen | shelter | casework. Build at an available site
                                  (SITE_ID from state.sites). Costs ~$1000; then needs staffing.
  <hire>KIND,N</hire>             KIND = untrained | trained. Hire N workers into the free pool.
  <train>N</train>                Train N free untrained workers into trained ($500 each).
  <staff>BUILDING,N</staff>       Assign N free workers to BUILDING (a name from state.facilities).
  <task>TASK_ID,CHOICE_ID</task>  Commit a choice for an active task (from state.tasks).
  <deconstruct>BUILDING</deconstruct>   Tear down a building, freeing its site.
You may repeat a command type (e.g. several <staff> or <hire>). Commands invalid for the current
state (bad/blocked site, unaffordable, unknown building, nonexistent choice) are ignored.

The state's `available` block tells you exactly what is executable THIS round, so you don't have to
guess: `hire` (kinds you can afford), `trainUntrainedMax`, `needStaff` ({building: workforce still
needed}), `staffNow` (the subset of those assignable RIGHT NOW from current free workers),
and `buildSites` (site ids you can build on).
Most rejected commands are staffing with no free worker — `available` prevents that.
STAFF ONLY buildings listed in `available.needStaff`, copying the EXACT name shown there (e.g.
`<staff>NAME,N</staff>` where NAME is a key of needStaff). A building NOT in needStaff is either
already fully staffed or not yet built — staffing it is rejected.
EXECUTION ORDER: your commands resolve in a fixed commonsense order each turn — deconstruct, build,
hire, train, staff, transfer — NOT the textual order. Hiring and staffing in the SAME turn works:
`<hire>untrained,4</hire>` then `<staff>NAME,4</staff>` (NAME in needStaff) assigns the workers you
just hired — so `staffNow` may read 0 yet a same-turn hire-then-staff still succeeds. BUT a building
you `<build>` this turn is UnderConstruction and CANNOT be staffed until it finishes (~next round);
it appears in `needStaff` only once ready.

RESPOND with one short line of reasoning prefixed `REASONING:`, then the command tags, e.g.:
REASONING: population rising and no shelter yet; build+staff one and answer the food task.
<build>shelter,3</build>
<hire>untrained,4</hire>
<task>12,2</task>
"""

# Appended to the cmd system prompt ONLY when the env runs with manual_transfers=True, so that the
# cmd format reaches parity with the idx menu (which always lists standalone transfers in that mode).
# In human-faithful (task_only) mode this is omitted and the <transfer> tag is simply never enumerated.
CMD_TRANSFER_DOC = """
MANUAL TRANSFERS ENABLED — you may also move resources between facilities directly:
  <transfer>RESOURCE,SOURCE,DEST,QTY</transfer>
      RESOURCE = food | people.  SOURCE/DEST = facility names from state.facilities.
      Dispatches a free idle vehicle (no budget cost; ties up a vehicle ~1 round). Offered
      quantities are discrete (food ~10/25/50/100, people ~5/10/20) and the closest offered
      amount is used. Ignored when no idle vehicle is available or the facility pair is invalid.
  e.g. <transfer>food,Community01,Motel,25</transfer>
The `available.transfers` block lists the valid routes this round (empty/absent if no idle vehicle),
so you can transfer without guessing.
"""


# PIMMUR minimal-control variant (cmd). Same factual mechanics + command grammar as CMD_SYSTEM_PROMPT
# but strategy stripped: neutral lodging/casework mechanics (no motel-vs-shelter ranking, no "build
# early"), no horizon nudge, no "negative budget heavily penalized" claim, and a neutral REASONING
# example (the guided one telegraphed a build-a-shelter-now strategy).
CMD_MINIMAL_SYSTEM_PROMPT = """You are the director in a turn-based disaster-response resource game.

OBJECTIVE: maximize cumulative score over the WHOLE game (`roundsLeft` rounds remain). Each round the
score rewards meeting community needs — food, lodging, and casework (return-home) — and subtracts
cost-inefficiency (spend per unit of need served). Budget is finite and may go negative.

ENTITIES & RULES (mechanics only — no strategy is given):
- Satisfaction (0-100) and Budget ($) are tracked. The state gives a cumulative `spend` breakdown and
  `roundsLeft`.
- Tasks carry numbered choices; committing one resolves it. A task has `roundsLeft`; if unresolved it
  expires. Demand/Emergency tasks are community needs (food, or population relocation/lodging);
  Advisory tasks include "Casework Request".
- LODGING: relocated population can be housed in Shelters or the prebuilt Motel. A Shelter must be
  built (~$1000) and staffed before InUse. The Motel needs no construction; it charges a per-resident
  daily cost reported as `motelDailyCost`.
- CASEWORK / return-home: a "Casework Request" can be resolved only if a CaseworkSite has already been
  built and staffed.
- Buildings have `status`: UnderConstruction -> NeedWorker -> InUse (InUse only when workers >=
  needWorkers). Construction takes ~1 day. Kitchens produce/hold food; Shelters/Motel hold population.
- Workers: untrained (1 workforce, $100) or trained (2 workforce, $300). Workers must be assigned to a
  building to do work.

HOW TO ACT — emit COMMAND TAGS. You see the game STATE only (no action list); choose any number of
commands from this fixed grammar:
  <build>TYPE,SITE_ID</build>     TYPE = kitchen | shelter | casework. Build at an available site
                                  (SITE_ID from state.sites). Costs ~$1000; then needs staffing.
  <hire>KIND,N</hire>             KIND = untrained | trained. Hire N workers into the free pool.
  <train>N</train>                Train N free untrained workers into trained ($500 each).
  <staff>BUILDING,N</staff>       Assign N free workers to BUILDING (a name from state.facilities).
  <task>TASK_ID,CHOICE_ID</task>  Commit a choice for an active task (from state.tasks).
  <deconstruct>BUILDING</deconstruct>   Tear down a building, freeing its site.
You may repeat a command type (e.g. several <staff> or <hire>). Commands invalid for the current
state (bad/blocked site, unaffordable, unknown building, nonexistent choice) are ignored.

The state's `available` block tells you exactly what is executable THIS round, so you don't have to
guess: `hire` (kinds you can afford), `trainUntrainedMax`, `needStaff` ({building: workforce still
needed}), `staffNow` (the subset of those assignable RIGHT NOW from current free workers),
and `buildSites` (site ids you can build on).
Most rejected commands are staffing with no free worker — `available` prevents that.
STAFF ONLY buildings listed in `available.needStaff`, copying the EXACT name shown there (e.g.
`<staff>NAME,N</staff>` where NAME is a key of needStaff). A building NOT in needStaff is either
already fully staffed or not yet built — staffing it is rejected.
EXECUTION ORDER: your commands resolve in a fixed commonsense order each turn — deconstruct, build,
hire, train, staff, transfer — NOT the textual order. Hiring and staffing in the SAME turn works:
`<hire>untrained,4</hire>` then `<staff>NAME,4</staff>` (NAME in needStaff) assigns the workers you
just hired — so `staffNow` may read 0 yet a same-turn hire-then-staff still succeeds. BUT a building
you `<build>` this turn is UnderConstruction and CANNOT be staffed until it finishes (~next round);
it appears in `needStaff` only once ready.

RESPOND with one short line of reasoning prefixed `REASONING:`, then the command tags, e.g.:
REASONING: <your rationale for this round's decision>
<build>shelter,3</build>
<hire>untrained,4</hire>
<task>12,2</task>
"""


# ── minimal_v2: minimal + the prompt-side fix layer ────────────────────────
# A/B-clean superset of CMD_MINIMAL_SYSTEM_PROMPT: same text PLUS exactly two additions, so the
# minimal vs minimal_v2 comparison isolates these (and the matching encoding fixes gated by _V2):
#   (1) Passive-fixtures note — the Communities and Motel render `status: Passive`; without this the
#       models burned turns trying to <build>/<staff>/<deconstruct> them (see flagship trace analysis).
#   (2) Theme 1 — a sharpened build-then-staff rule: build and staff CANNOT both land in one turn.
#       The plain-minimal wording warned about it but models still paired <build>X with <staff>X;
#       this states the silent-drop outcome explicitly and tells them to wait for `needStaff`.
_V2_PASSIVE_NOTE = (
    "  needWorkers). Construction takes ~1 day. Kitchens produce/hold food; Shelters/Motel hold population.\n"
    "- `status: Passive` marks pre-built fixtures (the Communities and the Motel). They are always there,\n"
    "  cannot be built or deconstructed, and need no staffing — they exist only to hold population/food.\n"
)
_V2_BUILD_STAFF = (
    "BUILD-THEN-STAFF NEVER WORKS IN ONE TURN: a building you `<build>` this turn is UnderConstruction,\n"
    "is NOT listed in `available.needStaff`, and ANY `<staff>` aimed at it this turn is silently dropped.\n"
    "Build it this turn; `<staff>` it only on a LATER turn, once it appears in `needStaff` (~next round)."
)
CMD_MINIMAL_V2_SYSTEM_PROMPT = (
    CMD_MINIMAL_SYSTEM_PROMPT
    .replace(
        "  needWorkers). Construction takes ~1 day. Kitchens produce/hold food; Shelters/Motel hold population.\n",
        _V2_PASSIVE_NOTE, 1)
    .replace(
        "BUT a building\n"
        "you `<build>` this turn is UnderConstruction and CANNOT be staffed until it finishes (~next round);\n"
        "it appears in `needStaff` only once ready.",
        _V2_BUILD_STAFF, 1)
)


def cmd_system_prompt(manual_transfers=True, variant="original"):
    """Cmd-format system prompt by ablation variant ('original' default; 'minimal' = PIMMUR
    minimal-control; 'minimal_v2' = minimal + the prompt-side fix layer, paired with the _V2
    encoding fixes), with the manual-transfer grammar appended only when transfers are enumerated
    (manual mode). Keeps the prompt faithful to the actual action surface."""
    if variant == "minimal_v2":
        base = CMD_MINIMAL_V2_SYSTEM_PROMPT
    elif variant == "minimal":
        base = CMD_MINIMAL_SYSTEM_PROMPT
    else:
        base = CMD_SYSTEM_PROMPT
    return base + (CMD_TRANSFER_DOC if manual_transfers else "")


# ── OBSERVATION ENCODER ─────────────────────────────────────────────────────
# The encoder now lives in the SHARED `obs_encoder` module (this file is its source
# of truth; the live agent router imports the same module). These are thin wrappers
# that (a) forward the benchmark's A/B toggles `_NEW`/`_V2` as explicit params, and
# (b) unpack `env` into `(env.game_state, env.get_valid_actions())` so obs_encoder has
# no gym/benchmark coupling. Output is byte-identical to the pre-refactor inline code
# (guarded by a golden equivalence test), so in-flight benchmark runs are unaffected.
MOTEL_COST_PER_PERSON_PER_DAY = obs_encoder.MOTEL_COST_PER_PERSON_PER_DAY  # re-export (back-compat)


def compact_action(i, a):
    return obs_encoder.compact_action(i, a, new=_NEW)


def summarize(env, show_impacts=True, rounds_left=None):
    """Compress the game state into the observation the LLM sees.

    NOTE: this is the main lever for giving the model a fair view of the game.
    If the agent seems confused, enrich this before blaming the model.

    show_impacts: when True, each task choice includes its sparse impacts list
    (e.g. Budget +5000, Satisfaction +10) so the agent can reason about funding /
    cost tradeoffs. Toggle off for the no-observation-impacts ablation. (Unity always
    sends impacts in the payload; this only controls what the model is shown.)
    rounds_left: rounds remaining in the EPISODE (game horizon), so the model can size
    recurring costs against the time left. None -> field omitted.
    """
    return obs_encoder.build_observation(env.game_state, env.get_valid_actions(),
                                         new=_NEW, v2=_V2,
                                         show_impacts=show_impacts, rounds_left=rounds_left)


def summarize_commands(env, show_impacts=True, rounds_left=None):
    """State-only observation for the command-tag format: identical game facts as summarize(),
    but WITHOUT the enumerated `actions` menu. Instead exposes the slots a command can target —
    available construction `sites` (id+name) and a fixed `costs` block — so the model can form
    <build>/<hire>/<staff>/<task> commands from state alone. The big token saving is dropping the
    ~70-entry action list."""
    return obs_encoder.build_observation_commands(env.game_state, env.get_valid_actions(),
                                                  new=_NEW, v2=_V2,
                                                  show_impacts=show_impacts, rounds_left=rounds_left)


def render_state_compact(obs):
    """Render the cmd observation dict as a COMPACT TEXT block instead of json.dumps(obs).
    (Delegates to obs_encoder; forwards the minimal_v2 fix-layer toggle.)"""
    return obs_encoder.render_state_compact(obs, v2=_V2)


def render_state_delta(obs, prev_obs):
    """History-carrying DELTA rendering (facilities block diffed vs the previous turn; everything
    actionable stays full). Delegates to obs_encoder; forwards the minimal_v2 toggle."""
    return obs_encoder.render_state_delta(obs, prev_obs, v2=_V2)


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
    print(f"=== LLM smoke test (rules-only prompt, rich obs): {model} ===")
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
