"""
Flagship-model benchmark for the ARC gym environment.

Runs N full episodes per model with the same prompt pack and observation, then reports
per-model performance and a shared "mistake"
profile. The point is to separate three explanations for poor play:
  (a) some models play well and others don't  -> model decision-making differs
  (b) all models fail the SAME way            -> prompt/observation/env issue
  (c) all models play well                    -> the game is easy / obs is fine

Each episode gets a FRESH headless Unity process (clean game) — the gym server has
no in-place reset, so we relaunch per episode on a per-worker port. Episodes can run
concurrently across workers (each worker owns one port + one Unity process).

This is an EVAL harness: it reuses GameEnv.reset()/step() and the smoke-test's
summarize()/ask()/prompt verbatim — it is not a new rollout engine and does not patch
the env. The score is cora.scoring's (the gym reports it per round).

Usage:
  python benchmark_models.py [--episodes N] [--rounds R] [--workers K]
                             [--models m1,m2,...] [--out DIR] [--validate]

  --validate runs ONE 2-round no-LLM (no-op) episode to confirm the fresh-process
  lifecycle works before spending any API budget.
"""
import os, re, sys, json, argparse, traceback, queue, base64, hashlib
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed

sys.path.insert(0, str(Path(__file__).parent))
from cora.env import GameEnv
from cora.llm import Provider, ProviderSpec, client_for, reasoning_of
from cora import prompts as cora_prompts  # prompt packs (prompts/*.json), the single prompt source
from cora.observation import ObsConfig, observe, render as render_obs
from cora import executor               # typed tool calls -> game actions

# Platform-aware headless executable paths. Default to the macOS .app on darwin; on the
# GPU cluster the Linux Dedicated Server build (HeadlessBuildScript.BuildLinux) is used.
# Override either with an env var (ARC_HEADLESS_EXE / ARC_RENDER_EXE) for non-standard layouts.
_HEADLESS_EXE_BY_PLAT = {
    "darwin": "Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/ARC_DisasterSimulation",
    "linux":  "Build/Headless/Linux/ARC_Headless.x86_64",
    "win":    "Build/Headless/Windows/ARC_Headless.exe",
}
_RENDER_EXE_BY_PLAT = {
    "darwin": "Build/HeadlessRender/macOS/ARC_HeadlessRender.app/Contents/MacOS/ARC_DisasterSimulation",
    "linux":  "Build/HeadlessRender/Linux/ARC_HeadlessRender.x86_64",
    "win":    "Build/HeadlessRender/Windows/ARC_HeadlessRender.exe",
}
def _plat_key():
    if sys.platform.startswith("win"):
        return "win"
    return "linux" if sys.platform.startswith("linux") else "darwin"

HEADLESS_EXE = os.environ.get("ARC_HEADLESS_EXE") or _HEADLESS_EXE_BY_PLAT[_plat_key()]
# Player build with graphics kept (launched WITHOUT -nographics) — needed only for the
# real-image arm, which captures the live game frame at decision time. Synthetic/none arms
# use the faster non-rendering Server build above. On Linux the render build must be launched
# under a virtual display (xvfb-run); the Server build above needs no display.
RENDER_EXE = os.environ.get("ARC_RENDER_EXE") or _RENDER_EXE_BY_PLAT[_plat_key()]
MAP_GRID_JSON = "arc_map_grid.json"   # static tile lattice for the synthetic renderer
BASE_PORT = 9900
# --map-config / ARC_MAP_CONFIG: map JSON for every episode ("none" = the scene's built-in layout).
MAP_CONFIG = os.environ.get("ARC_MAP_CONFIG") or None

# Cross-vendor flagships available on the CMU gateway (edit via --models).
DEFAULT_MODELS = [
    "us.anthropic.claude-opus-4-8",
    "us.anthropic.claude-sonnet-4-6",
    "gpt-5.5",
    "gpt-5.4-pro",
    "gemini/gemini-3.1-pro-preview",
    "gemini-2.5-pro",
]


# ── Robust chat: gateway models disagree on token-limit param name ──────────
# Visible-answer headroom is added ON TOP of the reasoning budget so a higher effort never
# starves the JSON decision. Measured reasoning_tokens on a small planning prompt:
#   gpt-5-mini  low=256  medium=1152 high=3840   |  gemini-2.5-flash low=802 medium=1360 high=1478
# Real game prompts are larger, so we pad generously; the actual spend is logged per round.
_EFFORT_BUDGET = {"none": 2000, "low": 6000, "medium": 12000, "high": 20000, "xhigh": 24000}

# Set in main() when --base-url points at a local OpenAI-compatible server (Ollama). Ollama
# AUTO-ENABLES thinking on reasoning-capable models (qwen3, qwen3.5, gpt-oss) unless the request
# carries reasoning_effort, so the hidden chain-of-thought eats the token budget before any action
# tag appears. We forward the CLI --reasoning_effort here: "none" turns thinking off (~2-5 tok),
# low/medium/high cap it. Left None on the CMU-gateway path (Claude rejects the knob; gpt-5*/gemini
# are handled in their own branch of chat()).
LOCAL_REASONING_EFFORT = None


def _set_local_reasoning_effort(effort):
    global LOCAL_REASONING_EFFORT
    LOCAL_REASONING_EFFORT = effort


# Explicit total-generation budget (reasoning + visible answer) for LOCAL models, set from
# --max_tokens. When None, the effort floor in _EFFORT_BUDGET applies (legacy behavior). When set,
# it OVERRIDES that floor — including below it — so you can deliberately tighten the budget (e.g.
# 3000 with effort=low) to probe how the model copes with limited room. Local path only.
LOCAL_MAX_TOKENS = None


def _set_local_max_tokens(n):
    global LOCAL_MAX_TOKENS
    LOCAL_MAX_TOKENS = n


# Chat-template kwargs forwarded on the LOCAL path, e.g. {"enable_thinking": False}.
# MEASURED on qwen3:4b (real tools prompt, 6k-char observation), completion tokens vs
# visible content:
#     reasoning_effort=none               3065 tok   11321 chars of content
#     native /api/chat think=false        4488 tok   16259 chars
#     enable_thinking=false                4861 tok      94 chars   <-- this
#     "do NOT deliberate" in the prompt   4228 tok   16249 chars   (ignored)
# NOTE WHAT THIS DOES AND DOES NOT DO: it does NOT reduce generation. The model emits
# ~3-5k tokens regardless; enable_thinking=false only makes the server strip them out of
# `content` instead of handing them back. That is still worth having -- it removes any
# chance of the harness parsing deliberation as commands, and makes transcripts readable
# -- but it buys correctness, not speed. Nothing in Ollama's surface makes this model
# think less; that is the model.
LOCAL_CHAT_TEMPLATE_KWARGS = None


def _set_local_chat_template_kwargs(d):
    global LOCAL_CHAT_TEMPLATE_KWARGS
    LOCAL_CHAT_TEMPLATE_KWARGS = d


def _is_anthropic(model):
    m = model.lower()
    return "anthropic" in m or "claude" in m

ANTHROPIC_TEMP_MAX = 1.0   # Bedrock/Anthropic reject temperature > 1.0 (gpt-5* ignore temp; gemini allows >1)


def chat(client, model, messages, max_tokens=2000, reasoning_effort="low", temperature=None):
    """OpenAI-compatible call that tolerates per-vendor param quirks.

    Returns (content, reasoning_trace, reasoning_tokens) — reasoning_trace is the provider's
    hidden chain-of-thought when the gateway surfaces it (reasoning_content / reasoning), else
    None; reasoning_tokens is the usage-reported hidden-thinking token count (None if absent).
    content is the visible message text (may itself contain <think>…).
    """
    ml = model.lower()
    is_gpt5 = ml.startswith("gpt-5") or "/gpt-5" in ml
    is_gemini = "gemini" in ml   # Gemini 2.5/3.x are thinking models (see below)
    if is_gpt5 or is_gemini:
        # Thinking models (gpt-5*, Gemini 2.5/3.x) spend a large HIDDEN reasoning budget
        # before any visible text. With a tight token cap the hidden thinking eats the whole
        # budget and the visible answer is empty (gpt-5) or truncated mid-JSON (gemini) — which
        # showed up as ~31/32 parse failures per gemini episode. Give headroom that scales with
        # reasoning_effort so a complete, compact decision still comes back at higher effort.
        # gpt-5 wants max_completion_tokens; gemini wants max_tokens (per the gateway).
        budget = max(max_tokens, _EFFORT_BUDGET.get(reasoning_effort, 6000))
        kw = dict(model=model, messages=messages, reasoning_effort=reasoning_effort)
        kw["max_completion_tokens" if is_gpt5 else "max_tokens"] = budget
        # gpt-5* reject a temperature knob (only default is allowed); Gemini 2.5/3.x accept it, so
        # temperature is the exploration axis there. Send it only where it is honored.
        if temperature is not None and is_gemini:
            kw["temperature"] = temperature
        try:
            r = client.chat.completions.create(**kw)
        except Exception:
            kw.pop("reasoning_effort", None)   # some snapshots reject the knob; retry without it
            r = client.chat.completions.create(**kw)
    else:
        kw = dict(model=model, max_tokens=max_tokens, messages=messages)
        if temperature is not None:
            kw["temperature"] = temperature
        if LOCAL_REASONING_EFFORT is not None:
            # Local Ollama OpenAI-compat endpoint: thinking auto-enables on reasoning-capable
            # models unless reasoning_effort is set. Forward it ("none" disables thinking) and give
            # the visible answer the same token headroom as the gateway thinking branch so any
            # retained chain-of-thought can't starve the action tag.
            if not LOCAL_CHAT_TEMPLATE_KWARGS:      # see the conflict note in ask_tools
                kw["reasoning_effort"] = LOCAL_REASONING_EFFORT
            kw["max_tokens"] = (LOCAL_MAX_TOKENS if LOCAL_MAX_TOKENS is not None
                                else max(max_tokens, _EFFORT_BUDGET.get(LOCAL_REASONING_EFFORT, max_tokens)))
            if LOCAL_CHAT_TEMPLATE_KWARGS:
                kw["extra_body"] = {**kw.get("extra_body", {}),
                                    "chat_template_kwargs": LOCAL_CHAT_TEMPLATE_KWARGS}
        try:
            r = client.chat.completions.create(**kw)
        except Exception as e:
            msg = str(e).lower()
            retried = False
            if "temperature" in msg and "temperature" in kw:
                kw.pop("temperature", None); retried = True   # vendor rejected the temp value
            if ("reasoning" in msg or "think" in msg) and "reasoning_effort" in kw:
                kw.pop("reasoning_effort", None); retried = True   # non-thinking local model
                # Ollama phrases this as '"<model>" does not support thinking' (no "reasoning"),
                # so match "think" too; otherwise non-reasoning models error out with 0 rounds.
            if "max_tokens" in msg or "max_completion_tokens" in msg:
                kw.pop("max_tokens", None)
                kw["max_completion_tokens"] = max_tokens; retried = True
            if retried:
                r = client.chat.completions.create(**kw)
            else:
                raise
    m = r.choices[0].message
    return (m.content or ""), *reasoning_of(r)


