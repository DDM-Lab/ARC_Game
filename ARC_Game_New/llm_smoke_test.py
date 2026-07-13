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

# Aliases the model may type for each building type -> canonical enumerator building_type.
_BUILD_ALIASES = {
    "kitchen": "Kitchen", "kitchens": "Kitchen",
    "shelter": "Shelter", "shelters": "Shelter",
    "casework": "CaseworkSite", "caseworksite": "CaseworkSite",
    "caseworks": "CaseworkSite", "case": "CaseworkSite",
}

_TRANSFER_RESOURCE = {
    "food": "FoodPacks", "foodpacks": "FoodPacks", "foodpack": "FoodPacks", "packs": "FoodPacks",
    "people": "Population", "population": "Population", "pop": "Population", "persons": "Population",
}

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


def _action_index(env):
    """{action_type: [(index, action_dict), ...]} over the round's enumerated actions."""
    idx = {}
    for i, a in enumerate(env.get_valid_actions()):
        idx.setdefault(a.get("action_type"), []).append((i, a))
    return idx


def _bundle_indices(candidates, n):
    """Greedily cover quantity `n` using available (quantity -> index) bundles, largest first.
    Repeating an index re-executes that bundle (the env executes each listed index in order)."""
    by_q = sorted(candidates, key=lambda qi: -qi[0])   # [(qty, index), ...] desc
    out, remaining = [], n
    while remaining > 0:
        pick = next((qi for qi in by_q if qi[0] <= remaining), None)
        if pick is None:
            pick = by_q[-1] if by_q else None           # smallest bundle, if even that overshoots
            if pick is None:
                break
        out.append(pick[1]); remaining -= pick[0]
    return out


_CMD_RE = re.compile(r"<\s*(build|hire|train|staff|task|deconstruct|transfer)\s*>(.*?)<\s*[\\/]\s*\1\s*>",
                     re.I | re.S)


