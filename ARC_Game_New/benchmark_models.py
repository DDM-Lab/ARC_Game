"""
Flagship-model benchmark for the ARC gym environment.

Runs N full episodes per model with the SAME rules-only prompt + rich observation
as llm_smoke_test.py, then reports per-model performance and a shared "mistake"
profile. The point is to separate three explanations for poor play:
  (a) some models play well and others don't  -> model decision-making differs
  (b) all models fail the SAME way            -> prompt/observation/env issue
  (c) all models play well                    -> the game is easy / obs is fine

Each episode gets a FRESH headless Unity process (clean game) — the gym server has
no in-place reset, so we relaunch per episode on a per-worker port. Episodes can run
concurrently across workers (each worker owns one port + one Unity process).

This is an EVAL harness: it reuses ARCGameGymEnv.reset()/step() and the smoke-test's
summarize()/ask()/prompt verbatim — it is not a new rollout engine and does not patch
the env. All reward scoring stays in arc_game_gym_env_tcp.compute_score.

Usage:
  python benchmark_models.py [--episodes N] [--rounds R] [--workers K]
                             [--models m1,m2,...] [--out DIR] [--validate]

  --validate runs ONE 2-round no-LLM (no-op) episode to confirm the fresh-process
  lifecycle works before spending any API budget.
"""
import os, sys, json, argparse, traceback, queue, base64, hashlib
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed

sys.path.insert(0, str(Path(__file__).parent))
from arc_game_gym_env_tcp import ARCGameGymEnv
import llm_smoke_test as smoke
import prompt_packs                     # declarative JSON prompt packs (opt-in via --prompt-pack)
import openai

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
_EFFORT_BUDGET = {"none": 2000, "low": 6000, "medium": 12000, "high": 20000}

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
            kw["reasoning_effort"] = LOCAL_REASONING_EFFORT
            kw["max_tokens"] = (LOCAL_MAX_TOKENS if LOCAL_MAX_TOKENS is not None
                                else max(max_tokens, _EFFORT_BUDGET.get(LOCAL_REASONING_EFFORT, max_tokens)))
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
    content = m.content or ""
    # Provider reasoning tokens land in non-standard fields depending on vendor/gateway.
    extra = getattr(m, "model_extra", None) or {}
    rtrace = (getattr(m, "reasoning_content", None) or getattr(m, "reasoning", None)
              or extra.get("reasoning_content") or extra.get("reasoning"))
    det = getattr(getattr(r, "usage", None), "completion_tokens_details", None)
    rtok = getattr(det, "reasoning_tokens", None) if det else None
    return content, (rtrace if isinstance(rtrace, str) else None), rtok


def _clean_llm_json(text):
    """Strip the non-JSON garnish models add: markdown code fences and JS-style // and /* */
    comments (gemini annotates actions like `1, // Build Shelter, Cost $1000` — those stray
    numbers would otherwise be scraped as action indices)."""
    import re
    t = text or ""
    t = re.sub(r"```(?:json)?", "", t)            # markdown fences
    t = re.sub(r"/\*.*?\*/", "", t, flags=re.S)   # block comments
    t = re.sub(r"//[^\n\r]*", "", t)              # line comments
    return t


def _extract_json_object(text):
    """Best-effort parse of the FIRST balanced {...} object in `text`. Tolerates code fences,
    a missing leading brace (gemini sometimes drops it), trailing commas, and literal
    control chars inside strings (strict=False). Returns the dict or None."""
    import re
    text = _clean_llm_json(text)
    start = text.find("{")
    if start == -1:                                          # gemini sometimes omits the opening {
        if '"reasoning"' in text or '"actions"' in text or '"choices"' in text:
            text = "{" + text; start = 0
        else:
            return None
    depth = 0                                                # brace-balance to the matching close
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                cand = text[start:i + 1]
                for c in (cand, re.sub(r",\s*([}\]])", r"\1", cand)):
                    try:
                        return json.loads(c, strict=False)
                    except Exception:
                        pass
                break
    return None