def _user_msg(text, image_b64=None):
    """Build a user message, multimodal when an image is supplied. The image is a
    decision-time view of the same state (synthetic dashboard or real game frame)."""
    if not image_b64:
        return {"role": "user", "content": text}
    return {"role": "user", "content": [
        {"type": "text", "text": text},
        {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{image_b64}"}}]}


def ask_tools(client, model, state, env, system_text, image_b64=None, reasoning_effort="low",
              temperature=None, obs_encoding="json", history=None, prev_state=None):
    """One model call: system prompt + tool schema + the current observation; the model may reason,
    then emits tool calls. Returns (decision, raw_content, reasoning_trace, reasoning_tokens, None):
    the calls themselves go to cora.executor in the round loop, which also decides parsed_ok
    (whether every call was well-formed). The tool schema is cora.tools' — the same one the RL
    policy trains on and the officer router offers."""
    from cora.tools import openai_tools
    _mt = getattr(env, "manual_transfers", True)
    if obs_encoding == "delta":
        rendered = render_obs(state, prev=prev_state)
    elif obs_encoding == "compact":
        rendered = render_obs(state)
    else:
        rendered = json.dumps(state)
    user_text = "State:\n" + rendered + "\n\nAct by calling the tools."
    msgs = [{"role": "system", "content": system_text}]
    if history:
        msgs.extend(history)
    msgs.append(_user_msg(user_text, image_b64))
    tools = openai_tools(manual_transfers=_mt)
    kw = dict(model=model, messages=msgs, tools=tools, max_tokens=2000)
    # LOCAL path (Ollama) parity with chat(): a reasoning-capable local model auto-enables
    # thinking unless reasoning_effort is sent, and several of them emit a long prose preamble
    # BEFORE the tool call — a hard 2000 cap truncates them before any tool_call is produced.
    # Both knobs are None on the gateway path, leaving that behavior untouched.
    # MEASURED CONFLICT: sending reasoning_effort ALONGSIDE enable_thinking=false defeats it
    # -- Ollama then leaks the whole chain-of-thought back into `content` (median 16.6k chars
    # over 3 samples, vs 0 with the template kwarg alone). Same token count either way, so
    # this is purely about which channel it lands in. enable_thinking wins when both are set.
    if LOCAL_REASONING_EFFORT is not None and not LOCAL_CHAT_TEMPLATE_KWARGS:
        kw["reasoning_effort"] = LOCAL_REASONING_EFFORT
    if LOCAL_MAX_TOKENS is not None:
        kw["max_tokens"] = LOCAL_MAX_TOKENS
    if LOCAL_CHAT_TEMPLATE_KWARGS:
        # MUST go through extra_body: the OpenAI SDK rejects unknown top-level params, and
        # passing it directly got silently swallowed by the retry handler -- the run looked
        # fine and thinking stayed ON (16k chars of content instead of ~0).
        kw["extra_body"] = {**kw.get("extra_body", {}),
                            "chat_template_kwargs": LOCAL_CHAT_TEMPLATE_KWARGS}
    if temperature is not None:
        kw["temperature"] = temperature
    try:
        r = client.chat.completions.create(**kw)
    except Exception as e:
        emsg = str(e).lower()
        if "temperature" in emsg:
            kw.pop("temperature", None)
        if "max_tokens" in emsg or "max_completion_tokens" in emsg:
            kw.pop("max_tokens", None)
            kw["max_completion_tokens"] = LOCAL_MAX_TOKENS or 2000
        if ("reasoning" in emsg or "think" in emsg) and "reasoning_effort" in kw:
            kw.pop("reasoning_effort", None)   # non-thinking model rejects the knob
        if "chat_template" in emsg or "template" in emsg:
            kw.pop("extra_body", None)
        r = client.chat.completions.create(**kw)
    m = r.choices[0].message
    content = m.content or ""
    raw_tcs = getattr(m, "tool_calls", None) or []

    if history is not None:
        history.append(_user_msg(user_text, None))
        # HISTORY SERIALIZATION BUG (fixed): this used to append
        #     {"role": "assistant", "content": content or tags}
        # so a pure tool-call turn (content == "") was recorded as if the assistant had SPOKEN
        # the command-tag text. The model then few-shot-imitated its own apparent output format
        # and emitted "<staff>Kitchen_0,4</staff>" as plain text -- which the executor never
        # reads (it only looks at message.tool_calls), so the action was silently dropped with
        # no error and no state change. Self-reinforcing: once it emits text, content is
        # non-empty, so history keeps teaching text-mode. Measured on 32-episode runs:
        # Qwen3-4B h=4 937/1024 rounds (91.5%) corrupted, Qwen3-14B 397 (38.8%),
        # Qwen3.5-27B 153 (14.9%), and 0/1024 at history=1 -- median onset round 2, the first
        # round in which a prior assistant turn exists. 3,738 well-formed actions destroyed in
        # the 4B run alone. Record the real tool_calls instead, so the replayed history shows
        # the model the channel it must actually use.
        if raw_tcs:
            history.append({"role": "assistant", "content": content or None,
                            "tool_calls": [{"id": tc.id, "type": "function",
                                            "function": {"name": tc.function.name,
                                                         "arguments": tc.function.arguments}}
                                           for tc in raw_tcs]})
            for tc in raw_tcs:
                history.append({"role": "tool", "tool_call_id": tc.id, "content": "ok"})
        else:
            history.append({"role": "assistant", "content": content})
    reason = ""
    mr = re.search(r"REASONING:\s*(.+)", content or "")
    if mr:
        reason = mr.group(1).splitlines()[0].strip()
    # Run by executor.execute_turn in the round loop; nothing is returned to the model.
    # Zero calls is a deliberate no-op, not a failure.
    dec = {"tool_calls": list(raw_tcs), "reasoning": reason}
    rtrace, rtok = reasoning_of(r)
    return dec, content, rtrace, rtok, None


# ── Non-learning baseline policies (operate on the full env, not the prompt) ──
from cora.scoring import REWARD_WEIGHTS

# $ -> value in the baselines' task-choice heuristic (spend is weighed against acting on demand).
_CHOICE_COST_WEIGHT = 0.0002


def _impacts_dict(choice):
    return {i.get("type"): i.get("value", 0) for i in (choice.get("impacts") or [])}


_DEBUG_PIPELINE = os.environ.get("ARC_DEBUG_PIPELINE", "").lower() in ("1", "true", "yes")


def _legacy_dest_from_text(choice_text):
    """The OLD choiceText substring heuristic — kept ONLY for the debug pipeline check below,
    so we can flag where the new structured field disagrees with it (those disagreements are
    the bug class the structured field was added to eliminate, e.g. a casework group whose name
    contains 'Motel'/'Shelter')."""
    tl = (choice_text or "").lower()
    cw = "casework" in tl
    motel = ("motel" in tl) and not cw
    shel = ("shelter" in tl) and not motel and not cw
    return "CaseworkSite" if cw else "Motel" if motel else "Shelter" if shel else ""


def _debug_choice_pipeline(gs, rnd):
    """Validate the Unity->Python choice-destination pipeline (enable via ARC_DEBUG_PIPELINE=1).

    Confirms the structured destinationCategory/deliveryQuantity fields actually arrive from the
    Unity build, and flags two failure modes:
      * MISSING  — a delivery choice arrived with NO destinationCategory (serialization broken /
                   stale build that predates the TaskChoiceBrief change).
      * MISMATCH — the structured field disagrees with the legacy text heuristic. When the
                   structured value is the correct one (e.g. CaseworkSite for a '..._to_Motel'
                   group) this is the bug the fix resolves; it proves the new path is live.
    Prints a per-round summary plus a line per anomaly. No-op unless the env var is set."""
    if not _DEBUG_PIPELINE:
        return
    seen = missing = mismatch = 0
    cats = {}                                        # destinationCategory -> count
    qtys = []
    for t in (gs.get("allActiveTasks") or []):
        for c in (t.get("choices") or []):
            txt = c.get("choiceText") or ""
            dest = c.get("destinationCategory") or ""
            heur = _legacy_dest_from_text(txt)
            b = float(_impacts_dict(c).get("Budget", 0) or 0)
            if dest:
                seen += 1
                cats[dest] = cats.get(dest, 0) + 1
                if c.get("deliveryQuantity"):
                    qtys.append(c.get("deliveryQuantity"))
                if heur and dest in ("CaseworkSite", "Motel", "Shelter") and dest != heur:
                    mismatch += 1
                    # The case the structured field FIXES: e.g. a casework group named
                    # '..._to_Motel' that the old substring heuristic would call Motel.
                    print(f"    [PIPE r{rnd}] MISMATCH(fix) struct={dest!r} heur={heur!r} "
                          f"qty={c.get('deliveryQuantity')} text={txt!r}")
            elif heur == "Motel" and b <= 0:
                # 'motel' only ever appears in relocation/delivery choices: a missing dest here
                # means the field never made it across (broken or stale build).
                missing += 1
                print(f"    [PIPE r{rnd}] MISSING destinationCategory; text implies Motel: {txt!r}")
    print(f"    [PIPE r{rnd}] with_dest={seen} missing={missing} struct!=heur={mismatch} "
          f"cats={cats} qtys={qtys}")


def greedy_decision(env):
    """Myopic, reward-mirrored greedy baseline (no learning, no API).

    Choices: per task pick the choice maximizing a reward-mirrored value built from
      the exposed impacts — funding (Budget>0) is scaled, demand fulfillment is worth
      ~w_food/w_lodging, costs are penalized with the reward's w_*_cost. Take the best
      if its value > 0; skip otherwise.
    Actions: assign free workers to NeedWorker buildings (cost 0, immediately enables
      InUse -> fulfillment + worker-use). Deliberately does NOT build/hire/train — those
      cost now and pay later, so a strictly myopic policy skips them (the under-investment
      is the intended diagnostic; the discounted-flow variant adds them)."""
    gs = env.game_state or {}
    va = env.valid_actions or []
    choices = []
    for t in gs.get("allActiveTasks", []) or []:
        tcs = t.get("choices") or []
        if not tcs:
            continue
        demand = t.get("taskType") in ("Demand", "Emergency")
        best, best_v = None, 0.0
        for c in tcs:
            imp = _impacts_dict(c)
            b = float(imp.get("Budget", 0) or 0)
            s = float(imp.get("Satisfaction", 0) or 0)
            if b > 0:                                   # funding choice
                v = b / 10000.0 + 0.01 * s
            else:                                       # acting / waiting
                cost = -b
                acting = (cost > 0) or (s >= 10)
                v = (1.0 if (acting and demand) else 0.0) + 0.01 * s - _CHOICE_COST_WEIGHT * cost
            if v > best_v:
                best_v, best = v, c
        if best is not None:
            choices.append({"taskId": t["taskId"], "choiceId": best["choiceId"]})

    # worker assignment: worker_assignment actions nest their fields under
    # a["assignment"] (to_dict pops the top-level building_name/worker_type/quantity).
    # The enumerator only emits these for buildings that need workers AND when free
    # workers exist, so take them directly (prefer trained; fill each building once).
    wf = gs.get("workforceState", {}) or {}
    ft = int(wf.get("freeTrainedWorkers", 0) or 0)
    fu = int(wf.get("freeUntrainedWorkers", 0) or 0)
    by_building = {}
    for i, a in enumerate(va):
        if a.get("action_type") == "worker_assignment":
            asg = a.get("assignment") or {}
            by_building.setdefault(asg.get("building_name"), []).append((i, asg))
    actions = []
    for bname, cands in by_building.items():
        for i, asg in sorted(cands, key=lambda x: (x[1].get("worker_type") != "trained",
                                                   -(x[1].get("quantity") or 0))):
            wt, q = asg.get("worker_type"), int(asg.get("quantity") or 0)
            avail = ft if wt == "trained" else fu
            if 0 < q <= avail:
                actions.append(i)
                if wt == "trained":
                    ft -= q
                else:
                    fu -= q
                break
    return {"choices": choices, "actions": actions,
            "note": "greedy", "reasoning": "myopic reward-mirrored: fulfill+fund via best choice, staff NeedWorker buildings"}


# ── Potential-shaping baseline (greedy selection + a hand-crafted state potential) ──
# Builds shelter/kitchen capacity toward anticipated demand (anchored to community
# population, capped by the empirical arrival rate, horizon-discounted), staffs them
# to claim worker_use, and fulfills via the cheapest *effective* option.
_POT_MODE = os.environ.get("POT_MODE", "baseline")     # "baseline" | "demandsupply"
_POT_DS_COVERAGE = float(os.environ.get("POT_DS_COVERAGE", "1.0"))  # shelter-cap target as fraction of P
_POT_MIN_HORIZON = int(os.environ.get("POT_MIN_HORIZON", "4"))   # don't build with fewer rounds left
_POT_KITCHEN_TARGET = int(os.environ.get("POT_KITCHEN_TARGET", "2"))  # operational kitchens to aim for
_POT_CASEWORK_BUILD_ROUND = 0  # build the casework site EARLY. The workforce is capped (~3-4 operational
                               # buildings), and a building only gets staffed if free workers exist when it's
                               # built — deferring the casework build to ~round 8 left it permanently
                               # NeedWorker (workers already committed to shelters) → 0 processed. Building it
                               # first claims its 4 workers up front, which is the only way it stays operational.
                               # The cost (~one shelter's staffing → lower lodging) is inherent to the worker cap.
_POT_BUDGET_RESERVE = float(os.environ.get("POT_BUDGET_RESERVE", "1500"))  # reserve before discretionary building
_POT_SHELTER_COVERAGE = 1e9  # θ: route lodging to free shelter only when space >= θ×need.
                             # Set huge = OFF: deferred shelter relocations are unreliable
                             # (travel/expiry) and cost lodging fulfillment vs the reliable
                             # immediate option, so free-shelter routing is disabled by
                             # default. Lower (e.g. 1.0) to re-enable the cost-vs-fulfillment trade.
# ── shared: move people into shelters we already paid for ────────────────────
# Both rules-based policies BUILD and STAFF shelters and then never fill them: measured
# across 10 episodes each, shelter population was 0/3500 (rules-based) and 0/5000
# (rules-based-v2) while 2,532 and 6,000 people respectively sat in the Motel. The Motel
# bills $200/person/DAY; a staffed shelter is $0/day once built. So the policies were
# paying construction AND the full motel bill, which is why both end deeply negative.
#
# The gap was simply that neither emitted `resource_transfer` actions at all — the
# affordance works (the random baseline used it 1,340 times, and opus 137), it was just
# never in their action set. This helper closes that: drain the Motel first (it is the
# only source that costs money per day), then Communities, into any InUse shelter with
# free beds.
def _fill_shelters_from_costly_sources(env, actions, max_transfers=4):
    """Append transfer-action indices that move people into free shelter capacity.

    Ordering matters: the Motel is drained BEFORE Communities because Motel occupancy is
    the recurring cost. Moving a Community resident into a shelter helps satisfaction but
    saves nothing; moving a Motel resident saves $200/day, every day, for the rest of the
    game.
    """
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = (gs.get("mapState", {}) or {}).get("facilities", []) or []

    free = {}
    for f in facs:
        if f.get("buildingType") == "Shelter" and f.get("buildingStatus") == "InUse":
            spare = (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0)
            if spare > 0:
                free[f.get("facilityName")] = spare
    if not free:
        return

    def _src_rank(name):
        # Motel first (it is the one bleeding money), then anything else.
        return 0 if "motel" in str(name).lower() else 1

    cands = []
    for i, a in enumerate(va):
        if a.get("action_type") != "resource_transfer":
            continue
        tr = a.get("transfer") or {}
        if tr.get("resource_type") == "FoodPacks":
            continue                      # people only; food routing is a separate concern
        dst, src = tr.get("destination_facility"), tr.get("source_facility")
        if dst not in free:
            continue
        cands.append((_src_rank(src), -(tr.get("quantity") or 0), i, dst, tr.get("quantity") or 0))

    cands.sort()                          # motel sources first, largest quantity first
    used = 0
    for _rank, _negq, idx, dst, qty in cands:
        if used >= max_transfers or free.get(dst, 0) <= 0:
            continue
        actions.append(idx)
        free[dst] -= qty
        used += 1


def potential_decision(env, rnd=0, rounds_total=32):
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = gs.get("mapState", {}).get("facilities", []) or []
    budget = float((gs.get("satisfactionAndBudget") or {}).get("budget", 0) or 0)
    rounds_left = max(0, rounds_total - rnd)

    # Reuse greedy's RELIABLE choices + worker assignments (it prefers the acting/
    # immediate options that actually fulfill). Potential adds *building* on top — the
    # free/deferred options fail until infrastructure is stocked, so don't switch to
    # them; keep reliable fulfillment and let building pay off via worker_use + capacity.
    base = greedy_decision(env)
    choices = base["choices"]
    actions = list(base["actions"])  # already includes worker assignments

    # demand anchor: community population (known from round 0); target free shelter
    # capacity ~ P so we can eventually relocate for free instead of paying motel.
    P = sum((f.get("currentPopulation") or 0) for f in facs if f.get("buildingType") == "Community") or 120
    shelter_cap = sum((f.get("populationCapacity") or 0) for f in facs if f.get("buildingType") == "Shelter")
    n_kitchens = sum(1 for f in facs if f.get("buildingType") == "Kitchen")
    n_casework = sum(1 for f in facs if f.get("buildingType") == "CaseworkSite")
    tasks_by_id = {t["taskId"]: t for t in (gs.get("allActiveTasks") or [])}

    def is_lodging(t):
        return t and any(k in (t.get("taskTitle") or "") for k in ("Relocation", "Population", "Lodging"))

    def find_build(btype):
        cands = [(i, a) for i, a in enumerate(va) if a.get("action_type") == "construction"
                 and (a.get("construction") or {}).get("building_type") == btype]
        return min(cands, key=lambda x: x[1].get("cost") or 0) if cands else None

    # ════════════════════════════════════════════════════════════════════════════
    # DEMAND-SUPPLY MODE (POT_MODE=demandsupply): grounded in the Unity audit —
    #   Motel = $0 upfront but $200/person/DAY recurring (MotelCostManager).
    #   Shelter = $1000 + 4 workers, then $0/day forever (10 beds).
    # So the cost-optimal housing is a STAFFED shelter, and the cheapest *reliable*
    # way to fill it is the $3000 immediate Helicopter-to-Shelters (~$75/person once
    # for a 40-person community) — vs the "free" motel that bills $200/person/day.
    # Policy: build+staff shelter capacity toward demand (≈ community pop P), then
    # route each relocation into shelter space via the reliable immediate helicopter
    # while shelter beds last; spill to the motel only when shelters are full.
    # ════════════════════════════════════════════════════════════════════════════
    if _POT_MODE == "demandsupply":
        shel_free = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                        for f in facs if f.get("buildingType") == "Shelter"
                        and f.get("buildingStatus") == "InUse")
        pop_by_fac = {f.get("facilityName"): (f.get("currentPopulation") or 0) for f in facs}

        def _pick(cs, *kws, paid=None):
            for c in cs:
                txt = (c.get("choiceText") or "").lower()
                if all(k in txt for k in kws):
                    has_cost = bool(_impacts_dict(c).get("Budget"))
                    if paid is None or has_cost == paid:
                        return c
            return None

        for ch in choices:
            t = tasks_by_id.get(ch["taskId"])
            if not is_lodging(t):
                continue
            cs = t.get("choices") or []
            need = pop_by_fac.get(t.get("affectedFacility")) or 40
            if shel_free >= need:
                # reliable immediate evac INTO a staffed shelter ($0/day thereafter)
                pick = (_pick(cs, "helicopter", "shelter", paid=True)
                        or _pick(cs, "evacuation", "shelter")
                        or _pick(cs, "shelter", paid=False))
                if pick:
                    ch["choiceId"] = pick["choiceId"]
                    shel_free -= need
            # else: shelters full → leave greedy's choice (motel/helicopter spill)

        # build+staff shelters toward demand coverage, then kitchens for food + workers
        if rounds_left >= _POT_MIN_HORIZON and budget >= _POT_BUDGET_RESERVE:
            target = None
            if shelter_cap < _POT_DS_COVERAGE * P:
                target = find_build("Shelter")
            if target is None and n_kitchens < _POT_KITCHEN_TARGET:
                target = find_build("Kitchen")
            if target and (target[1].get("cost") or 0) <= budget - _POT_BUDGET_RESERVE:
                actions.append(target[0])
                budget -= (target[1].get("cost") or 0)

        wf = gs.get("workforceState", {}) or {}
        free_workers = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
        need_w = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
                     for f in facs if f.get("buildingStatus") == "NeedWorker")
        if need_w > free_workers and budget >= _POT_BUDGET_RESERVE:
            for i, a in enumerate(va):
                if (a.get("action_type") == "worker"
                        and (a.get("worker") or {}).get("worker_action_type") == "hire_untrained"
                        and (a.get("cost") or 0) <= budget - _POT_BUDGET_RESERVE):
                    actions.append(i)
                    break
        _fill_shelters_from_costly_sources(env, actions)
        return {"choices": choices, "actions": actions, "note": "potential-ds",
                "reasoning": f"demandsupply: P={P} shelterCap={shelter_cap} shelFree={shel_free} kitchens={n_kitchens}"}

    # ── NO motel-routing override. We tried forcing the $3000 immediate Helicopter-to-Motel
    # for every lodging task; it REGRESSED reward (1.44 -> 1.35) and pinned cost_lodging at the
    # cap (1.0 = $5000+ spent per person housed). cost_lodging is NOT structurally capped — it
    # caps only when you overspend per fulfilled relocation. Greedy's myopic choice already
    # PREFERS the free "Send to Shelters/Motel" (cost 0 → scores higher than the −$0.6 helicopter),
    # which houses people at $0 when it completes, keeping cost_lodging ~0.78 (uncapped). The real
    # lodging bottleneck is FULFILLMENT reliability (lodgingFulfilled ~6 of ~14 resolved), which
    # caps sat_lodging AND inflates $/person together — not the cost term itself. ──

    # ── free-shelter-when-ready (demand-aware): for lodging tasks, switch from greedy's
    # reliable paid choice to the free "Send to Shelters" option ONLY when free shelter
    # space fully covers that task's relocation need — so the deferred relocation
    # completes (no partial-fulfillment loss). Need = the affected community's population
    # (no fixed guess). θ=_POT_SHELTER_COVERAGE dials aggressive(<1) ↔ conservative(=1). ──
    free_shelter_space = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                             for f in facs if f.get("buildingType") == "Shelter")
    if free_shelter_space > 0:
        pop_by_facility = {f.get("facilityName"): (f.get("currentPopulation") or 0) for f in facs}
        for ch in choices:
            t = tasks_by_id.get(ch["taskId"])
            if not is_lodging(t):
                continue
            need = pop_by_facility.get(t.get("affectedFacility")) or 40  # 40 = a community
            if free_shelter_space >= _POT_SHELTER_COVERAGE * need:
                free_shelter = next((c for c in (t.get("choices") or [])
                                     if c.get("destinationCategory") == "Shelter"
                                     and not _impacts_dict(c).get("Budget")), None)
                if free_shelter:
                    ch["choiceId"] = free_shelter["choiceId"]   # override to free option
                    free_shelter_space -= need

    # ── build (the potential term): only with enough horizon + budget headroom ──
    def find_build(btype):
        cands = [(i, a) for i, a in enumerate(va) if a.get("action_type") == "construction"
                 and (a.get("construction") or {}).get("building_type") == btype]
        return min(cands, key=lambda x: x[1].get("cost") or 0) if cands else None
    # Build order: shelters toward demand (free relocation destinations) + kitchens (food+workers)
    # EARLY, then ONE casework site once round >= _POT_CASEWORK_BUILD_ROUND — deferring it keeps the
    # capped workforce on shelters during the early relocation waves, then staffs casework just before
    # the first return-home requests (~round 13). Once it's up, greedy's choice logic routes
    # "Casework Request" tasks to it (no smarter policy needed).
    if rounds_left >= _POT_MIN_HORIZON and budget >= _POT_BUDGET_RESERVE:
        target = None
        if n_casework < 1 and rnd >= _POT_CASEWORK_BUILD_ROUND:
            target = find_build("CaseworkSite")
        if target is None and shelter_cap < P:
            target = find_build("Shelter")
        if target is None and n_kitchens < _POT_KITCHEN_TARGET:
            target = find_build("Kitchen")
        if target and (target[1].get("cost") or 0) <= budget - _POT_BUDGET_RESERVE:
            actions.append(target[0])
            budget -= (target[1].get("cost") or 0)

    # (worker assignments are already in `base` from greedy_decision — don't redo them)

    # ── hire (untrained) if buildings need more workers than we have free ──
    wf = gs.get("workforceState", {}) or {}
    free_workers = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
    need = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
               for f in facs if f.get("buildingStatus") == "NeedWorker")
    if need > free_workers and budget >= _POT_BUDGET_RESERVE:
        for i, a in enumerate(va):
            if (a.get("action_type") == "worker"
                    and (a.get("worker") or {}).get("worker_action_type") == "hire_untrained"
                    and (a.get("cost") or 0) <= budget - _POT_BUDGET_RESERVE):
                actions.append(i)
                break

    _fill_shelters_from_costly_sources(env, actions)
    return {"choices": choices, "actions": actions, "note": "rules-based",
            "reasoning": f"rules-based: P={P} shelterCap={shelter_cap} kitchens={n_kitchens} casework={n_casework} roundsLeft={rounds_left}"}