def parse_commands(text, env):
    """Map command tags in `text` to (action_indices, choices) against the round's enumeration.

    Returns dict: {actions:[idx...], choices:[{taskId,choiceId}...], parsed:[...], errors:[...]}.
    Pure w.r.t. the env beyond reading its enumerated actions, so it is unit-testable on a snapshot
    via a stub exposing get_valid_actions()/game_state.
    """
    idx = _action_index(env)
    choices, parsed, errors = [], [], []

    # Commonsense execution order: regardless of the order the model writes the tags, we execute
    # deconstruct -> build -> hire -> train -> staff -> transfer. This makes the obvious plan
    # "hire, then staff the workers you just hired" work in a single turn (the gym executes the
    # action list in order). emit() tags each resolved menu index with its category priority;
    # the final `actions` list is the indices sorted by that priority (stable within a category).
    _PRIO = {"deconstruct": 0, "build": 1, "hire": 2, "train": 3, "staff": 4, "transfer": 5}
    act_items = []  # (priority, action_index)

    def emit(cmd_name, *idxs):
        for i in idxs:
            act_items.append((_PRIO[cmd_name], i))

    # Simulated free-workforce pool, in WORKFORCE UNITS (trained=2, untrained=1), so a <staff>
    # issued the same turn as a <hire> can see the newly-hired workers. ActionExecutor creates
    # hired workers Free (immediately assignable), and TryAssignWorkersToBuilding pulls from the
    # global free pool, so this models execution faithfully. Staff is resolved AFTER the main
    # pass (see staff_cmds) once every <hire> has been counted, independent of textual order.
    gs = env.game_state
    _wf = gs.get("workforceState", {})
    sim_wf = (_wf.get("freeTrainedWorkers", 0) or 0) * 2 + (_wf.get("freeUntrainedWorkers", 0) or 0)
    # building -> remaining workforce need this turn (consumed as we staff, so two <staff> tags to
    # the same building don't both claim the full need).
    need = {}
    for f in gs.get("mapState", {}).get("facilities", []):
        if f.get("buildingStatus") in ("NeedWorker", "InUse"):
            rem = (f.get("requiredWorkforce", 4) or 0) - (f.get("assignedWorkforce", 0) or 0)
            if rem > 0 and f.get("facilityName"):
                need[f["facilityName"]] = rem
    staff_cmds = []  # deferred (raw_label, N) resolved after all hires are counted

    def split(body, n):
        parts = [p.strip() for p in body.replace("\n", " ").split(",")]
        return parts if len(parts) >= n else None

    for m in _CMD_RE.finditer(text or ""):
        cmd = m.group(1).lower()
        body = m.group(2).strip()
        try:
            if cmd == "build":
                p = split(body, 2)
                if not p:
                    errors.append(f"build: need TYPE,SITE_ID got '{body}'"); continue
                btype = _BUILD_ALIASES.get(p[0].lower().replace(" ", ""))
                site = int(float(p[1]))
                if not btype:
                    errors.append(f"build: unknown type '{p[0]}'"); continue
                hit = next((i for i, a in idx.get("construction", [])
                            if a["construction"]["building_type"] == btype
                            and int(a["construction"]["site_id"]) == site), None)
                if hit is None:
                    errors.append(f"build: no available {btype} at site {site}"); continue
                emit("build", hit); parsed.append(f"build {btype}@{site}")

            elif cmd == "hire":
                p = split(body, 2)
                if not p:
                    errors.append(f"hire: need KIND,N got '{body}'"); continue
                kind = p[0].lower()
                trained = kind in ("trained", "true", "t", "1", "yes")
                wat = "hire_trained" if trained else "hire_untrained"
                n = int(float(p[1]))
                cands = [(a["worker"]["quantity"], i) for i, a in idx.get("worker", [])
                         if a["worker"]["worker_action_type"] == wat]
                got = _bundle_indices(cands, n)
                if not got:
                    errors.append(f"hire: no {wat} bundles available"); continue
                # Count the workers actually hired into the simulated free pool so a same-turn
                # <staff> can assign them (trained=2 workforce units, untrained=1).
                qmap = {i: q for q, i in cands}
                hired = sum(qmap.get(i, 0) for i in got)
                sim_wf += hired * (2 if trained else 1)
                emit("hire", *got); parsed.append(f"hire {wat} x{n}->{len(got)}act")

            elif cmd == "train":
                p = split(body, 1)
                n = int(float(p[0])) if p else 0
                cands = [(a["worker"]["quantity"], i) for i, a in idx.get("worker", [])
                         if a["worker"]["worker_action_type"] == "train_untrained"]
                got = _bundle_indices(cands, n)
                if not got:
                    errors.append("train: no train bundles available"); continue
                emit("train", *got); parsed.append(f"train x{n}->{len(got)}act")

            elif cmd == "staff":
                p = split(body, 2)
                if not p:
                    errors.append(f"staff: need BUILDING,N got '{body}'"); continue
                # Defer: resolve after the whole text is scanned so workers hired THIS turn
                # (in any textual order) are counted into sim_wf before we assign them.
                staff_cmds.append((p[0], int(float(p[1]))))

            elif cmd == "deconstruct":
                bname = body.strip().lower()
                hit = next((i for i, a in idx.get("deconstruction", [])
                            if bname in a["deconstruction"]["building_name"].lower()), None)
                if hit is None:
                    errors.append(f"deconstruct: no building matching '{body}'"); continue
                emit("deconstruct", hit); parsed.append(f"deconstruct {body}")

            elif cmd == "task":
                p = split(body, 2)
                if not p:
                    errors.append(f"task: need TASK_ID,CHOICE_ID got '{body}'"); continue
                choices.append({"taskId": int(float(p[0])), "choiceId": int(float(p[1]))})
                parsed.append(f"task {p[0]}/{p[1]}")

            elif cmd == "transfer":
                # Manual resource transfer (only enumerated when the env runs with manual_transfers).
                # <transfer>RESOURCE,SOURCE,DEST,QTY</transfer> e.g. food,Community01,Motel,25
                p = split(body, 4)
                if not p:
                    errors.append(f"transfer: need RESOURCE,SOURCE,DEST,QTY got '{body}'"); continue
                rtype = _TRANSFER_RESOURCE.get(p[0].lower().replace(" ", ""))
                if not rtype:
                    errors.append(f"transfer: unknown resource '{p[0]}'"); continue
                src, dst = p[1].strip().lower(), p[2].strip().lower()
                qty = int(float(p[3]))
                cands = [(t["transfer"]["quantity"], i) for i, t in idx.get("resource_transfer", [])
                         if t["transfer"]["resource_type"] == rtype
                         and src in t["transfer"]["source_facility"].lower()
                         and dst in t["transfer"]["destination_facility"].lower()]
                if not cands:
                    errors.append(f"transfer: no {rtype} route {p[1]}->{p[2]} "
                                  f"(needs a free vehicle and a valid facility pair)"); continue
                # pick the offered quantity closest to the requested amount (ties -> larger)
                hit = min(cands, key=lambda qi: (abs(qi[0] - qty), -qi[0]))[1]
                emit("transfer", hit); parsed.append(f"transfer {rtype} {p[1]}->{p[2]} ~{qty}")
        except (ValueError, KeyError, IndexError) as e:
            errors.append(f"{cmd}: parse error '{body}' ({e})")

    # Resolve deferred <staff> now that every <hire> this turn is counted into sim_wf. We synthesize
    # the worker_assignment action directly (Unity's ExecuteAssignment only needs building_name +
    # quantity-in-workforce-units; it greedily pulls from the free pool and ignores worker_type) and
    # append it to env.valid_actions so the gym can execute it by index THIS turn. Capping quantity at
    # the available workforce guarantees TryAssignWorkersToBuilding (all-or-nothing) succeeds rather
    # than failing and aborting the rest of the turn's plan.
    for raw_label, n in staff_cmds:
        bname = raw_label.strip().lower()
        match = next((nm for nm in need if need[nm] > 0 and bname in nm.lower()), None)
        if match is None:
            errors.append(f"staff: '{raw_label}' is not staffable now "
                          f"(must be a built building still needing workers)")
            continue
        want = min(n if n > 0 else need[match], need[match], sim_wf)
        if want <= 0:
            errors.append(f"staff: no free workers for '{raw_label}' "
                          f"(hire workers this turn, or none are available)")
            continue
        synth = {"action_id": f"assign_{match}_{want}", "action_type": "worker_assignment",
                 "description": f"Assign workforce {want} to {match}", "cost": 0,
                 "assignment": {"building_name": match, "worker_type": "untrained", "quantity": want}}
        env.valid_actions.append(synth)
        emit("staff", len(env.valid_actions) - 1)
        sim_wf -= want
        need[match] -= want
        parsed.append(f"staff {match} wf{want}")

    actions = [i for _, i in sorted(act_items, key=lambda kv: kv[0])]
    return {"actions": actions, "choices": choices, "parsed": parsed, "errors": errors}


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