def _regex_actions_choices(text):
    """Last-resort extraction of just the actions[] and choices[] arrays via regex, for when the
    full object won't parse (malformed entries, prose mixed in). We only need these two to act."""
    import re
    text = _clean_llm_json(text)
    am = re.search(r'"actions"\s*:\s*\[([^\]]*)\]', text, re.S)
    cm = re.search(r'"choices"\s*:\s*\[(.*?)\]', text, re.S)
    if not am and not cm:
        return None
    actions = [int(x) for x in re.findall(r"-?\d+", am.group(1))] if am else []
    choices = []
    if cm:
        for pair in re.findall(r"\{[^}]*\}", cm.group(1)):
            t = re.search(r'"taskId"\s*:\s*(\d+)', pair)
            c = re.search(r'"choiceId"\s*:\s*(\d+)', pair)
            if t and c:
                choices.append({"taskId": int(t.group(1)), "choiceId": int(c.group(1))})
    rm = re.search(r'"reasoning"\s*:\s*"(.*?)"\s*[,}]', text, re.S)
    return {"choices": choices, "actions": actions,
            "reasoning": (rm.group(1)[:500] if rm else ""), "note": "regex-fallback"}


def _parse_decision(content):
    """Robustly extract a decision from an LLM response. Models (esp. gemini) wrap JSON in code
    fences, drop the leading brace, pretty-print across lines, put literal newlines in strings, or
    add a malformed entry. Try a balanced-brace parse first, then a regex pull of just
    actions[]/choices[]. A genuinely unusable response degrades to a no-op, never crashes."""
    obj = _extract_json_object(content)
    if obj is not None:
        return obj, True
    dec = _regex_actions_choices(content)
    if dec is not None:
        return dec, True
    return {"choices": [], "actions": []}, False


# Appended to the system prompt ONLY in the image arms, so the model knows the
# attached image is a current-state view to reason over (not decoration). Kept out
# of the text-only arms so those prompts stay byte-identical to the original benchmark.
IMAGE_PREAMBLE = {
    "synthetic": ("\n\nYou are ALSO given a rendered top-down map of the CURRENT state: tile terrain "
                  "(grass/road/river/forest), build-sites (gold stars, #id), built facilities, and "
                  "communities colored by food deficit, plus food/lodging/budget panels. Use it for "
                  "spatial + resource reasoning."),
    "real": ("\n\nYou are ALSO given a live top-down screenshot of the current game UI (map + any "
             "open panels). Use it to read the spatial layout and on-screen state."),
}


def _system_content(base, image_b64, image_mode):
    """Base system prompt, plus the mode-specific image line when an image is attached."""
    return base + (IMAGE_PREAMBLE.get(image_mode, "") if image_b64 else "")