# ── Improved rules-based ("rules-based-v2") ──────────────────────────────────
# Addresses the four documented flaws of the baseline rules-based policy:
#   (1) multi-action turns: build several buildings AND hire several workers in one turn
#       (the env executes a list of actions; the baseline self-capped at 1 build + 1 hire);
#   (2) forward capital investment: size shelter capacity to the FULL known displaced
#       population P up front (ahead of the demand waves), not one shelter at a time;
#   (3) geographic site selection: rank available build sites by proximity to the
#       communities they serve (shorter, safer relocation routes), de-prioritizing
#       flood-blocked sites where identifiable — the baseline picked an arbitrary site;
#   (4) deploy reserves: invest the budget down to a small operating buffer instead of
#       hoarding a fixed reserve that never gets spent.
# Principled bound: STAFFING is the bottleneck (~4 workers/building), so building is paced by a
# staffable pipeline — never queue more buildings than the workforce can plausibly clear over the
# horizon. That bound is enforced by `budget_buildings` (max_buildings - existing) below.
# INCOME PACING (_V2_BUILD_PER_TURN): building is also capped per turn. This is NOT arbitrary
# throttling — funding arrives at ~$2-3k/round and a building+staffing costs ~$1k, so deploying
# faster than ~3/turn exhausts starting capital before the disaster peaks, leaving no cash to build
# shelters as population crests. Removing this cap halved lodging (0.85 -> 0.40) and cut reward 20%
# (n=10). The separate concurrent-unstaffed ("pipeline") cap was REMOVED as redundant — with
# per-turn pacing plus multi-worker hiring, each turn's new buildings staff within a round; n=10
# confirmed no regression. The operating buffer is a FLAT reserve held before discretionary
# building, sized to fund a reactive paid-fulfilment wave (food airlift ~$1-3k); a demand-SCALED
# buffer was tested and regressed lodging 0.85 -> 0.51 (it ballooned to ~$5.7k mean during food
# waves and starved shelter construction at the population peak), so the flat value is kept.
_V2_BUILD_PER_TURN = 3      # income-paced: max buildings deployed per turn (see note above)
_V2_OP_BUFFER = 3000        # flat cash reserve kept before discretionary building (see note above)
_V2_SHELTER_BEDS = 10       # population capacity per shelter


def _vec_dist(a, b):
    if not a or not b:
        return 0.0
    dx = (a.get("x", 0) or 0) - (b.get("x", 0) or 0)
    dy = (a.get("y", 0) or 0) - (b.get("y", 0) or 0)
    dz = (a.get("z", 0) or 0) - (b.get("z", 0) or 0)
    return (dx * dx + dy * dy + dz * dz) ** 0.5


_ROUNDS_PER_DAY = 4  # the motel bills per DAY; ~4 rounds per in-game day


def _lt_choice_value(c, demand, rounds_left):
    """Greedy choice value with LONG-TERM cost built in.

    Identical to the myopic greedy value, EXCEPT the motel's recurring $200/person/day is
    charged over the remaining days of the episode and added to the choice's effective cost.
    Over a long horizon this makes the (one-time, then-free) shelter dominate the motel, so
    relocation routing into shelters emerges from the value function itself — no separate
    shelter-routing override needed. Late in the episode (few days left) the motel's small
    remaining bill makes it acceptable again, exactly as it should be.

    Returns (value, fulfils_demand, is_shelter).
    """
    imp = _impacts_dict(c)
    b = float(imp.get("Budget", 0) or 0)
    s = float(imp.get("Satisfaction", 0) or 0)
    if b > 0:                                    # funding choice
        return (b / 10000.0 + 0.01 * s, False, False)
    cost = -b                                    # immediate upfront cost ($)
    dest = c.get("destinationCategory")          # structured field from Unity; None for non-delivery
    is_casework = dest == "CaseworkSite"
    is_motel = dest == "Motel"
    is_shelter = dest == "Shelter"
    # SAME acting predicate as greedy: a real (reliable) action costs money or grants real
    # satisfaction. Free "send to shelter/motel" and free "request from kitchens" choices are
    # non-acting (v contribution 0) — they often fail to complete, so we don't credit them.
    # Routing to shelters instead of the motel comes purely from the motel's recurring penalty
    # below (which sinks paid/free motel options), leaving the reliable shelter option on top.
    acting = (cost > 0) or (s >= 10)
    recurring = 0.0
    if is_motel:                                 # lifetime motel bill over the remaining days
        people = float(c.get("deliveryQuantity") or 20)
        days_left = max(1.0, rounds_left / _ROUNDS_PER_DAY)
        recurring = 200.0 * people * days_left
    v = (1.0 if (acting and demand) else 0.0) + 0.01 * s - _CHOICE_COST_WEIGHT * (cost + recurring)
    return (v, acting and demand, is_shelter)