def _user_msg(text, image_b64=None):
    """Build a user message, multimodal when an image is supplied. The image is a
    decision-time view of the same state (synthetic dashboard or real game frame)."""
    if not image_b64:
        return {"role": "user", "content": text}
    return {"role": "user", "content": [
        {"type": "text", "text": text},
        {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{image_b64}"}}]}


def ask(client, model, state, image_b64=None, image_mode="none", reasoning_effort="low",
        system_variant="original", temperature=None, prompt_pack=None):
    """Return (decision_dict, raw_content, reasoning_trace, reasoning_tokens, parsed_ok). raw_content
    is the full visible response (kept verbatim so we can inspect any prose/think emitted).
    prompt_pack (a loaded pack dict) overrides the built-in idx system prompt when supplied."""
    _base = (prompt_packs.render(prompt_pack, has_image=False)
             if prompt_pack else smoke.idx_system_prompt(system_variant))
    content, rtrace, rtok = chat(client, model, [
        {"role": "system", "content": _system_content(_base, image_b64, image_mode)},
        _user_msg("State:\n" + json.dumps(state) + "\n\nJSON decision:", image_b64)],
        reasoning_effort=reasoning_effort, temperature=temperature)
    dec, ok = _parse_decision(content)
    return dec, content, rtrace, rtok, ok


def ask_cmd(client, model, state, env, image_b64=None, image_mode="none", reasoning_effort="low",
            system_variant="original", temperature=None, obs_encoding="json", history=None,
            prev_state=None, prompt_pack=None):
    """Command-tag analogue of ask(): state-only obs + the command grammar (no enumerated menu),
    parsing the emitted <build>/<hire>/<staff>/<task>/... tags into (actions, choices) against the
    round's enumeration. Returns the same (decision_dict, raw_content, reasoning_trace,
    reasoning_tokens, parsed_ok) 5-tuple so the episode loop is format-agnostic. parsed_ok is False
    only when the model produced text but no tag parsed AND the parser flagged errors.
    obs_encoding {json, compact}: how the state is serialized into the user message. `compact` uses
    the safe-set tabular/text renderer (~62% fewer tokens, same actionable facts) for trajectory-length
    reduction; `json` is json.dumps. Identical state dict either way — only the rendering differs.

    history: when a list is passed, the policy becomes history-carrying (K>1). The list holds the
    prior turns' (user-state, assistant-action) message pairs and is the append-only context the
    model sees alongside the current state. We send [system] + history + [current state]; after the
    call we append THIS turn's text-only state (image bytes dropped to keep the cached prefix small
    and byte-stable) and the model's VISIBLE output (actions, not hidden CoT — the 'actions-only'
    history BALROG/Verlog use). The caller owns trimming the list to the K window. history=None is
    the legacy stateless (K=1) path."""
    import re
    _mt = getattr(env, "manual_transfers", True)
    sys_prompt = (prompt_packs.render(prompt_pack, manual_transfers=_mt, has_image=False)
                  if prompt_pack else smoke.cmd_system_prompt(_mt, system_variant))
    if obs_encoding == "delta":
        # facilities diffed vs the previous (in-window) turn; actionable surface stays full.
        # prev_state=None (round 0, or non-history mode) falls back to the full compact render.
        rendered = smoke.render_state_delta(state, prev_state)
    elif obs_encoding == "compact":
        rendered = smoke.render_state_compact(state)
    else:
        rendered = json.dumps(state)
    user_text = "State:\n" + rendered + "\n\nCommands:"
    # system first (static => cache-anchored), then the append-only prior turns, then current state.
    msgs = [{"role": "system", "content": _system_content(sys_prompt, image_b64, image_mode)}]
    if history:
        msgs.extend(history)
    msgs.append(_user_msg(user_text, image_b64))
    content, rtrace, rtok = chat(client, model, msgs,
        reasoning_effort=reasoning_effort, temperature=temperature)
    if history is not None:
        history.append(_user_msg(user_text, None))            # text-only: keep the prefix light/stable
        history.append({"role": "assistant", "content": content or ""})
    pc = smoke.parse_commands(content, env)
    reason = ""
    m = re.search(r"REASONING:\s*(.+)", content or "")
    if m:
        reason = m.group(1).splitlines()[0].strip()
    dec = {"choices": pc["choices"], "actions": pc["actions"],
           "reasoning": reason, "note": " ".join(pc["parsed"])[:120], "errors": pc["errors"]}
    ok = bool(pc["actions"] or pc["choices"]) or not pc["errors"]
    return dec, content, rtrace, rtok, ok


def ask_tools(client, model, state, env, image_b64=None, image_mode="none", reasoning_effort="low",
              system_variant="minimal", temperature=None, obs_encoding="json", history=None,
              prev_state=None, prompt_pack=None):
    """Tool-use analogue of ask_cmd(): state-only obs + the TYPED tool schema (cora_tools). The
    model emits NATIVE tool_calls; cora_tools.translate_tool_calls -> command tags ->
    smoke.parse_commands, returning the SAME 5-tuple so the episode loop is format-agnostic. This
    is the benchmark wing of the Phase B unification — the identical tool schema the live officer
    offers and the RL policy trains on."""
    import re
    import cora_tools
    import cora_prompts
    _mt = getattr(env, "manual_transfers", True)
    sys_prompt = (prompt_packs.render(prompt_pack, manual_transfers=_mt, has_image=False)
                  if prompt_pack else cora_prompts.tool_system_prompt(manual_transfers=_mt, variant=system_variant))
    if obs_encoding == "delta":
        rendered = smoke.render_state_delta(state, prev_state)
    elif obs_encoding == "compact":
        rendered = smoke.render_state_compact(state)
    else:
        rendered = json.dumps(state)
    user_text = "State:\n" + rendered + "\n\nAct by calling the tools."
    msgs = [{"role": "system", "content": _system_content(sys_prompt, image_b64, image_mode)}]
    if history:
        msgs.extend(history)
    msgs.append(_user_msg(user_text, image_b64))
    tools = cora_tools.openai_tools(manual_transfers=_mt)
    kw = dict(model=model, messages=msgs, tools=tools, max_tokens=2000)
    if temperature is not None:
        kw["temperature"] = temperature
    try:
        r = client.chat.completions.create(**kw)
    except Exception as e:
        emsg = str(e).lower()
        if "temperature" in emsg:
            kw.pop("temperature", None)
        if "max_tokens" in emsg or "max_completion_tokens" in emsg:
            kw.pop("max_tokens", None); kw["max_completion_tokens"] = 2000
        r = client.chat.completions.create(**kw)
    m = r.choices[0].message
    content = m.content or ""
    raw_tcs = getattr(m, "tool_calls", None) or []
    tcs = [(tc.function.name, tc.function.arguments) for tc in raw_tcs]
    tags, tmeta = cora_tools.translate_tool_calls(tcs)
    if history is not None:
        history.append(_user_msg(user_text, None))
        history.append({"role": "assistant", "content": content or tags})
    pc = smoke.parse_commands(tags, env)
    reason = ""
    mr = re.search(r"REASONING:\s*(.+)", content or "")
    if mr:
        reason = mr.group(1).splitlines()[0].strip()
    dec = {"choices": pc["choices"], "actions": pc["actions"], "reasoning": reason,
           "note": " ".join(pc["parsed"])[:120], "errors": pc["errors"], "tool_meta": tmeta}
    # parsed_ok: at least one action/choice resolved, OR the model emitted well-formed tool calls
    # (received > 0 with no unknown-name / bad-args) — a clean no-op turn, not a parse failure.
    ok = bool(pc["actions"] or pc["choices"]) or (
        tmeta["received"] > 0 and tmeta["bad_args"] == 0 and tmeta["unknown_name"] == 0)
    return dec, content, None, None, ok


# ── Non-learning baseline policies (operate on the full env, not the prompt) ──
from arc_game_gym_env_tcp import REWARD_WEIGHTS


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


def greedy_decision(env, w=REWARD_WEIGHTS):
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
                v = (1.0 if (acting and demand) else 0.0) + 0.01 * s - w["w_food_cost"] * cost
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
_POT_MIN_HORIZON = 4        # don't build with fewer rounds left — can't amortize
_POT_KITCHEN_TARGET = 2     # operational kitchens to aim for (food + worker employment)
_POT_CASEWORK_BUILD_ROUND = 0  # build the casework site EARLY. The workforce is capped (~3-4 operational
                               # buildings), and a building only gets staffed if free workers exist when it's
                               # built — deferring the casework build to ~round 8 left it permanently
                               # NeedWorker (workers already committed to shelters) → 0 processed. Building it
                               # first claims its 4 workers up front, which is the only way it stays operational.
                               # The cost (~one shelter's staffing → lower lodging) is inherent to the worker cap.
_POT_BUDGET_RESERVE = 1500  # keep this much budget before discretionary building
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


def potential_decision(env, rnd=0, rounds_total=32, w=REWARD_WEIGHTS):
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = gs.get("mapState", {}).get("facilities", []) or []
    budget = float((gs.get("satisfactionAndBudget") or {}).get("budget", 0) or 0)
    rounds_left = max(0, rounds_total - rnd)

    # Reuse greedy's RELIABLE choices + worker assignments (it prefers the acting/
    # immediate options that actually fulfill). Potential adds *building* on top — the
    # free/deferred options fail until infrastructure is stocked, so don't switch to
    # them; keep reliable fulfillment and let building pay off via worker_use + capacity.
    base = greedy_decision(env, w)
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


def _lt_choice_value(c, demand, rounds_left, w):
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
    v = (1.0 if (acting and demand) else 0.0) + 0.01 * s - w["w_food_cost"] * (cost + recurring)
    return (v, acting and demand, is_shelter)


def improved_rules_based_decision(env, rnd=0, rounds_total=32, w=REWARD_WEIGHTS):
    gs = env.game_state or {}
    va = env.valid_actions or []
    ms = gs.get("mapState", {}) or {}
    facs = ms.get("facilities", []) or []
    budget = float((gs.get("satisfactionAndBudget") or {}).get("budget", 0) or 0)
    rounds_left = max(0, rounds_total - rnd)

    # reactive core: keep greedy's free worker assignments, but REPLACE its myopic choices
    # with long-term-aware ones (the motel's recurring cost is priced in by _lt_choice_value,
    # so relocations route into free shelters automatically — when those shelters have space).
    base = greedy_decision(env, w)
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
            v, fdem, is_shel = _lt_choice_value(c, demand, rounds_left, w)
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
    # this turn. The game has NO per-day hiring ceiling (action_enumerator: hiring is budget-limited;
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


def combined_decision(env, rnd=0, rounds_total=32, w=REWARD_WEIGHTS):
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
    lt = improved_rules_based_decision(env, rnd, rounds_total, w)
    pot = potential_decision(env, rnd, rounds_total, w)

    seen, actions = set(), []
    for i in list(pot.get("actions") or []) + list(lt.get("actions") or []):
        if i not in seen:
            seen.add(i); actions.append(i)

    return {"choices": lt.get("choices") or [], "actions": actions, "note": "combined",
            "reasoning": "lt-value choices + potential building + shelter transfers"}


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
            resp = env._send_request({"type": "capture_frame"})
            return (resp or {}).get("frame_base64")
    except Exception as e:
        print(f"    [image:{image_mode}] capture failed: {type(e).__name__}: {str(e)[:80]}")
    return None


# ── One episode ─────────────────────────────────────────────────────────────
def run_episode(model, ep_idx, rounds, port, client, validate=False, port_pool=None, log_dir=None,
                show_impacts=True, policy="llm", action_format="idx", image_mode="none",
                reasoning_effort="low", manual_transfers=True, system_variant="original",
                temperature=None, obs_encoding="json", history=1, prompt_pack=None):
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
    smoke._set_v2(system_variant == "minimal_v2")
    # Anthropic caps temperature at 1.0; clamp per-model so a shared sweep invocation (e.g. temp=1.5
    # for gemini) doesn't 400 Claude. eff_temp is what's actually sent + logged; temperature is the
    # requested experimental level.
    eff_temp = temperature
    if temperature is not None and _is_anthropic(model):
        eff_temp = min(temperature, ANTHROPIC_TEMP_MAX)
    ulog = None
    if log_dir:
        safe = model.replace("/", "_").replace(":", "_")
        ulog = str((Path(log_dir) / f"unity_{safe}_ep{ep_idx}_{image_mode}_{action_format}.log").resolve())
    use_image = (image_mode in ("synthetic", "real") and policy == "llm")
    real_img = (image_mode == "real" and policy == "llm")
    env_kwargs = dict(unity_exe_path=RENDER_EXE if real_img else HEADLESS_EXE,
                      unity_port=port, auto_start_unity=True,
                      max_episode_steps=rounds + 5, unity_log_path=ulog,
                      manual_transfers=manual_transfers)
    if real_img:
        # Configure live capture so capture_frame works at decision time. PerStep also
        # auto-captures on advance (we ignore those); base64 off there to save TCP bytes.
        env_kwargs.update(frame_capture="step", frame_include_base64=False,
                          frame_dir=os.environ.get("ARC_FRAME_DIR", "render_frames_bench"))
    env = ARCGameGymEnv(**env_kwargs)
    # Static tile lattice for synthetic rendering (loaded once per episode).
    grid = MAP_GRID_JSON if (use_image and image_mode == "synthetic") else None
    tmp_png = None
    if use_image and image_mode == "synthetic":
        tmp_png = str((Path(log_dir or ".") / f".synth_{model.replace('/','_')}_ep{ep_idx}_{action_format}.png").resolve())
    # Command-tag format only applies to the LLM policy; the non-learning baselines emit action
    # indices directly and always use the enumerated (idx) observation.
    cmd_fmt = (action_format == "cmd" and policy == "llm")
    tools_fmt = (action_format == "tools" and policy == "llm")
    state_only = cmd_fmt or tools_fmt   # both use state-only obs + a non-idx action surface
    if prompt_pack:
        _base = prompt_packs.render(prompt_pack, manual_transfers=manual_transfers, has_image=False)
    elif tools_fmt:
        import cora_prompts as _cp
        _base = _cp.tool_system_prompt(manual_transfers=manual_transfers, variant=system_variant)
    else:
        _base = (smoke.cmd_system_prompt(manual_transfers, system_variant) if cmd_fmt
                 else smoke.idx_system_prompt(system_variant))
    _sys_text = _system_content(_base, "x" if use_image else None, image_mode)
    rec = {"model": model, "episode": ep_idx, "rounds": [], "error": None, "show_impacts": show_impacts,
           "action_format": action_format if state_only else "idx",
           "obs_encoding": (obs_encoding if state_only else "json"),
           # K = number of turns the policy sees INCLUDING the current one. K=1 is the legacy
           # stateless path; K>1 carries an append-only window of prior (state, action) turns.
           "history": (history if state_only else 1),
           "image_mode": image_mode if use_image else "none",
           "transfers": "manual" if manual_transfers else "task_only",
           # prompt identity is logged per episode so every record is attributable to an exact
           # system prompt (PIMMUR replicability): the variant label, a content hash, the exploration
           # knob actually used, and the full prompt text (a self-contained finetuning corpus).
           "system_variant": system_variant,
           "prompt_pack": (prompt_pack.get("name") if prompt_pack else None),
           "prompt_sha": hashlib.sha1(_sys_text.encode("utf-8")).hexdigest()[:12],
           "reasoning_effort": reasoning_effort,
           "temperature": temperature,           # requested experimental level
           "temperature_sent": eff_temp,         # actually sent (Anthropic clamped to <=1.0)
           "system_prompt": _sys_text}
    try:
        env.reset()
        total = 0.0
        actions_requested = actions_executed = action_failures = invalid_idx = 0
        min_budget = float("inf")
        built = hired = False
        # Append-only context buffer for history-carrying play (K>1): holds prior (state, action)
        # message pairs that ask_cmd prepends to each call. None => legacy stateless K=1 path.
        # max_pairs caps it to the K-1 most-recent prior turns (ask_cmd adds the current turn).
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
            if state_only:
                state = smoke.summarize_commands(env, show_impacts=show_impacts, rounds_left=rounds - rnd)
            else:
                state = smoke.summarize(env, show_impacts=show_impacts, rounds_left=rounds - rnd)
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
            elif policy == "random":
                dec = random_decision(env); raw = json.dumps(dec)
            else:                                               # llm
                img_b64 = _decision_image(image_mode, env, grid, tmp_png) if use_image else None
                if use_image:
                    rec["images_attached" if img_b64 else "images_missing"] = \
                        rec.get("images_attached" if img_b64 else "images_missing", 0) + 1
                try:
                    if tools_fmt:
                        dec, raw, rtrace, rtok, parsed_ok = ask_tools(
                            client, model, state, env, img_b64, image_mode, reasoning_effort,
                            system_variant, eff_temp, obs_encoding, cmd_history,
                            prev_state if cmd_history is not None else None,
                            prompt_pack=prompt_pack)
                    elif cmd_fmt:
                        dec, raw, rtrace, rtok, parsed_ok = ask_cmd(
                            client, model, state, env, img_b64, image_mode, reasoning_effort,
                            system_variant, eff_temp, obs_encoding, cmd_history,
                            prev_state if cmd_history is not None else None,
                            prompt_pack=prompt_pack)
                    else:
                        dec, raw, rtrace, rtok, parsed_ok = ask(
                            client, model, state, img_b64, image_mode, reasoning_effort,
                            system_variant, eff_temp, prompt_pack=prompt_pack)
                    # Slide the window: ask_cmd just appended this turn's pair; keep only the last
                    # K-1 prior turns so the cached prefix stays bounded (K=32 keeps the whole episode).
                    if cmd_history is not None and len(cmd_history) > max_pairs:
                        del cmd_history[:len(cmd_history) - max_pairs]
                    prev_state = state   # next round's delta diffs against this turn's state
                    if not parsed_ok:
                        # one unparseable response -> no-op this round, keep playing
                        rec["parse_failures"] = rec.get("parse_failures", 0) + 1
                    if state_only and dec.get("errors"):
                        rec["cmd_errors"] = rec.get("cmd_errors", 0) + len(dec["errors"])
                except Exception as e:
                    # hard API/network error: end the episode
                    rec["error"] = f"LLM error r{rnd}: {e}"
                    break
            # task choices
            nsel = 0
            for c in dec.get("choices", []):
                try:
                    if env.select_task_choice(int(c["taskId"]), int(c["choiceId"])):
                        nsel += 1
                except Exception:
                    pass
            # actions: count requested / invalid; tally the per-turn category mix
            # (the model's intended strategy: game-action types + task-choice by task type)
            req = [int(a) for a in dec.get("actions", []) if str(a).lstrip("-").isdigit()]
            actions_requested += len(req)
            invalid_idx += sum(1 for a in req if a < 0 or a >= n_valid)
            act_cats = {}
            tmap = {t["taskId"]: t.get("type", "?") for t in state.get("tasks", [])}
            for c in dec.get("choices", []):
                try:
                    cat = "choice:" + str(tmap.get(int(c["taskId"]), "?"))
                except Exception:
                    cat = "choice:?"
                act_cats[cat] = act_cats.get(cat, 0) + 1
            for a in req:
                if 0 <= a < n_valid:
                    at = acts_enum[a].get("action_type") or "?"
                    act_cats[at] = act_cats.get(at, 0) + 1
                    if at == "construction": built = True
                    if at == "worker": hired = True
            obs, reward, term, trunc, info = env.step(",".join(str(a) for a in req))
            total += reward
            exres = info.get("execution_results") or []
            actions_executed += sum(1 for r in exres if r.get("success"))
            action_failures += sum(1 for r in exres if not r.get("success"))
            min_budget = min(min_budget, info.get("budget", 0.0))
            rm = info.get("reward_metrics") or {}
            rec["rounds"].append({
                "r": rnd, "reward": round(reward, 4), "sumR": round(total, 4),
                "sat": info["satisfaction"], "budget": info["budget"],
                "satScore": round(info["satisfaction_score"], 4),
                "costEff": round(info["cost_efficiency"], 4),
                # full reward breakdown (cumulative-to-date) for per-component graphing
                "comps": {k: round(v, 4) for k, v in (info.get("score_components") or {}).items()},
                "foodFul": rm.get("foodFulfilled"), "foodRes": rm.get("foodResolved"),
                "lodgFul": rm.get("lodgingFulfilled"), "lodgRes": rm.get("lodgingResolved"),
                "nSel": nsel, "nReq": len(req), "nFail": sum(1 for r in exres if not r.get("success")),
                "actCats": act_cats,   # {category: count attempted this turn} — strategy mix
                # cmd-format diagnostics: the parser's per-command rejection strings this round
                # (empty for idx). Lets us categorize WHY cmd commands fail (bad format / unknown
                # building / unaffordable / no available site / nonexistent choice).
                "cmdErrors": list(dec.get("errors", [])) if state_only else [],

                "note": (dec.get("note") or "")[:80],
                "reasoning": (dec.get("reasoning") or "")[:1500],   # model's own rationale (JSON field)
                "reasoningTokens": rtok,   # hidden-thinking tokens spent this round (None if N/A)
                # Finetuning-complete (prompt -> completion): the FULL observation the
                # model saw and its FULL untruncated response. obs is the user-message
                # content (state). parsed_ok flags whether the response was valid JSON.
                "obs": state,
                "raw": raw or "",
                "reasoningTrace": rtrace or None,
                "parsed_ok": parsed_ok,
            })
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
            # Scores are comparable across wings (live/RL/benchmark all share
            # reward_scoring.compute_score_components) but only under the SAME weights.
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


_WB_COMP = ["sat_food", "sat_lodging", "sat_worker_use", "cost_food", "cost_lodging", "cost_worker"]


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
                   "step/game/cost_efficiency": avg([rd.get("costEff") for rd in at]),
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
    ap.add_argument("--policy", choices=["llm", "greedy", "build-potential", "choice-lookahead",
                                        "combined", "random", "noop",
                                        "rules-based", "rules-based-v2"], default="llm",
                    help="llm = benchmark the --models. Non-learning baselines (no API): greedy; "
                         "build-potential (adds infrastructure building); choice-lookahead (picks "
                         "task choices by long-term value); combined (both); random; noop. "
                         "rules-based / rules-based-v2 are deprecated aliases for build-potential "
                         "/ choice-lookahead — the old names implied a version ordering that does "
                         "not exist: they are different algorithms improving opposite halves.")
    ap.add_argument("--action_format", choices=["idx", "cmd", "tools"], default="idx",
                    help="LLM action interface: idx = enumerated action menu + JSON index list (default); "
                         "cmd = state-only obs + command tags (<build>/<hire>/<staff>/<task>/...). "
                         "Switches both the system prompt and the response parser. Ignored for non-llm policies.")
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
    ap.add_argument("--reasoning_effort", choices=["none", "low", "medium", "high"], default="low",
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
    ap.add_argument("--system_prompt", choices=["original", "minimal", "minimal_v2"], default="original",
                    help="system-prompt ablation: original (default) = strategy-laden prompt; "
                         "minimal = PIMMUR minimal-control prompt (mechanics + objective only, no "
                         "strategy hints); minimal_v2 = minimal + the prompt/encoding fix layer "
                         "(Passive-fixtures note, sharpened build-then-staff rule, un-truncated choice "
                         "text, transfers/affects cleanup) for A/B vs minimal. "
                         "Logged per episode (system_variant + prompt_sha). LLM-only.")
    ap.add_argument("--prompt-pack", dest="prompt_pack", default=None,
                    help="load the director system prompt from a declarative JSON pack in prompts/ "
                         "(bare name like 'cmd_minimal' or a path to a .json). Overrides "
                         "--action_format and --system_prompt from the pack's format/variant, so a "
                         "low-code collaborator can A/B a prompt by editing JSON — no Python. "
                         "prompt_sha + the pack name are logged per episode. See prompts/README.md.")
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

    # Opt-in declarative prompt pack: load once, and let the pack drive format/variant so the
    # per-episode records stay attributable. Absent --prompt-pack, the built-in path is unchanged.
    args._loaded_pack = None
    if args.prompt_pack:
        args._loaded_pack = prompt_packs.load_pack(args.prompt_pack)
        args.action_format = args._loaded_pack["format"]
        args.system_prompt = args._loaded_pack.get("variant", args.system_prompt)
        print(f"    prompt-pack:   {args._loaded_pack['name']}  "
              f"(format={args.action_format}, variant={args.system_prompt}, "
              f"from {args._loaded_pack['_path']})")

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
        base_url = args.base_url or smoke.GATEWAY_BASE
        api_key = args.api_key or (smoke.load_env_key() if args.base_url is None else "local")
        client = openai.OpenAI(api_key=api_key, base_url=base_url)
        if args.base_url:
            # Local OpenAI-compat server (Ollama): forward --reasoning_effort so chat() can cap or
            # disable thinking on reasoning models (Ollama auto-enables it otherwise). Not set for
            # the CMU gateway, where Claude rejects the knob and gpt-5*/gemini handle it in-branch.
            _set_local_reasoning_effort(args.reasoning_effort)
            _set_local_max_tokens(args.max_tokens)
            print(f"    endpoint:      {base_url} (local/override)")
            print(f"    local thinking: reasoning_effort={args.reasoning_effort}"
                  f"{' (thinking OFF)' if args.reasoning_effort == 'none' else ''}"
                  f"{f', max_tokens={args.max_tokens}' if args.max_tokens else ''}")
    outdir = Path(args.out); outdir.mkdir(parents=True, exist_ok=True)
    jsonl = outdir / "episodes.jsonl"

    jobs = [(m, e) for m in models for e in range(args.episodes)]
    print(f"=== Benchmark: {len(models)} models x {args.episodes} eps x {args.rounds} rounds "
          f"= {len(jobs)} episodes, {args.workers} worker(s) ===")
    print(f"    models: {models}")
    print(f"    observation choice-impacts: {'SHOWN' if args.impacts else 'HIDDEN (ablation)'}")
    if args.policy == "llm":
        print(f"    action format: {args.action_format} "
              f"({'state-only obs + command tags' if args.action_format == 'cmd' else 'enumerated menu + JSON indices'})")
        print(f"    image mode:    {args.image_mode}"
              f"{' (graphics render build)' if need_render else ''}")
        print(f"    reasoning:     effort={args.reasoning_effort} "
              f"(budget {_EFFORT_BUDGET[args.reasoning_effort]} tok; reasoning_tokens logged/round)")
        print(f"    transfers:     {args.transfers} "
              f"({'standalone food/people transfers exposed to the LLM' if args.transfers == 'manual' else 'human-faithful — transfers only via task choices'})")
        print(f"    system prompt: {args.system_prompt} "
              f"({'strategy-laden (original)' if args.system_prompt == 'original' else 'PIMMUR minimal-control (mechanics + objective only)'})")
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
                           args.action_format, args.image_mode, args.reasoning_effort,
                           args.transfers == "manual", args.system_prompt,
                           args.temperature, args.obs_encoding, args.history,
                           args._loaded_pack)] = (model, ep)
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
        # Encode the experiment cell (image_mode x action_format) into the WandB
        # condition so each of the 6 cells is its own run group; append the ablation flag.
        cond = (f"img-{args.image_mode}_{args.action_format}"
                + ("" if args.impacts else "_noimpacts")
                + ("" if args.reasoning_effort == "low" else f"_eff-{args.reasoning_effort}")
                + ("" if args.transfers == "manual" else "_xfer-task_only")
                + ("" if args.system_prompt == "original" else f"_sys-{args.system_prompt}")
                + ("" if args.obs_encoding == "json" else f"_obs-{args.obs_encoding}")
                + ("" if args.history == 1 else f"_k{args.history}")
                + ("" if args.temperature is None else f"_temp-{args.temperature}"))
        log_wandb(records, args.wandb_project, cond, args.episodes, args.rounds)


if __name__ == "__main__":
    main()