def improved_rules_based_decision(env, rnd=0, rounds_total=32):
    gs = env.game_state or {}
    va = env.valid_actions or []
    ms = gs.get("mapState", {}) or {}
    facs = ms.get("facilities", []) or []
    budget = float((gs.get("satisfactionAndBudget") or {}).get("budget", 0) or 0)
    rounds_left = max(0, rounds_total - rnd)

    # reactive core: keep greedy's free worker assignments, but REPLACE its myopic choices
    # with long-term-aware ones (the motel's recurring cost is priced in by _lt_choice_value,
    # so relocations route into free shelters automatically — when those shelters have space).
    base = greedy_decision(env)
    actions = list(base["actions"])

    pop_by_fac = {f.get("facilityName"): (f.get("currentPopulation") or 0) for f in facs}
    free_shelter_space = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                             for f in facs if f.get("buildingType") == "Shelter"
                             and f.get("buildingStatus") == "InUse")
    choices = []
    for t in (gs.get("allActiveTasks") or []):
        cs = t.get("choices") or []
        if not cs:
            continue
        demand = t.get("taskType") in ("Demand", "Emergency")
        people = float(pop_by_fac.get(t.get("affectedFacility")) or 20)
        best = None  # (choiceId, value, is_shelter, fulfils)
        for c in cs:
            v, fdem, is_shel = _lt_choice_value(c, demand, rounds_left)
            # don't route into a shelter that lacks space for this relocation (it would
            # defer/fail and lose fulfilment) — push it below the motel fallback instead.
            if is_shel and free_shelter_space < people:
                v -= 100.0
            if best is None or v > best[1]:
                best = (c["choiceId"], v, is_shel, fdem)
        if best and (best[1] > 0 or best[3]):    # take if positive, or it fulfils a real demand
            choices.append({"taskId": t["taskId"], "choiceId": best[0]})
            if best[2] and free_shelter_space >= people:
                free_shelter_space -= people

    op_buffer = float(_V2_OP_BUFFER)   # flat reserve before discretionary building (see constant note)

    communities = [f for f in facs if f.get("buildingType") == "Community"]
    P = sum((f.get("currentPopulation") or 0) for f in communities) or 120
    shelter_cap = sum((f.get("populationCapacity") or 0) for f in facs if f.get("buildingType") == "Shelter")
    n_kitchens = sum(1 for f in facs if f.get("buildingType") == "Kitchen")
    n_casework = sum(1 for f in facs if f.get("buildingType") == "CaseworkSite")

    # (2) FORWARD INVESTMENT, staffing-aware. Food is a FULL reward point but is hard-gated by a
    # kitchen (no kitchen -> no food packs -> 0 food), so kitchens come BEFORE shelters. And we
    # never queue more capacity than we can plausibly STAFF over the horizon: hiring is capped at
    # 5/day and each building needs ~4 workers, so chasing all of P (12 shelters) just creates
    # unstaffable buildings. Cap total buildings to (current workers + future hires) / 4.
    wf = gs.get("workforceState", {}) or {}
    total_workers = (int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
                     + int(wf.get("workingTrainedWorkers", 0) or 0) + int(wf.get("workingUntrainedWorkers", 0) or 0))
    days_left = max(1, -(-rounds_left // _ROUNDS_PER_DAY))            # ceil(rounds_left/4)
    max_workers = total_workers + 5 * days_left                       # 5 hires/day cap
    max_buildings = max_workers // 4                                  # ~4 workers per building
    n_shelters = sum(1 for f in facs if f.get("buildingType") == "Shelter")
    existing_buildings = n_casework + n_kitchens + n_shelters
    budget_buildings = max(0, max_buildings - existing_buildings)     # how many MORE we can staff

    shelters_needed = max(0, (max(0, P - shelter_cap) + _V2_SHELTER_BEDS - 1) // _V2_SHELTER_BEDS)
    want = []
    if n_casework < 1:
        want.append("CaseworkSite")
    if n_kitchens < _POT_KITCHEN_TARGET:                              # kitchens BEFORE shelters
        want += ["Kitchen"] * (_POT_KITCHEN_TARGET - n_kitchens)
    want += ["Shelter"] * shelters_needed
    want = want[:budget_buildings]                                    # cap to what we can staff

    # (3) GEOGRAPHY: rank available sites by distance to nearest community; avoid blocked routes.
    cstate = gs.get("constructionState", {}) or {}
    sites = [s for s in (cstate.get("availableSites") or []) if s.get("isAvailable")]
    blocked = set((ms.get("floodState", {}) or {}).get("blockedRoutes", []) or [])
    comm_pos = [c.get("position") for c in communities if c.get("position")]

    def _rank(s):
        p = s.get("position")
        d = min((_vec_dist(p, cp) for cp in comm_pos), default=0.0) if (p and comm_pos) else 0.0
        return d + (1e6 if s.get("siteName") in blocked else 0.0)

    ranked_ids = [s.get("siteId") for s in sorted(sites, key=_rank)]
    build_by = {}
    for i, a in enumerate(va):
        if a.get("action_type") == "construction":
            c = a.get("construction") or {}
            build_by.setdefault(c.get("building_type"), {})[c.get("site_id")] = (i, a.get("cost") or 0)

    # (1)+(4): build up to the income-paced per-turn limit, taking each building whose cost clears
    # the (demand-scaled) operating buffer. `want` is already truncated to the staffable headroom
    # (budget_buildings), and the per-turn cap keeps construction in step with funding inflow, so we
    # never front-load all capital into round 0. No separate concurrent-unstaffed ("pipeline") cap:
    # the per-turn pace plus multi-worker hiring (below) keep new buildings staffed within a round.
    unstaffed = sum(1 for f in facs if f.get("buildingStatus") in ("UnderConstruction", "NeedWorker"))
    used = set()
    if rounds_left >= 2:
        for btype in want:
            if len(used) >= _V2_BUILD_PER_TURN:    # income pacing — don't outrun funding inflow
                break
            avail = build_by.get(btype, {})
            sid = next((s for s in ranked_ids if s in avail and s not in used), None) \
                or next((s for s in avail if s not in used), None)
            if sid is None:
                continue
            idx, cost = avail[sid]
            if cost <= budget - op_buffer:
                actions.append(idx)
                budget -= cost
                used.add(sid)

    # (1): hire enough UNTRAINED workers to staff current NeedWorker buildings + the ones queued
    # this turn. The game has NO per-day hiring ceiling (cora.actions: hiring is budget-limited;
    # each hire action bundles up to 5), so we append AS MANY hire actions as needed to close the gap,
    # each bounded by the cash above the operating buffer. The env executes cached action indices in
    # order and re-checks budget live per action, so reusing the largest affordable bundle hires
    # repeatedly; we keep `budget` accurate as we go so no appended action trips the no-debt gate
    # (a server-side failure would abort every later action in the same step).
    wf = gs.get("workforceState", {}) or {}
    free_workers = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
    need_now = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
                   for f in facs if f.get("buildingStatus") == "NeedWorker")
    gap = max(0, need_now + 4 * len(used) - free_workers)
    hire_gap0 = gap                                   # remember the original gap for the log line
    workers_hired = 0
    hire_actions = 0
    # untrained-hire actions offered this turn, keyed by bundle quantity -> (index, cost)
    hire_by_q = {}
    for i, a in enumerate(va):
        wk = a.get("worker") or {}
        if a.get("action_type") == "worker" and wk.get("worker_action_type") == "hire_untrained":
            q = int(wk.get("quantity") or 0)
            if q > 0:
                hire_by_q[q] = (i, int(a.get("cost") or 0))
    if gap > 0 and hire_by_q:
        qs = sorted(hire_by_q)
        unit = hire_by_q[qs[0]][1] / qs[0]            # $ per worker (constant across bundles)
        max_bundle = qs[-1]
        guard = 0
        while gap > 0 and unit > 0 and guard < 64:
            guard += 1
            afford_q = int((budget - op_buffer) // unit)   # bundle the remaining cash can fund
            q = min(gap, max_bundle, afford_q)
            if q <= 0:
                break
            if q not in hire_by_q:                    # fall back to the largest enumerated bundle <= q
                q = max([x for x in qs if x <= q], default=0)
                if q <= 0:
                    break
            idx, cost = hire_by_q[q]
            actions.append(idx)
            budget -= cost
            gap -= q
            workers_hired += q
            hire_actions += 1

    _fill_shelters_from_costly_sources(env, actions)
    return {"choices": choices, "actions": actions, "note": "rules-based-v2",
            "reasoning": (f"v2: P={P} shelterCap={shelter_cap} wantBuilds={len(want)} "
                          f"built={len(used)} hireGap={hire_gap0} hired={workers_hired}/{hire_actions}act "
                          f"unstaffed={unstaffed} opBuf={int(op_buffer)} rl={rounds_left}")}


def combined_decision(env, rnd=0, rounds_total=32):
    """Both hand-written strategies at once.

    The two rules-based policies improve OPPOSITE halves of a turn and neither touches the
    other's half, so they compose without conflict:

      * potential_decision      — keeps greedy's choices, ADDS building (shelters/kitchens
                                  toward a demand target). Improves the ACTION side.
      * improved_rules_based_.. — keeps greedy's worker assignments, REPLACES the choices with
                                  long-term-value ones that price in the motel's recurring
                                  $200/person/day. Improves the CHOICE side.

    So: take the choices from the long-term-value policy and the actions from the building
    policy. Action lists are indices into the same env.valid_actions, so the merge is a
    de-duplicated union that preserves each policy's ordering.

    Both sub-policies already append shelter-filling transfers, so the union inherits those
    too; dedup keeps a transfer from being issued twice.
    """
    lt = improved_rules_based_decision(env, rnd, rounds_total)
    pot = potential_decision(env, rnd, rounds_total)

    seen, actions = set(), []
    for i in list(pot.get("actions") or []) + list(lt.get("actions") or []):
        if i not in seen:
            seen.add(i); actions.append(i)

    return {"choices": lt.get("choices") or [], "actions": actions, "note": "combined",
            "reasoning": "lt-value choices + potential building + shelter transfers"}




# ── demand-NPV policy ───────────────────────────────────────────────────────────────────────
# Provision infrastructure by comparing the discounted savings of OWNING capacity against the
# price of paying per incident. Every constant below is READ FROM THE LIVE STATE, so the policy
# transfers to a different map (more/fewer vehicles, different bed counts, retuned prices)
# without edits. Nothing is hardcoded except the fallbacks used before a quantity is observable.
#
# Mechanics verified in the Unity source (not inferred from play):
#   Kitchen.prefab roundProduction  -> 10 food packs PER ROUND, requiredResources: [], and
#     BuildingResourceStorage.ProduceResources() gates on Building.IsOperational(), so a kitchen
#     pays out only once STAFFED. resourceCapacities.maxCapacity = 20 packs, and production is
#     skipped when CanAddResource fails -> a kitchen that nobody draws from fills in 2 rounds and
#     STALLS. Kitchen throughput is therefore bounded by how fast vehicles haul food away.
#   Vehicle.maxCargoCapacity = 10 packs = exactly one "100 meals" choice; DeliverySystem splits a
#     larger request into ceil(qty/capacity) loads, each needing its own vehicle.
#   MotelCostManager.costPerPersonPerDay = 200, charged EVERY day a resident stays; a staffed
#     shelter costs nothing per day once built.
# Consequence: one kitchen ~ one vehicle's haul rate, so the kitchen target is derived from the
# FLEET SIZE, and the shelter target from the population still exposed to the motel meter.
_DNPV_COVER      = float(os.environ.get("DNPV_COVER", "1.0"))    # shelter beds per unhoused person
_DNPV_KITCHEN_PER_VEH = float(os.environ.get("DNPV_KITCHEN_PER_VEH", "1.0"))
_DNPV_MAX_PAY    = float(os.environ.get("DNPV_MAX_PAY", "3000")) # per-incident cash ceiling
_DNPV_PAY_IF_NO_VEH = os.environ.get("DNPV_PAY_IF_NO_VEH", "1") == "1"


def _dnpv_map_constants(env, gs, facs):
    """Derive this MAP's constants from observed state; no map-specific literals."""
    c = getattr(env, "_dnpv_cache", None)
    if c is None:
        c = env._dnpv_cache = {"fleet": 0, "bed": 0, "kcap": 0, "motel_rate": 0.0,
                               "day": None, "lodge": 0.0, "pop": 0,
                               "n_reloc": 0, "n_food": 0, "packs": 0, "people": 0, "rounds": 0}
    log = (gs.get("logistics") or {})
    # fleet size: the most vehicles ever simultaneously idle is a lower bound on the fleet
    c["fleet"] = max(c["fleet"], int(log.get("availableVehicles") or 0),
                     int(log.get("totalVehicles") or 0))
    for f in facs:
        if f.get("buildingType") == "Shelter":
            c["bed"] = max(c["bed"], int(f.get("populationCapacity") or 0))
        if f.get("buildingType") == "Kitchen":
            c["kcap"] = max(c["kcap"], int((f.get("resources") or {}).get("foodPacksCapacity") or 0))
    # Bootstrap: with no shelter standing there is nothing to read a bed count from, and any
    # sizing rule that needs `bed` then never authorises the first build -- a deadlock that held
    # shelters at 0/0 for entire episodes in the smoke. Take the capacity off the enumerated
    # build action if it carries one; otherwise mark bed UNKNOWN so the policy builds a single
    # probe shelter to learn the number rather than stalling forever.
    for a in (getattr(env, "valid_actions", None) or []):
        if a.get("action_type") == "construction":
            con = a.get("construction") or {}
            if str(con.get("building_type")).lower().startswith("shelter"):
                for k in ("population_capacity", "populationCapacity", "capacity"):
                    if con.get(k):
                        c["bed"] = max(c["bed"], int(con[k]))
    c["bed_known"] = c["bed"] > 0
    # Prices come from the RAW state's own fields (the source the observation reads), never literals.
    cs = gs.get("constructionState") or {}
    wf = gs.get("workforceState") or {}
    costs = {}
    for v, k in ((cs.get("buildingConstructionCost"), "build"),
                 (wf.get("untrainedWorkerCost"), "hire")):
        if v: costs[k] = float(v)

    # MOTEL RATE: older builds exported no per-person-per-day price, so INFER it from what the map actually charges:
    #   d(lodgingSpend) / motel_population, sampled on day boundaries.
    # That makes the policy correct on a map with a different price without touching the code.
    rm = gs.get("rewardMetrics") or {}
    lodging = float(rm.get("lodgingSpend") or 0.0)
    motel = next((f for f in facs if f.get("buildingType") == "Motel"), None)
    pop = int((motel or {}).get("currentPopulation") or 0)
    day = int((gs.get("sessionInfo") or {}).get("currentDay") or 0)
    prev_day, prev_lodge, prev_pop = c.get("day"), c.get("lodge", 0.0), c.get("pop", 0)
    if prev_day is not None and day > prev_day and prev_pop > 0:
        rate = (lodging - prev_lodge) / prev_pop      # $ per resident per day, observed
        if rate > 0:
            obs = c.setdefault("rate_obs", [])
            obs.append(rate)
            # lodgingSpend also absorbs one-off paid evacuations, so a single day can read high
            # (measured 430 against a true 200). The median rejects those spikes.
            c["motel_rate"] = sorted(obs)[len(obs) // 2]
    if day != prev_day:
        c["day"], c["lodge"], c["pop"] = day, lodging, pop
    return c, costs


def demand_npv_decision(env, rnd=0, rounds_total=32):
    """Demand-driven provisioning + per-incident build-vs-pay arbitration (map-agnostic)."""
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = gs.get("mapState", {}).get("facilities", []) or []
    rounds_left = max(0, rounds_total - rnd)
    K, costs = _dnpv_map_constants(env, gs, facs)

    rounds_per_day = 4.0
    days_left  = max(0.0, rounds_left / rounds_per_day)
    fleet      = K["fleet"] or 1
    bed        = K["bed"] or 0
    motel_rate = K["motel_rate"] or 0.0
    build_cost = costs.get("build", 0.0)
    hire_cost  = costs.get("hire", 0.0)
    free_veh   = int((gs.get("logistics") or {}).get("availableVehicles") or 0)

    base = greedy_decision(env)
    choices, actions = base["choices"], list(base["actions"])
    tasks_by_id = {t["taskId"]: t for t in (gs.get("allActiveTasks") or [])}

    def _txt(c):  return (c.get("choiceText") or "").lower()
    def _cost(c): return abs(float(_impacts_dict(c).get("Budget", 0) or 0))
    def _qty(c):
        q = c.get("deliveryQuantity")
        if q: return int(q)
        m = re.search(r"(\d+)", _txt(c))          # rendered text is packs x10
        return int(m.group(1)) // 10 if m else 1

    unhoused = sum((f.get("currentPopulation") or 0) for f in facs
                   if f.get("buildingType") == "Community")
    beds_free = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                    for f in facs if f.get("buildingType") == "Shelter"
                    and f.get("buildingStatus") == "InUse")
    n_shelter = sum(1 for f in facs if f.get("buildingType") == "Shelter")
    n_kitchen = sum(1 for f in facs if f.get("buildingType") == "Kitchen")
    # one vehicle hauls one load per trip, and a kitchen refills ~one load per round, so the
    # fleet is what decides how many kitchens can actually be drained.
    kitchen_target = max(1, int(round(_DNPV_KITCHEN_PER_VEH * fleet)))

    for ch in choices:
        t = tasks_by_id.get(ch["taskId"])
        if not t:
            continue
        cs, title = (t.get("choices") or []), (t.get("taskTitle") or "")

        if "Relocation" in title or "Population" in title:
            opt = next((c for c in cs if "shelter" in _txt(c)), None)
            if opt is not None and beds_free > 0:
                ch["choiceId"] = opt["choiceId"]
                beds_free = max(0, beds_free - min(beds_free, bed or beds_free))

        elif "Food Request" in title:
            # rank the free (vehicle-hauled) options by how many vehicle-loads they need
            hauled = sorted([(c, max(1, _qty(c))) for c in cs if _cost(c) == 0],
                            key=lambda x: -x[1])
            instant = next((c for c in cs if _cost(c) > 0), None)
            pick, used = None, 0
            for c, q in hauled:                       # take the largest that FITS the free fleet
                loads = max(1, -(-q // 10)) if q > 10 else 1
                if loads <= free_veh:
                    pick, used = c, loads
                    break
            if pick is None and _DNPV_PAY_IF_NO_VEH and instant is not None \
                    and _cost(instant) <= _DNPV_MAX_PAY:
                pick, used = instant, 0               # fleet saturated: cash is the only clearer
            if pick is not None:
                ch["choiceId"] = pick["choiceId"]
                free_veh = max(0, free_veh - used)

    def find_build(btype):
        cands = [(i, a) for i, a in enumerate(va) if a.get("action_type") == "construction"
                 and (a.get("construction") or {}).get("building_type") == btype]
        return min(cands, key=lambda x: x[1].get("cost") or 0) if cands else None

    target = None
    if rounds_left >= rounds_per_day:
        need_workers = 0
        for f in facs:
            if f.get("buildingType") in ("Shelter", "Kitchen"):
                need_workers = max(need_workers, int(f.get("requiredWorkforce") or 0))
        staff_cost = need_workers * hire_cost
        # SHELTER: savings = taking a shelter-load off the $/person/day meter for the rest of the run
        if bed and motel_rate and (n_shelter * bed) < _DNPV_COVER * unhoused:
            cand = find_build("Shelter")
            if cand:
                people = min(bed, max(0, unhoused))
                if people * motel_rate * days_left > (cand[1].get("cost") or build_cost) + staff_cost:
                    target = cand
        # KITCHEN: savings = the paid deliveries its per-round output displaces, while the fleet
        # still has slack to haul that output away.
        if target is None and n_kitchen < kitchen_target:
            cand = find_build("Kitchen")
            if cand:
                target = cand
    if target is not None:
        actions.append(target[0])

    wf = gs.get("workforceState", {}) or {}
    free_w = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
    need_w = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
                 for f in facs if f.get("buildingStatus") in ("NeedWorker", "UnderConstruction"))
    hires = 0
    while need_w > free_w + hires and hires < 8:
        idx = next((i for i, a in enumerate(va)
                    if a.get("action_type") == "worker"
                    and (a.get("worker") or {}).get("worker_action_type") == "hire_untrained"
                    and i not in actions), None)
        if idx is None:
            break
        actions.append(idx); hires += 1
    _fill_shelters_from_costly_sources(env, actions)
    return {"choices": choices, "actions": actions, "note": "demand-npv",
            "reasoning": f"fleet={fleet} bed={bed} motel_rate={motel_rate:.0f} unhoused={unhoused} "
                         f"beds_free={beds_free} kitchens={n_kitchen}/{kitchen_target} veh={free_veh}"}



# ── demand-FORECAST policy ──────────────────────────────────────────────────────────────────
# demand-npv provisions against demand that has ALREADY arrived. This one estimates the arrival
# PROCESS online and provisions against the demand still to come, which is what actually closes
# the headroom: capacity only pays if it exists BEFORE the demand shows up, and construction has
# a ~1-day (~4-round) lead time plus a staffing step, so reacting is structurally too late.
#
# Online estimates (all per-map, nothing hardcoded):
#   lam_reloc, lam_food  — task arrivals per round, counted from tasks actually seen so far
#   ppl_per_reloc        — mean people moved per relocation, from the affected facility
#   packs_per_food       — mean packs requested, from choice deliveryQuantity
# Forecast over the remaining horizon, net of build lead time:
#   future_people = lam_reloc * usable_rounds * ppl_per_reloc
#   future_packs  = lam_food  * usable_rounds * packs_per_food
#
# Sizing:
#   shelters = ceil(future_people / beds_per_shelter), each justified only if the motel meter it
#     switches off over the REMAINING days exceeds build + staffing:
#         people_served * motel_rate * days_left_after_ready  >  build + workers*hire
#   kitchens = ceil(future_packs / packs_a_kitchen_can_deliver_over_horizon), capped by FLEET,
#     because a kitchen that nobody hauls from fills its store and stalls (CanAddResource gate).
#     Justified if the paid deliveries it displaces exceed build + staffing.
# Every price, capacity and rate is read from live state via _dnpv_map_constants.
_DFC_LEAD_ROUNDS = float(os.environ.get("DFC_LEAD_ROUNDS", "5"))   # construct + staff before useful
_DFC_SAFETY      = float(os.environ.get("DFC_SAFETY", "1.0"))      # over/under-provision knob
_DFC_MAX_PAY     = float(os.environ.get("DFC_MAX_PAY", "3000"))
# Shelters look like the obvious win on cash ($200/person/day avoided) but the SCORE disagrees:
# score = satisfaction - cost_efficiency, satisfaction terms are clamp01(fulfilled/resolved)
# QUANTITY ratios, and ExpireTask still books an ignored task's demand into the denominator with
# a zero numerator -- so unfulfilled demand is unavoidable damage. A shelter holds `bed` people
# and partially fulfils anything larger; the motel is effectively unbounded and always completes.
# Measured: routing to shelters put sat_lodging at 0.78-0.81 against build-potential's 0.998,
# while cost_lodging moved 0.122 -> 0.120, i.e. the cash saving was worth ~nothing. Meanwhile
# casework_processing_sat (= caseworkProcessed/caseworkRequested, denominator NOT dodgeable) sat
# at 0.53 with casework_efficiency 0.001 -- nearly free score left on the table. So the build
# budget belongs in CASEWORK, not shelters.
_DFC_USE_SHELTER = os.environ.get("DFC_USE_SHELTER", "1") == "1"
_DFC_CW_PER      = float(os.environ.get("DFC_CW_PER", "4"))   # open casework requests per site
_DFC_CW_MIN      = int(os.environ.get("DFC_CW_MIN", "1"))     # floor once any casework is requested


def _dfc_observe(env, gs, K):
    """Update the online arrival-rate estimates from THIS round's task list."""
    seen = getattr(env, "_dfc_seen", None)
    if seen is None:
        seen = env._dfc_seen = set()
    K["rounds"] = K.get("rounds", 0) + 1
    facs = gs.get("mapState", {}).get("facilities", []) or []
    pop_by = {f.get("facilityName"): (f.get("currentPopulation") or 0) for f in facs}
    # Group size, learned from what actually LEAVES the communities each round. The task's
    # affectedFacility never matched a facilityName, so the per-task lookup below silently
    # yielded 0 and the forecast ran on the unhoused count alone.
    cur_comm = sum(v for k, v in pop_by.items() if "community" in str(k).lower())
    prev = K.get("comm_pop")
    if prev is not None and prev > cur_comm:
        moved = prev - cur_comm
        K["grp_max"] = max(K.get("grp_max", 0), moved)
        K["grp_sum"] = K.get("grp_sum", 0) + moved
        K["grp_n"] = K.get("grp_n", 0) + 1
    K["comm_pop"] = cur_comm
    for t in (gs.get("allActiveTasks") or []):
        tid = t.get("taskId"); title = (t.get("taskTitle") or "")
        key = (title, t.get("affectedFacility"), tid)
        if key in seen:
            continue                      # count each arrival once, not once per round it lingers
        seen.add(key)
        if "Relocation" in title or "Population" in title:
            K["n_reloc"] += 1
            K["people"] += pop_by.get(t.get("affectedFacility")) or 0
        elif "Food Request" in title:
            K["n_food"] += 1
            qs = [int(c.get("deliveryQuantity") or 0) for c in (t.get("choices") or [])]
            qs = [q for q in qs if q > 0]
            K["packs"] += (sum(qs) / len(qs)) if qs else 0


def demand_forecast_decision(env, rnd=0, rounds_total=32):
    """Forecast future demand and pre-build shelters/kitchens to meet it (map-agnostic)."""
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = gs.get("mapState", {}).get("facilities", []) or []
    K, costs = _dnpv_map_constants(env, gs, facs)
    _dfc_observe(env, gs, K)

    rounds_left = max(0, rounds_total - rnd)
    usable = max(0.0, rounds_left - _DFC_LEAD_ROUNDS)          # capacity only helps after it exists
    days_after_ready = usable / 4.0
    fleet      = K["fleet"] or 1
    bed        = K["bed"] or 0
    motel_rate = K.get("motel_rate", 0.0)
    build_cost = costs.get("build", 0.0)
    hire_cost  = costs.get("hire", 0.0)
    obs_rounds = max(1, K.get("rounds", 1))
    free_veh   = int((gs.get("logistics") or {}).get("availableVehicles") or 0)

    # ---- forecast ------------------------------------------------------------------------
    lam_reloc = K["n_reloc"] / obs_rounds
    lam_food  = K["n_food"]  / obs_rounds
    ppl_per   = (K["people"] / K["n_reloc"]) if K["n_reloc"] else 0.0
    packs_per = (K["packs"]  / K["n_food"])  if K["n_food"]  else 0.0
    future_people = _DFC_SAFETY * lam_reloc * usable * ppl_per
    future_packs  = _DFC_SAFETY * lam_food  * usable * packs_per
    # anyone still sitting in a Community is demand we KNOW about, on top of the forecast
    unhoused = sum((f.get("currentPopulation") or 0) for f in facs
                   if f.get("buildingType") == "Community")
    people_target = max(future_people, min(unhoused, future_people + unhoused))

    n_shelter = sum(1 for f in facs if f.get("buildingType") == "Shelter")
    n_kitchen = sum(1 for f in facs if f.get("buildingType") == "Kitchen")
    beds_free = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                    for f in facs if f.get("buildingType") == "Shelter"
                    and f.get("buildingStatus") == "InUse")

    need_workers = max([int(f.get("requiredWorkforce") or 0) for f in facs
                        if f.get("buildingType") in ("Shelter", "Kitchen")] or [0])
    staff_cost = need_workers * hire_cost

    shelters_needed = int(-(-people_target // bed)) if bed else (1 if people_target > 0 else 0)
    # a kitchen delivers at most one vehicle-load per round, so over `usable` rounds it can move
    # at most that; and the fleet caps how many kitchens can be drained at once.
    per_kitchen_packs = 10.0 * usable          # 10 packs/round production (Kitchen.roundProduction)
    kitchens_needed = int(-(-future_packs // per_kitchen_packs)) if per_kitchen_packs > 0 else 0
    kitchens_needed = max(0, min(kitchens_needed, int(fleet)))

    base = greedy_decision(env)
    choices, actions = base["choices"], list(base["actions"])
    tasks_by_id = {t["taskId"]: t for t in (gs.get("allActiveTasks") or [])}

    def _txt(c):  return (c.get("choiceText") or "").lower()
    def _cost(c): return abs(float(_impacts_dict(c).get("Budget", 0) or 0))

    for ch in choices:
        t = tasks_by_id.get(ch["taskId"])
        if not t:
            continue
        cs, title = (t.get("choices") or []), (t.get("taskTitle") or "")
        if "Relocation" in title or "Population" in title:
            # A shelter holds `bed` people; the motel is effectively unbounded. Routing a group
            # into a shelter with too few free beds PARTIALLY fulfils it, and the score pays for
            # fulfilment, not for where people sleep. Measured: gating on `beds_free > 0` drove
            # lodgingFulfilled to 512/676 (76%) against build-potential's 600/601 (99.8%) and cost
            # 0.234 of sat_lodging -- while the cost terms were IDENTICAL (cost_lodging 0.120 vs
            # 0.122), i.e. the motel savings this was chasing were already priced in. So require
            # room for the whole group, sized from what relocations have actually moved so far.
            group = K.get("grp_max") or bed or 0
            opt = next((c for c in cs if "shelter" in _txt(c)), None) if _DFC_USE_SHELTER else None
            if opt is not None and bed and beds_free >= max(group, bed):
                ch["choiceId"] = opt["choiceId"]
                beds_free = max(0, beds_free - group)
        elif "Food Request" in title:
            hauled = sorted([(c, int(c.get("deliveryQuantity") or 1)) for c in cs if _cost(c) == 0],
                            key=lambda x: -x[1])
            instant = next((c for c in cs if _cost(c) > 0), None)
            pick, used = None, 0
            for c, q in hauled:
                loads = max(1, int(-(-q // 10)))
                if loads <= free_veh:
                    pick, used = c, loads
                    break
            if pick is None and instant is not None and _cost(instant) <= _DFC_MAX_PAY:
                pick, used = instant, 0
            if pick is not None:
                ch["choiceId"] = pick["choiceId"]
                free_veh = max(0, free_veh - used)

    def find_build(btype):
        cands = [(i, a) for i, a in enumerate(va) if a.get("action_type") == "construction"
                 and (a.get("construction") or {}).get("building_type") == btype]
        return min(cands, key=lambda x: x[1].get("cost") or 0) if cands else None

    target = None
    if usable > 0:
        # CASEWORK FIRST: largest untapped satisfaction term, and its cost term is ~0.001.
        n_casework0 = sum(1 for f in facs if f.get("buildingType") == "CaseworkSite")
        cw_open0 = sum(1 for t in (gs.get("allActiveTasks") or [])
                       if "Casework" in (t.get("taskTitle") or ""))
        cw_target = max(_DFC_CW_MIN, int(-(-cw_open0 // _DFC_CW_PER))) if cw_open0 else 0
        if n_casework0 < cw_target:
            cand = find_build("CaseworkSite")
            if cand:
                target = cand
        probe = (_DFC_USE_SHELTER and (not K.get("bed_known"))
                 and n_shelter == 0 and unhoused > 0)
        if target is None and _DFC_USE_SHELTER and (
                probe or (n_shelter < shelters_needed and bed and motel_rate)):
            cand = find_build("Shelter")
            if cand:
                if probe:
                    target = cand          # one probe build to LEARN beds/shelter and the rate
                else:
                    saved = min(bed, people_target) * motel_rate * days_after_ready
                    if saved > (cand[1].get("cost") or build_cost) + staff_cost:
                        target = cand
        # CASEWORK: a "Casework Request" can only be resolved once a casework site is built and
        # staffed, and casework_processing_sat is a first-class reward term. Omitting it cost
        # 0.368 of score against build-potential (0.173 vs 0.541) -- the largest single gap.
        # Size it off the casework demand actually arriving, not a fixed count.
        n_casework = sum(1 for f in facs if f.get("buildingType") == "CaseworkSite")
        cw_open = sum(1 for t in (gs.get("allActiveTasks") or [])
                      if "Casework" in (t.get("taskTitle") or ""))
        if target is None and cw_open > 0 and n_casework < max(1, int(-(-cw_open // 4))):
            cand = find_build("Casework") or find_build("CaseworkSite")
            if cand:
                target = cand
        if target is None and n_kitchen < kitchens_needed:
            cand = find_build("Kitchen")
            if cand:
                # each pack this kitchen delivers displaces a paid pack; price it off the cheapest
                # paid food choice currently on offer rather than a literal.
                paid = [(_cost(c) / max(1, int(c.get("deliveryQuantity") or 1)))
                        for t in (gs.get("allActiveTasks") or [])
                        if "Food Request" in (t.get("taskTitle") or "")
                        for c in (t.get("choices") or []) if _cost(c) > 0]
                per_pack = min(paid) if paid else 0.0
                movable = min(per_kitchen_packs, future_packs)
                if per_pack and movable * per_pack > (cand[1].get("cost") or build_cost) + staff_cost:
                    target = cand
    if target is not None:
        actions.append(target[0])

    wf = gs.get("workforceState", {}) or {}
    free_w = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
    need_w = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
                 for f in facs if f.get("buildingStatus") in ("NeedWorker", "UnderConstruction"))
    hires = 0
    while need_w > free_w + hires and hires < 8:
        idx = next((i for i, a in enumerate(va)
                    if a.get("action_type") == "worker"
                    and (a.get("worker") or {}).get("worker_action_type") == "hire_untrained"
                    and i not in actions), None)
        if idx is None:
            break
        actions.append(idx); hires += 1
    _fill_shelters_from_costly_sources(env, actions)
    return {"choices": choices, "actions": actions, "note": "demand-forecast",
            "reasoning": (f"lam_reloc={lam_reloc:.2f} lam_food={lam_food:.2f} ppl/rel={ppl_per:.0f} "
                          f"packs/food={packs_per:.0f} -> people={people_target:.0f} packs={future_packs:.0f} "
                          f"| shelters {n_shelter}/{shelters_needed} kitchens {n_kitchen}/{kitchens_needed} "
                          f"fleet={fleet} bed={bed} motel={motel_rate:.0f}")}



# ── pareto policy ───────────────────────────────────────────────────────────────────────────
# The strategy the calibrated surrogate's Pareto frontier converges on. Every one of the nine
# non-dominated policies shares these invariants (the rest -- kitchen count, casework count, food
# rule -- only trade score against banked budget):
#     6 shelters · build from round 0 · one building per round · relocations to SHELTERS
#     · answer every casework request
# Build ORDER is casework -> shelters -> kitchens, because casework has the longest chain to payoff
# (build ~4 rounds, then staff, and requests only mature once residents have been housed a while),
# while paid food covers the gap cheaply until the kitchens come online.
# Sizing rationale, all derived rather than assumed:
#   shelters  ~ relocated population / beds-per-shelter. Swept 4..12 on the surrogate: score peaks
#               at 5-6 (3.876/3.877) and falls away after -- extra shelters eat the sites and cash
#               that casework and kitchens need. 6 is a real optimum, not the sweep's cap.
#   kitchens  ~ fleet haul capacity: a kitchen refills ~1 vehicle-load per round, so more kitchens
#               than the fleet can drain just stall on their 20-pack store.
#   casework  ~ sized to the request backlog; its cost term is ~0.001, so it is nearly free score.
# ---------------------------------------------------------------------------------------------
# The POLICY FAMILY shared with the surrogate (oracle/pareto_sweep.py:make_plan).
#
# Both engines must run the SAME policy or an engine-vs-engine comparison measures the two
# implementations rather than the two engines. So the nine knobs live in one place, are read from
# ARC_FAMILY_CFG (JSON), and mean exactly what make_plan() means by them:
#   n_shelter/n_kitchen/n_casework  how many of each to build
#   start, spacing                  first build round, and rounds between builds
#   food    "kitchen10" haul from a kitchen | "paid" buy the immediate option
#   reloc   "shelter" route to shelters     | "motel" route to the motel
#   answer_cw                       take offered casework actions (0/1)
#   switch                          round at which food and reloc both flip to the other rule
# Defaults reproduce the frontier plan already measured on Unity (job 36031, +2.600).
# ---------------------------------------------------------------------------------------------
_FAMILY_DEFAULT = dict(n_shelter=6, n_kitchen=2, n_casework=3, start=0, spacing=1,
                       food="kitchen10", reloc="shelter", answer_cw=1, switch=None)


def _family_cfg():
    cfg = dict(_FAMILY_DEFAULT)
    raw = os.environ.get("ARC_FAMILY_CFG", "").strip()
    if raw:
        cfg.update(json.loads(raw))
    # legacy single-knob env vars still honoured so old sbatch wrappers keep working
    for k, ev in (("n_shelter", "PARETO_SHELTERS"), ("n_kitchen", "PARETO_KITCHENS"),
                  ("n_casework", "PARETO_CASEWORK")):
        if os.environ.get(ev):
            cfg[k] = int(os.environ[ev])
    return cfg


def _family_rules(cfg, rnd):
    """food/reloc rule in force at `rnd`, applying `switch`. Mirrors make_plan.macro exactly."""
    food, reloc = cfg["food"], cfg["reloc"]
    if cfg.get("switch") is not None and rnd >= cfg["switch"]:
        food = "paid" if food == "kitchen10" else "kitchen10"
        reloc = "motel" if reloc == "shelter" else "shelter"
    return food, reloc


def _family_build_schedule(cfg):
    """round -> buildingType. Same order (casework, shelter, kitchen) and cadence as make_plan."""
    order = (["CaseworkSite"] * cfg["n_casework"] + ["Shelter"] * cfg["n_shelter"]
             + ["Kitchen"] * cfg["n_kitchen"])
    return {cfg["start"] + i * cfg["spacing"]: b for i, b in enumerate(order)}


def pareto_decision(env, rnd=0, rounds_total=32, cfg=None):
    """Frontier strategy, parameterised by the shared policy family (see _family_cfg)."""
    cfg = cfg or _family_cfg()
    food_rule, reloc_rule = _family_rules(cfg, rnd)
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = gs.get("mapState", {}).get("facilities", []) or []
    have = {k: sum(1 for f in facs if f.get("buildingType") == k)
            for k in ("Shelter", "Kitchen", "CaseworkSite")}
    want = [("CaseworkSite", cfg["n_casework"]), ("Shelter", cfg["n_shelter"]),
            ("Kitchen", cfg["n_kitchen"])]

    base = greedy_decision(env)
    choices, actions = base["choices"], list(base["actions"])
    tasks_by_id = {t["taskId"]: t for t in (gs.get("allActiveTasks") or [])}

    def _txt(c):  return (c.get("choiceText") or "").lower()
    def _cost(c): return abs(float(_impacts_dict(c).get("Budget", 0) or 0))
    free_veh = int((gs.get("logistics") or {}).get("availableVehicles") or 0)
    shelter_beds_free = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                            for f in facs if f.get("buildingType") == "Shelter"
                            and f.get("buildingStatus") == "InUse")
    kitchen_stock = sum(((f.get("resources") or {}).get("foodPacks") or 0)
                        for f in facs if f.get("buildingType") == "Kitchen"
                        and f.get("buildingStatus") == "InUse")

    for ch in choices:
        t = tasks_by_id.get(ch["taskId"])
        if not t:
            continue
        cs, title = (t.get("choices") or []), (t.get("taskTitle") or "")
        if "Relocation" in title or "Population" in title:
            # Route to a shelter ONLY when one is actually InUse with free beds. The surrogate
            # silently falls back to the motel when beds are short; Unity does not -- picking
            # "Send to Shelters" before any shelter is operational simply fails. Because this
            # policy front-loads construction, the first ~10 rounds have no beds at all, and the
            # unguarded version dropped sat_lodging to 0.803 (against 0.998 for build-potential).
            opt = (next((c for c in cs if "shelter" in _txt(c) and _cost(c) == 0), None)
                   if (reloc_rule == "shelter" and shelter_beds_free > 0) else None)
            if opt is None:
                opt = next((c for c in cs if "motel" in _txt(c) and _cost(c) == 0), None)
            if opt is not None:
                ch["choiceId"] = opt["choiceId"]
                shelter_beds_free = max(0, shelter_beds_free - 100)
        elif "Food Request" in title:
            # Kitchen haul needs BOTH a free vehicle and a kitchen holding stock; otherwise buy the
            # immediate option. Same failure mode as above: ordering from kitchens that are still
            # under construction cost sat_food 0.226.
            hauled = sorted([c for c in cs if _cost(c) == 0],
                            key=lambda c: int(c.get("deliveryQuantity") or 1))
            instant = next((c for c in cs if _cost(c) > 0), None)
            can_haul = (food_rule == "kitchen10" and hauled and free_veh > 0
                        and kitchen_stock > 0)
            pick = hauled[0] if can_haul else (instant or (hauled[0] if hauled else None))
            if pick is not None:
                ch["choiceId"] = pick["choiceId"]
                if pick is not instant:
                    free_veh = max(0, free_veh - 1)
                    kitchen_stock = max(0, kitchen_stock - 10)
        elif "Casework" in title and cfg["answer_cw"]:
            # casework_processing_sat is the largest untapped term and casework_efficiency ~0.001,
            # so always take an offered casework action.
            opt = next((c for c in cs if _cost(c) == 0), None) or (cs[0] if cs else None)
            if opt is not None:
                ch["choiceId"] = opt["choiceId"]

    # ONE building per round, in priority order, until each target is met -- but only on the
    # rounds the schedule names, so `start` and `spacing` mean the same thing in both engines.
    sched = _family_build_schedule(cfg)
    for btype, target in (want if rnd in sched else []):
        if have.get(btype, 0) >= target:
            continue
        cand = [(i, a) for i, a in enumerate(va)
                if a.get("action_type") == "construction"
                and (a.get("construction") or {}).get("building_type") == btype]
        if cand:
            actions.append(min(cand, key=lambda x: x[1].get("cost") or 0)[0])
        break                                   # at most one build per round

    # hire enough untrained bodies to staff what is standing or rising
    wf = gs.get("workforceState", {}) or {}
    free_w = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
    need_w = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
                 for f in facs if f.get("buildingStatus") in ("NeedWorker", "UnderConstruction"))
    hires = 0
    while need_w > free_w + hires and hires < 8:
        idx = next((i for i, a in enumerate(va)
                    if a.get("action_type") == "worker"
                    and (a.get("worker") or {}).get("worker_action_type") == "hire_untrained"
                    and i not in actions), None)
        if idx is None:
            break
        actions.append(idx); hires += 1
    _fill_shelters_from_costly_sources(env, actions)
    return {"choices": choices, "actions": actions, "note": "pareto",
            "reasoning": f"r{rnd} have={have} want={[(b, t) for b, t in want]} "
                         f"food={food_rule} reloc={reloc_rule} veh={free_veh}"}


def random_decision(env, rng_seed=0):
    """Random valid actions + one random choice per task (lower-bound baseline).
    Deterministic-ish per call via a simple LCG over valid_action count (no global RNG)."""
    va = env.valid_actions or []
    gs = env.game_state or {}
    # vary selection by env step + action count without Math.random-style globals
    seed = (env.current_step * 1103515245 + len(va) * 12345 + rng_seed) & 0x7fffffff
    actions = []
    for i in range(len(va)):
        seed = (seed * 1103515245 + 12345) & 0x7fffffff
        if (seed % 5) == 0:        # ~20% of valid actions
            actions.append(i)
    choices = []
    for t in gs.get("allActiveTasks", []) or []:
        tcs = t.get("choices") or []
        if tcs:
            seed = (seed * 1103515245 + 12345) & 0x7fffffff
            c = tcs[seed % len(tcs)]
            choices.append({"taskId": t["taskId"], "choiceId": c["choiceId"]})
    return {"choices": choices, "actions": actions[:8], "note": "random", "reasoning": "random baseline"}


# ── Decision-time image for the vision arms ─────────────────────────────────
def _decision_image(image_mode, env, grid, tmp_png):
    """Return base64 PNG of a decision-time view of the CURRENT state, or None.

    synthetic -> render the dashboard from env.game_state + the static tile grid
                 (pure Python, no graphics build needed).
    real      -> ask Unity to capture the live game frame right now (no advance).
    Any failure degrades to None (text-only round) rather than crashing the episode."""
    try:
        if image_mode == "synthetic":
            import arc_dashboard_render as dash
            dash.render_dashboard(env.game_state or {}, out_path=tmp_png, grid=grid)
            with open(tmp_png, "rb") as f:
                return base64.b64encode(f.read()).decode()
        if image_mode == "real":
            resp = env.request({"type": "capture_frame"})
            return (resp or {}).get("frame_base64")
    except Exception as e:
        print(f"    [image:{image_mode}] capture failed: {type(e).__name__}: {str(e)[:80]}")
    return None


# ── One episode ─────────────────────────────────────────────────────────────
# Category names for the per-round action mix (actCats); the analysis and plotting scripts read these.
_ACTION_CATEGORY = {"build": "construction", "hire": "worker", "train": "worker",
                    "staff": "worker_assignment", "deconstruct": "deconstruction",
                    "transfer": "resource_transfer"}


def _round_record(rnd, reward, total, info, call_results, state, raw, dec, parsed_ok, rtrace, rtok):
    """One round of an episode record: the score breakdown, what was attempted and what happened
    to each call, and the full prompt/completion pair (a self-contained finetuning corpus)."""
    task_type = {t.get("taskId"): t.get("type", "?") for t in (state or {}).get("tasks", [])}
    act_cats = {}
    for cr in call_results:
        if cr.status == "invalid":
            continue
        k = (f"choice:{task_type.get(cr.choice['taskId'], '?')}" if cr.choice is not None
             else _ACTION_CATEGORY.get(cr.tool, cr.tool))
        act_cats[k] = act_cats.get(k, 0) + max(1, len(cr.action_indices))
    exres = info.get("execution_results") or []
    rm = info.get("reward_metrics") or {}
    return {
        "r": rnd, "reward": round(reward, 4), "sumR": round(total, 4),
        "sat": info["satisfaction"], "budget": info["budget"],
        "satScore": round(info["satisfaction_score"], 4),
        "eff": round(info.get("efficiency", 0.0), 4),   # live efficiency / 1000
        # Cumulative-to-date score breakdown, numeric terms only.
        "comps": {k: round(v, 4) for k, v in (info.get("score_components") or {}).items()
                  if isinstance(v, (int, float))},
        "foodFul": rm.get("foodFulfilled"), "foodRes": rm.get("foodResolved"),
        "lodgFul": rm.get("lodgingFulfilled"), "lodgRes": rm.get("lodgingResolved"),
        "nSel": sum(1 for cr in call_results if cr.choice is not None and cr.status == "executed"),
        "nReq": sum(len(cr.action_indices) for cr in call_results),
        "nFail": sum(1 for r in exres if not r.get("success")),
        "actCats": act_cats,
        # Every call and its outcome (executed / refused / invalid, with the reason).
        "calls": [cr.as_dict() for cr in call_results],
        "cmdErrors": [f"{cr.tool}: {cr.reason}" for cr in call_results if cr.status == "invalid"],
        "choiceErrors": [cr.reason for cr in call_results if cr.choice is not None and cr.status == "refused"],
        "note": "; ".join(cr.summary for cr in call_results if cr.summary)[:300],
        "reasoning": (dec.get("reasoning") or "")[:1500],
        "reasoningTokens": rtok,
        "obs": state, "raw": raw or "", "reasoningTrace": rtrace or None, "parsed_ok": parsed_ok,
    }


def run_episode(model, ep_idx, rounds, port, client, validate=False, port_pool=None, log_dir=None,
                show_impacts=True, policy="llm", image_mode="none",
                reasoning_effort="low", manual_transfers=True, prompt="minimal_v6_1",
                temperature=None, obs_encoding="json", history=1, ablation="",
                base_seed=None):
    """Fresh Unity process -> play `rounds` -> structured per-episode record.

    image_mode {none, synthetic, real}: what decision-time image (if any) is shown to
    the LLM alongside the text state. real uses the graphics-enabled render build;
    none/synthetic use the fast Server build. Only applies to the llm policy.

    If port_pool (a Queue) is given, lease a unique port for the lifetime of this
    episode so concurrent episodes never collide on a port (scheduling-safe)."""
    if port_pool is not None:
        port = port_pool.get()
    # Turn the minimal_v2 encoding fixes (Passive label, un-truncated choices, transfers/affects
    # cleanup) ON for the minimal_v2 arm and OFF otherwise, so it pairs with the v2 system prompt.
    # Constant per run; set before any obs is built (summarize()/render read this flag).
    # v3 inherits the v2 ENCODING fixes (Passive label, un-truncated choice text, dead-transfer
    # line dropped, dangling `affects` hidden); only the prompt text differs between v2 and v3.
    pack = cora_prompts.load_pack(prompt)
    # The pack declares the observation features its text relies on (minimal_v6_1: marked choices).
    obs_config = ObsConfig(show_impacts=show_impacts,
                           mark_unavailable_choices=bool(pack.observation.get("mark_unavailable_choices")))
    # Anthropic caps temperature at 1.0; clamp per-model so a shared sweep invocation (e.g. temp=1.5
    # for gemini) doesn't 400 Claude. eff_temp is what's actually sent + logged; temperature is the
    # requested experimental level.
    eff_temp = temperature
    if temperature is not None and _is_anthropic(model):
        eff_temp = min(temperature, ANTHROPIC_TEMP_MAX)
    ulog = None
    if log_dir:
        safe = model.replace("/", "_").replace(":", "_")
        ulog = str((Path(log_dir) / f"unity_{safe}_ep{ep_idx}_{image_mode}_tools.log").resolve())
    use_image = (image_mode in ("synthetic", "real") and policy == "llm")
    real_img = (image_mode == "real" and policy == "llm")
    env_kwargs = dict(unity_exe_path=RENDER_EXE if real_img else HEADLESS_EXE,
                      unity_port=port, auto_start_unity=True,
                      max_episode_steps=rounds + 5, unity_log_path=ulog,
                      manual_transfers=manual_transfers)
    # base_seed + ep_idx: reproducible across runs, distinct within a run. Every
    # prompt variant benchmarked with the same base_seed sees the SAME scenarios,
    # which is what makes variant comparisons paired.
    if base_seed is not None:
        env_kwargs["seed"] = int(base_seed) + int(ep_idx)
    if MAP_CONFIG:
        env_kwargs["map_config"] = MAP_CONFIG
    if real_img:
        # Configure live capture so capture_frame works at decision time. PerStep also
        # auto-captures on advance (we ignore those); base64 off there to save TCP bytes.
        env_kwargs.update(frame_capture="step", frame_include_base64=False,
                          frame_dir=os.environ.get("ARC_FRAME_DIR", "render_frames_bench"))
    env = GameEnv(**env_kwargs)
    # Static tile lattice for synthetic rendering (loaded once per episode).
    grid = MAP_GRID_JSON if (use_image and image_mode == "synthetic") else None
    tmp_png = None
    if use_image and image_mode == "synthetic":
        tmp_png = str((Path(log_dir or ".") / f".synth_{model.replace('/','_')}_ep{ep_idx}_tools.png").resolve())
    # The LLM acts through typed tool calls on a state-only observation; the non-learning baselines
    # emit action indices directly and read the enumerated observation.
    tools_fmt = state_only = (policy == "llm")
    # Rendered once per episode. Non-LLM policies only record it for provenance.
    _sys_text = cora_prompts.render(pack, manual_transfers=manual_transfers,
                                    image_mode=image_mode if use_image else "none")
    if ablation:
        from cora.prompt_ablation import ablate
        _sys_text = ablate(_sys_text, ablation)
    rec = {
        # Recorded so the analysis can pair episode i of one variant against episode i
        # of another; None when the run was unseeded.
        "seed": (int(base_seed) + int(ep_idx)) if base_seed is not None else None,
        "model": model, "episode": ep_idx, "rounds": [], "error": None,
        "show_impacts": show_impacts,
           "action_format": "tools",
           "obs_encoding": (obs_encoding if state_only else "json"),
           # K = number of turns the policy sees INCLUDING the current one. K=1 is the legacy
           # stateless path; K>1 carries an append-only window of prior (state, action) turns.
           "history": (history if state_only else 1),
           "image_mode": image_mode if use_image else "none",
           "transfers": "manual" if manual_transfers else "task_only",
           # prompt identity is logged per episode so every record is attributable to an exact
           # system prompt (PIMMUR replicability): the variant label, a content hash, the exploration
           # knob actually used, and the full prompt text (a self-contained finetuning corpus).
           "system_variant": pack.name,
           "prompt_ablation": ablation or None,
           "prompt_sha": cora_prompts.prompt_sha(_sys_text),
           "reasoning_effort": reasoning_effort,
           # Total-generation cap actually in force on the LOCAL path (None = gateway path, or
           # the _EFFORT_BUDGET floor applied). Worth stamping: a reasoning-capable local model
           # that CoTs in the CONTENT channel (qwen3:4b does) gets truncated mid-prose by a low
           # cap and emits no action tag at all, so the cap silently decides the result.
           "max_tokens": LOCAL_MAX_TOKENS,
           "temperature": temperature,           # requested experimental level
           "temperature_sent": eff_temp,         # actually sent (Anthropic clamped to <=1.0)
           "system_prompt": _sys_text}
    try:
        env.reset()
        # Which scenario this episode actually ran: map fingerprint/source, parameter source and
        # seed as the GAME reports them (not as requested), so runs on different maps or sheets
        # are never pooled by accident.
        rec["scenario"] = (env.game_state or {}).get("scenario")
        total = 0.0
        actions_requested = actions_executed = action_failures = invalid_idx = 0
        min_budget = float("inf")
        built = hired = False
        # Append-only context buffer for history-carrying play (K>1): holds prior (state, action)
        # messages that ask_tools prepends to each call. None => stateless K=1 (the default).
        # max_pairs caps it to the K-1 most-recent prior turns (ask_tools adds the current turn).
        cmd_history = [] if (state_only and history and history > 1) else None
        max_pairs = 2 * (history - 1) if (history and history > 1) else 0
        # Previous round's structured state, fed to render_state_delta when obs_encoding=delta.
        # Only meaningful in history mode (the prior turn is in the visible window); None => the
        # delta renderer falls back to full compact, so delta+K=1 degrades gracefully to compact.
        prev_state = None
        for rnd in range(rounds):
            # Enumeration is the execution/validation backend for BOTH formats: requested indices
            # (LLM idx, parsed cmd tags, or baseline output) all index this list, so categorize from
            # it rather than from the observation (which omits the menu in cmd format).
            acts_enum = env.get_valid_actions()
            n_valid = len(acts_enum)
            state = observe(env.game_state, acts_enum, obs_config)
            _debug_choice_pipeline(env.game_state or {}, rnd)
            raw = rtrace = None; rtok = None; parsed_ok = None
            if validate or policy == "noop":
                dec = {"choices": [], "actions": []}            # no-op
            elif policy == "greedy":
                dec = greedy_decision(env); raw = json.dumps(dec)
            elif policy in ("build-potential", "rules-based"):
                dec = potential_decision(env, rnd, rounds); raw = json.dumps(dec)
            elif policy in ("choice-lookahead", "rules-based-v2"):
                dec = improved_rules_based_decision(env, rnd, rounds); raw = json.dumps(dec)
            elif policy == "combined":
                dec = combined_decision(env, rnd, rounds); raw = json.dumps(dec)
            elif policy == "pareto":
                dec = pareto_decision(env, rnd, rounds); raw = json.dumps(dec)
            elif policy == "demand-forecast":
                dec = demand_forecast_decision(env, rnd, rounds); raw = json.dumps(dec)
            elif policy == "demand-npv":
                dec = demand_npv_decision(env, rnd, rounds); raw = json.dumps(dec)
            elif policy == "random":
                dec = random_decision(env); raw = json.dumps(dec)
            else:                                               # llm
                img_b64 = _decision_image(image_mode, env, grid, tmp_png) if use_image else None
                if use_image:
                    rec["images_attached" if img_b64 else "images_missing"] = \
                        rec.get("images_attached" if img_b64 else "images_missing", 0) + 1
                try:
                    dec, raw, rtrace, rtok, parsed_ok = ask_tools(
                        client, model, state, env, _sys_text, img_b64, reasoning_effort,
                        eff_temp, obs_encoding, cmd_history,
                        prev_state if cmd_history is not None else None)
                    # Slide the window: ask_tools just appended this turn's messages; keep only the last
                    # K-1 prior turns so the cached prefix stays bounded (K=32 keeps the whole episode).
                    # Trim on TURN boundaries, not raw message count. Since the tool-call
                    # serialization fix a turn is no longer a fixed 2 messages -- it is
                    # user + assistant(tool_calls) + one `tool` reply PER call -- so cutting a
                    # fixed number of messages off the front lands mid-group and leaves `tool`
                    # messages whose tool_call_id has no matching assistant tool_calls. vLLM does
                    # not validate that pairing; Anthropic does, and rejected every h=4 episode
                    # with 400 "'tool_call_id' ... not found in 'tool_calls' of previous message"
                    # (32/32 episodes dead by round 2-3). Cut only at a `user` message so each
                    # assistant+tool group stays intact.
                    if cmd_history is not None and max_pairs:
                        starts = [i for i, m in enumerate(cmd_history) if m.get("role") == "user"]
                        keep = max_pairs // 2          # max_pairs counts 2 msgs per legacy turn
                        if len(starts) > keep:
                            del cmd_history[:starts[len(starts) - keep]]
                    elif cmd_history is not None:
                        cmd_history.clear()
                    prev_state = state   # next round's delta diffs against this turn's state
                except Exception as e:
                    # hard API/network error: end the episode
                    rec["error"] = f"LLM error r{rnd}: {e}"
                    break
            # ── execute: the model's tool calls, or a baseline's (task choices, action indices) ──
            if dec.get("tool_calls") is not None:
                call_results, step = executor.execute_turn(env, dec["tool_calls"])
                parsed_ok = not any(cr.malformed for cr in call_results)
                if not parsed_ok:
                    rec["parse_failures"] = rec.get("parse_failures", 0) + 1
            else:
                call_results, step = executor.execute_indices(env, dec.get("choices"), dec.get("actions"))
            obs, reward, term, trunc, info = step
            total += reward
            exres = info.get("execution_results") or []
            actions_requested += sum(len(cr.action_indices) for cr in call_results)
            actions_executed += sum(1 for r in exres if r.get("success"))
            action_failures += sum(1 for r in exres if not r.get("success"))
            invalid_idx += sum(1 for cr in call_results if cr.tool == "action" and cr.status == "invalid")
            built = built or any(cr.tool == "build" and cr.status == "executed" for cr in call_results)
            hired = hired or any(cr.tool == "hire" and cr.status == "executed" for cr in call_results)
            min_budget = min(min_budget, info.get("budget", 0.0))
            rec["rounds"].append(_round_record(rnd, reward, total, info, call_results, state, raw,
                                               dec, parsed_ok, rtrace, rtok))
            if term or trunc:
                rec["terminated"] = bool(term)
                break
        # ── episode aggregates (the mistake profile) ──
        last = rec["rounds"][-1] if rec["rounds"] else {}
        fr, ff = last.get("foodRes") or 0, last.get("foodFul") or 0
        lr, lf = last.get("lodgRes") or 0, last.get("lodgFul") or 0
        rec["summary"] = {
            "totalReward": round(total, 4),
            "finalSat": last.get("sat"), "finalBudget": last.get("budget"),
            "finalEff": last.get("eff"),
            "finalScore": round(last.get("sumR", 0.0), 4),
            "foodFulfillRate": round(ff / fr, 3) if fr else None,
            "lodgingFulfillRate": round(lf / lr, 3) if lr else None,
            "foodResolved": fr, "lodgingResolved": lr,
            "actionsRequested": actions_requested, "actionsExecuted": actions_executed,
            "actionFailures": action_failures, "invalidIndices": invalid_idx,
            "minBudget": None if min_budget == float("inf") else min_budget,
            "wentNegative": (min_budget < 0) if min_budget != float("inf") else None,
            "everBuilt": built, "everHired": hired,
            "terminated": rec.get("terminated", False),
            "roundsPlayed": len(rec["rounds"]),
            # Scores are comparable across front ends (all use cora.scoring) only under the
            # SAME weights.
            # Stamp them so a later retune can't silently make old and new runs
            # incomparable — and so a corpus can be re-scored under new weights.
            "rewardWeights": dict(REWARD_WEIGHTS),
        }
    except Exception as e:
        rec["error"] = f"{e}\n{traceback.format_exc()}"
    finally:
        env.close()
        if port_pool is not None:
            port_pool.put(port)
    return rec


# ── Aggregation ─────────────────────────────────────────────────────────────
def mean(xs):
    xs = [x for x in xs if x is not None]
    return sum(xs) / len(xs) if xs else None


def aggregate(records):
    by_model = {}
    for r in records:
        by_model.setdefault(r["model"], []).append(r)
    out = {}
    for model, recs in by_model.items():
        ok = [r for r in recs if r.get("summary") and not r.get("error")]
        s = [r["summary"] for r in ok]
        out[model] = {
            "episodes": len(recs), "completed": len(ok),
            "errors": [r["error"].splitlines()[0] for r in recs if r.get("error")][:5],
            "meanTotalReward": mean([x["totalReward"] for x in s]),
            "meanFinalSat": mean([x["finalSat"] for x in s]),
            "meanFinalBudget": mean([x["finalBudget"] for x in s]),
            "meanFoodFulfill": mean([x["foodFulfillRate"] for x in s]),
            "meanLodgingFulfill": mean([x["lodgingFulfillRate"] for x in s]),
            "meanActionFailures": mean([x["actionFailures"] for x in s]),
            "meanInvalidIdx": mean([x["invalidIndices"] for x in s]),
            "fracWentNegative": mean([1.0 if x["wentNegative"] else 0.0 for x in s]),
            "fracTerminated": mean([1.0 if x["terminated"] else 0.0 for x in s]),
            "fracNeverBuilt": mean([0.0 if x["everBuilt"] else 1.0 for x in s]),
            "fracNeverHired": mean([0.0 if x["everHired"] else 1.0 for x in s]),
        }
    return out


def print_table(agg):
    cols = [("model", 34), ("ep", 4), ("reward", 8), ("sat", 6), ("food%", 7),
            ("lodg%", 7), ("fail", 6), ("neg%", 6), ("term%", 6)]
    hdr = "".join(name.ljust(w) for name, w in cols)
    print("\n" + hdr); print("-" * len(hdr))
    for model, a in sorted(agg.items(), key=lambda kv: -(kv[1]["meanTotalReward"] or -1e9)):
        def f(v, p="{:.2f}"): return "-" if v is None else p.format(v)
        row = [model[:33], f"{a['completed']}/{a['episodes']}", f(a["meanTotalReward"]),
               f(a["meanFinalSat"], "{:.0f}"), f(a["meanFoodFulfill"]), f(a["meanLodgingFulfill"]),
               f(a["meanActionFailures"], "{:.1f}"), f(a["fracWentNegative"]), f(a["fracTerminated"])]
        print("".join(str(c).ljust(w) for c, (_, w) in zip(row, cols)))


_WB_COMP = ["sat_food", "sat_lodging", "sat_worker_use", "sat_waste", "sat_casework",
            "eff_food", "eff_lodging", "eff_worker"]


def log_wandb(records, project, condition, episodes, rounds):
    """Log one WandB run per model with game/* metrics matching the Verlog RL runs,
    so benchmark and RL overlay on the same project. Two series per run:
      - per-step (step/*): mean across episodes at each round   (x-axis = round)
      - per-episode (ep/*): each episode's summary               (x-axis = episode)
    Metric names mirror the env's info['metrics'] game/* keys."""
    try:
        import wandb
    except ImportError:
        print("⚠️  wandb not installed; skipping WandB logging (pip install wandb)")
        return
    entity, _, proj = project.partition("/")
    if not proj:
        entity, proj = None, project

    def avg(vals):
        vals = [v for v in vals if v is not None]
        return sum(vals) / len(vals) if vals else None

    by = {}
    for r in records:
        by.setdefault(r["model"], []).append(r)

    for model, recs in by.items():
        ok = [r for r in recs if r.get("rounds") and not r.get("error")]
        if not ok:
            continue
        short = (model.split("/")[-1].replace("us.anthropic.", "")
                 .replace("-20251001-v1:0", "").replace(":0", ""))
        wandb.init(entity=entity, project=proj, reinit=True,
                   name=f"bench-{short}-{condition}", group=f"benchmark-{condition}",
                   job_type="benchmark", tags=["benchmark", condition, short],
                   config={"model": model, "condition": condition, "episodes": episodes,
                           "rounds": rounds, "n_completed": len(ok), "source": "llm_benchmark"})
        wandb.define_metric("round"); wandb.define_metric("step/*", step_metric="round")
        wandb.define_metric("episode"); wandb.define_metric("ep/*", step_metric="episode")

        # ── per-step series: mean across episodes at each round ──
        maxr = max(len(r["rounds"]) for r in ok)
        for t in range(maxr):
            at = [r["rounds"][t] for r in ok if len(r["rounds"]) > t]
            if not at:
                continue
            row = {"round": t,
                   "step/game/satisfaction": avg([rd.get("sat") for rd in at]),
                   "step/game/budget": avg([rd.get("budget") for rd in at]),
                   "step/game/satisfaction_score": avg([rd.get("satScore") for rd in at]),
                   "step/game/efficiency": avg([rd.get("eff") for rd in at]),
                   "step/game/reward": avg([rd.get("reward") for rd in at]),
                   "step/game/score": avg([rd.get("sumR") for rd in at])}
            for c in _WB_COMP:
                row["step/game/" + c] = avg([(rd.get("comps") or {}).get(c) for rd in at])
            wandb.log({k: v for k, v in row.items() if v is not None})

        # ── per-episode series ──
        for r in sorted(ok, key=lambda r: r["episode"]):
            s = r["summary"]; rds = r["rounds"]; last = rds[-1].get("comps") or {}
            row = {"episode": r["episode"],
                   "ep/game/score": s.get("finalScore"), "ep/totalReward": s.get("totalReward"),
                   "ep/game/satisfaction_final": s.get("finalSat"),
                   "ep/game/satisfaction_mean": avg([rd.get("sat") for rd in rds]),
                   "ep/game/finalBudget": s.get("finalBudget"), "ep/game/minBudget": s.get("minBudget"),
                   "ep/game/foodFulfill": s.get("foodFulfillRate"),
                   "ep/game/lodgingFulfill": s.get("lodgingFulfillRate"),
                   "ep/actionFailures": s.get("actionFailures"),
                   "ep/wentNegative": 1.0 if s.get("wentNegative") else 0.0,
                   "ep/terminated": 1.0 if s.get("terminated") else 0.0}
            for c in _WB_COMP:
                if c in last:
                    row["ep/game/" + c + "_final"] = last[c]
            wandb.log({k: v for k, v in row.items() if v is not None})

        # ── run-level summary (means over episodes) ──
        S = [r["summary"] for r in ok]
        for src, dst in [("totalReward", "totalReward"), ("finalSat", "finalSat"),
                         ("foodFulfillRate", "foodFulfill"), ("lodgingFulfillRate", "lodgingFulfill"),
                         ("minBudget", "minBudget"), ("finalBudget", "finalBudget")]:
            wandb.run.summary["mean/" + dst] = avg([x.get(src) for x in S])
        wandb.finish()

    print(f"WandB: logged {len(by)} model run(s) to {project} (condition={condition})")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--episodes", type=int, default=20)
    ap.add_argument("--rounds", type=int, default=32)  # full game: 8 days x 4 rounds
    ap.add_argument("--workers", type=int, default=1)
    ap.add_argument("--models", default=",".join(DEFAULT_MODELS))
    ap.add_argument("--out", default="benchmark_results")
    ap.add_argument("--validate", action="store_true")
    # Ablation toggle: show each task choice's sparse impacts (Budget/Satisfaction/...)
    # in the observation. Unity always sends them; this controls what the model sees.
    ap.add_argument("--no-impacts", dest="impacts", action="store_false",
                    help="hide choice impacts from the observation (ablation baseline)")
    ap.set_defaults(impacts=True)
    # WandB: log one run per model with game/* + behavior metrics matching the RL runs,
    # so benchmark and Verlog RL are directly comparable on the same WandB project.
    ap.add_argument("--wandb", action="store_true", help="log results to Weights & Biases")
    ap.add_argument("--wandb-project", default="cpulling/CORA_RL",
                    help="entity/project (default cpulling/CORA_RL)")
    ap.add_argument("--seed", type=int, default=None,
                    help="base RNG seed; episode i uses base+i. Omit for unseeded "
                         "(previous behaviour). Two runs with the same --seed play "
                         "identical scenarios, which makes A/B comparisons paired.")
    ap.add_argument("--policy", choices=["llm", "greedy", "build-potential", "choice-lookahead",
                                        "combined", "pareto", "demand-npv", "demand-forecast", "random", "noop",
                                        "rules-based", "rules-based-v2"], default="llm",
                    help="llm = benchmark the --models. Non-learning baselines (no API): greedy; "
                         "build-potential (adds infrastructure building); choice-lookahead (picks "
                         "task choices by long-term value); combined (both); random; noop. "
                         "rules-based / rules-based-v2 are deprecated aliases for build-potential "
                         "/ choice-lookahead — the old names implied a version ordering that does "
                         "not exist: they are different algorithms improving opposite halves.")
    ap.add_argument("--obs_encoding", choices=["json", "compact", "delta"], default="json",
                    help="cmd-format state serialization: json = json.dumps (default); "
                         "compact = safe-set tabular/text renderer (~62%% fewer tokens, same actionable "
                         "facts: schema-once facilities table, no task prose, ids-only sites, collapsed "
                         "transfer cross-product); delta = compact but the facilities block is diffed "
                         "vs the previous in-window turn (unchanged rows omitted), actionable surface "
                         "kept full + cache-safe. delta is meant for history mode (--history>1); at K=1 "
                         "it degrades to compact. cmd-only.")
    ap.add_argument("--history", type=int, default=1,
                    help="K = turns the policy sees INCLUDING the current one. 1 = stateless (default, "
                         "fresh [system, current_state] each round); K>1 carries an append-only window "
                         "of the K-1 prior (state, action) turns (K=32 = whole episode). cmd-only; the "
                         "prefix is kept byte-stable + image-free so provider prefix-caching applies.")
    ap.add_argument("--image_mode", choices=["none", "synthetic", "real"], default="none",
                    help="decision-time image shown to the LLM: none = text-only (default); "
                         "synthetic = rendered dashboard (fast Server build); "
                         "real = live game frame captured each round (graphics render build). LLM-only.")
    ap.add_argument("--max_tokens", type=int, default=None,
                    help="Explicit total-generation budget (reasoning + answer) for LOCAL models; "
                         "overrides the --reasoning_effort floor, including below it (e.g. 3000 with "
                         "effort=low). No effect on the CMU-gateway path.")
    ap.add_argument("--no-thinking", action="store_true",
                    help="LOCAL path only: send chat_template_kwargs={'enable_thinking': false}. "
                         "On Qwen3/Qwen3.5 templates this moves the chain-of-thought out of the "
                         "visible content (measured on qwen3:4b: 16k chars -> ~0). It does NOT "
                         "make the model generate fewer tokens -- ~3-5k either way. Ignored by "
                         "templates that do not declare the variable, and dropped on rejection.")
    ap.add_argument("--reasoning_effort", choices=["none", "low", "medium", "high", "xhigh"], default="low",
                    help="hidden-thinking budget for reasoning models (gpt-5*, gemini 2.5/3.x, and "
                         "local Ollama reasoning models via --base-url: qwen3, qwen3.5, gpt-oss). "
                         "'none' disables thinking entirely (local only; gateway gpt-5*/gemini keep a "
                         "small budget). Token headroom scales with effort; reasoning_tokens spent is "
                         "logged per round. no-op for non-reasoning models. Default low.")
    ap.add_argument("--transfers", choices=["manual", "task_only"], default="manual",
                    help="resource-transfer affordance: manual (default) enumerates standalone "
                         "food/people transfers (idx menu + <transfer> cmd tag) — LLMs coordinate "
                         "micro-logistics directly; task_only suppresses them so transfers happen "
                         "ONLY via task choices, matching the human GUI. LLM-only knob.")
    ap.add_argument("--map-config", default=None,
                    help="map JSON for every episode, or 'none' for the scene's built-in layout "
                         "(default: the build's config.json / bundled map_config.json)")
    ap.add_argument("--prompt", default=cora_prompts.DEFAULT_PACK,
                    help="prompt pack: a name in prompts/ (" + ", ".join(cora_prompts.list_packs()) +
                         ") or a path to a pack JSON. Recorded per episode with its prompt_sha.")
    ap.add_argument("--ablate", default="",
                    help="rule ablation applied to the rendered prompt: R07 | R07,R09 | R07_P1_direct "
                         "(see cora/prompt_ablation.py)")
    ap.add_argument("--temperature", type=float, default=None,
                    help="sampling temperature; sent ONLY to models that accept it (Gemini 2.5/3.x). "
                         "gpt-5* reasoning models reject it and use --reasoning_effort instead. "
                         "Default None = vendor default. Logged per episode.")
    ap.add_argument("--base-port", type=int, default=BASE_PORT,
                    help="gym base port; bump to run concurrently with another benchmark")
    ap.add_argument("--base-url", default=None,
                    help="OpenAI-compatible endpoint override (default = CMU gateway). Point at a "
                         "local server to benchmark a self-hosted model, e.g. an Ollama instance: "
                         "http://localhost:11434/v1 with --models qwen2.5:3b. LLM-only.")
    ap.add_argument("--api-key", default=None,
                    help="API key for --base-url. Defaults to the gateway key from env/.env; for a "
                         "local server pass any placeholder (e.g. 'ollama'). LLM-only.")
    args = ap.parse_args()
    if args.map_config:
        globals()["MAP_CONFIG"] = args.map_config

    need_render = (args.image_mode == "real" and args.policy == "llm")
    if not Path(RENDER_EXE if need_render else HEADLESS_EXE).exists():
        sys.exit(f"Build not found: {RENDER_EXE if need_render else HEADLESS_EXE}")
    if args.image_mode == "synthetic" and not Path(MAP_GRID_JSON).exists():
        sys.exit(f"Synthetic mode needs the tile grid: {MAP_GRID_JSON} (run export_map_grid.py)")

    if args.validate:
        print("=== VALIDATE: 1 no-LLM episode (2 rounds), fresh process ===")
        rec = run_episode("validate", 0, 2, BASE_PORT, None, validate=True)
        print(json.dumps(rec.get("summary") or {"error": rec.get("error")}, indent=2))
        return

    # Non-learning baselines need no LLM: one pseudo-model labelled by the policy.
    if args.policy != "llm":
        models = [args.policy]
        client = None
    else:
        models = [m.strip() for m in args.models.split(",") if m.strip()]
        # ARC_API_KEY lets an endpoint take its key from the environment instead of --api-key: a
        # command-line key is visible in `ps` to every user on a shared node.
        api_key = args.api_key or os.environ.get("ARC_API_KEY")
        # ARC_ANTHROPIC_NATIVE=1 uses the native Anthropic SDK (prompt caching; the OpenAI-compat
        # layer has none). --base-url points at any OpenAI-compatible server (vLLM, Ollama);
        # the default is the CMU gateway.
        if os.environ.get("ARC_ANTHROPIC_NATIVE") == "1":
            client = client_for(Provider.anthropic, api_key)
        elif args.base_url:
            client = client_for(ProviderSpec("openai", args.base_url, None), api_key)
        else:
            client = client_for(Provider.cmu_gateway, api_key)
        if args.base_url:
            # Local OpenAI-compat server (Ollama): forward --reasoning_effort so chat() can cap or
            # disable thinking on reasoning models (Ollama auto-enables it otherwise). Not set for
            # the CMU gateway, where Claude rejects the knob and gpt-5*/gemini handle it in-branch.
            _set_local_reasoning_effort(args.reasoning_effort)
            _set_local_max_tokens(args.max_tokens)
            if args.no_thinking:
                _set_local_chat_template_kwargs({"enable_thinking": False})
            print(f"    endpoint:      {args.base_url} (local/override)")
            print(f"    local thinking: reasoning_effort={args.reasoning_effort}"
                  f"{' (thinking OFF)' if args.reasoning_effort == 'none' else ''}"
                  f"{f', max_tokens={args.max_tokens}' if args.max_tokens else ''}"
                  f"{', enable_thinking=false' if args.no_thinking else ''}")
    outdir = Path(args.out); outdir.mkdir(parents=True, exist_ok=True)
    jsonl = outdir / "episodes.jsonl"

    jobs = [(m, e) for m in models for e in range(args.episodes)]
    print(f"=== Benchmark: {len(models)} models x {args.episodes} eps x {args.rounds} rounds "
          f"= {len(jobs)} episodes, {args.workers} worker(s) ===")
    print(f"    models: {models}")
    print(f"    observation choice-impacts: {'SHOWN' if args.impacts else 'HIDDEN (ablation)'}")
    if args.policy == "llm":
        print("    actions:       typed tool calls (game actions only; tools return nothing)")
        print(f"    image mode:    {args.image_mode}"
              f"{' (graphics render build)' if need_render else ''}")
        print(f"    reasoning:     effort={args.reasoning_effort} "
              f"(budget {_EFFORT_BUDGET[args.reasoning_effort]} tok; reasoning_tokens logged/round)")
        print(f"    transfers:     {args.transfers} "
              f"({'standalone food/people transfers exposed to the LLM' if args.transfers == 'manual' else 'human-faithful — transfers only via task choices'})")
        print(f"    prompt:        {args.prompt}{' (ablation ' + args.ablate + ')' if args.ablate else ''}")
        print(f"    temperature:   {args.temperature if args.temperature is not None else 'vendor default'} "
              f"(Gemini only; gpt-5* use reasoning_effort)")

    port_pool = queue.Queue()
    for w in range(args.workers):
        port_pool.put(args.base_port + w)

    ulog_dir = outdir / "unity_logs"; ulog_dir.mkdir(exist_ok=True)

    records = []
    with open(jsonl, "w") as fh, ThreadPoolExecutor(max_workers=args.workers) as ex:
        futs = {}
        for model, ep in jobs:
            futs[ex.submit(run_episode, model, ep, args.rounds, None, client,
                           False, port_pool, str(ulog_dir), args.impacts, args.policy,
                           args.image_mode, args.reasoning_effort,
                           args.transfers == "manual", args.prompt,
                           args.temperature, args.obs_encoding, args.history,
                           args.ablate, args.seed)] = (model, ep)
        for fut in as_completed(futs):
            model, ep = futs[fut]
            rec = fut.result()
            records.append(rec)
            fh.write(json.dumps(rec) + "\n"); fh.flush()
            s = rec.get("summary") or {}
            print(f"  done {model} ep{ep}: reward={s.get('totalReward')} "
                  f"sat={s.get('finalSat')} food={s.get('foodFulfillRate')} "
                  f"lodg={s.get('lodgingFulfillRate')} {'ERR:'+rec['error'].splitlines()[0] if rec.get('error') else ''}")

    agg = aggregate(records)
    (outdir / "summary.json").write_text(json.dumps(agg, indent=2))
    print_table(agg)
    print(f"\nPer-episode: {jsonl}\nSummary:     {outdir/'summary.json'}")

    if args.wandb:
        # Encode the experiment cell into the WandB condition so each cell is its own run group.
        cond = (f"img-{args.image_mode}_tools"
                + ("" if args.impacts else "_noimpacts")
                + ("" if args.reasoning_effort == "low" else f"_eff-{args.reasoning_effort}")
                + ("" if args.transfers == "manual" else "_xfer-task_only")
                + f"_prompt-{os.path.splitext(os.path.basename(args.prompt))[0]}"
                + (f"_ablate-{args.ablate}" if args.ablate else "")
                + ("" if args.obs_encoding == "json" else f"_obs-{args.obs_encoding}")
                + ("" if args.history == 1 else f"_k{args.history}")
                + ("" if args.temperature is None else f"_temp-{args.temperature}"))
        log_wandb(records, args.wandb_project, cond, args.episodes, args.rounds)


if __name__ == "__main__":
    main()
