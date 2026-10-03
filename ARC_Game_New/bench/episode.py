"""One benchmark episode: a fresh Unity process, one policy decision per decision point, and a
self-contained record (settings, scenario, prompt, every call and its outcome, score breakdown)."""
from __future__ import annotations

import json
import os
import traceback
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from bench.baselines import POLICIES
from bench.baselines.common import tool_calls
from bench.images import MAP_GRID_JSON, decision_image
from bench.llm import ANTHROPIC_TEMP_MAX, LocalOptions, ask_tools, is_anthropic
from rl.cora_env import CoraEnv, CoraEnvConfig
from cora import prompts as cora_prompts
from cora.env.game import DECISIONS
from cora.scoring import REWARD_WEIGHTS


@dataclass(frozen=True)
class RunConfig:
    """Everything an episode's settings depend on (one per benchmark invocation)."""
    policy: str = "llm"                 # "llm", "noop" or a baseline in bench.baselines.POLICIES
    rounds: int = 40                    # decision cap; a full game is 29 decisions and ends itself
    prompt: str = cora_prompts.DEFAULT_PACK
    ablation: str = ""
    show_impacts: bool = True
    manual_transfers: bool = False      # False = the human GUI's rule (transfers only via tasks)
    obs_encoding: str = "compact"       # json | compact | delta
    history: int = 1                    # turns the model sees, including the current one
    image_mode: str = "none"            # none | synthetic | real
    temperature: Optional[float] = None
    local: Optional[LocalOptions] = None
    base_seed: Optional[int] = None     # episode i plays seed base_seed + i
    map_config: Optional[str] = None
    log_dir: Optional[str] = None


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


def run_episode(model, ep_idx, cfg: RunConfig, client, port_pool):
    """Fresh Unity process -> play up to cfg.rounds decisions -> one episode record.

    `model` is the LLM (policy "llm") or the baseline's name. A port is leased from port_pool for
    the episode's lifetime, so concurrent episodes never collide."""
    port = port_pool.get()
    rounds, policy, image_mode = cfg.rounds, cfg.policy, cfg.image_mode
    show_impacts, manual_transfers, obs_encoding, history = (cfg.show_impacts, cfg.manual_transfers,
                                                             cfg.obs_encoding, cfg.history)
    temperature, ablation, base_seed = cfg.temperature, cfg.ablation, cfg.base_seed
    # Anthropic caps temperature at 1.0; clamp per-model so a shared sweep invocation (e.g. temp=1.5
    # for gemini) doesn't 400 Claude. eff_temp is what's actually sent + logged; temperature is the
    # requested experimental level.
    eff_temp = temperature
    if temperature is not None and is_anthropic(model):
        eff_temp = min(temperature, ANTHROPIC_TEMP_MAX)
    ulog = None
    if cfg.log_dir:
        safe = model.replace("/", "_").replace(":", "_")
        ulog = str((Path(cfg.log_dir) / f"unity_{safe}_ep{ep_idx}_{image_mode}_tools.log").resolve())
    use_image = (image_mode in ("synthetic", "real") and policy == "llm")
    real_img = (image_mode == "real" and policy == "llm")
    state_only = (policy == "llm")
    # The game and the turn contract (prompt, observation, tools, executor) come from CoraEnv, the
    # same environment RL trains in. base_seed + ep_idx: reproducible across runs, distinct within
    # a run, so every prompt variant run with one base_seed plays the SAME scenarios (paired).
    # Real-image arms capture the live frame at decision time (render build, step capture).
    cenv = CoraEnv(CoraEnvConfig(
        prompt=cfg.prompt, ablation=ablation, image_mode=image_mode if use_image else "none",
        manual_transfers=manual_transfers, show_impacts=show_impacts, obs_encoding=obs_encoding,
        history=history if state_only else 1, max_steps=rounds + 5,
        seed=(int(base_seed) + int(ep_idx)) if base_seed is not None else None,
        map_config=cfg.map_config, port=port, unity_log=ulog,
        frame_capture="step" if real_img else "off",
        frame_dir=os.environ.get("ARC_FRAME_DIR", "render_frames_bench") if real_img else None))
    # Static tile lattice for synthetic rendering (loaded once per episode).
    grid = MAP_GRID_JSON if (use_image and image_mode == "synthetic") else None
    tmp_png = None
    if use_image and image_mode == "synthetic":
        tmp_png = str((Path(cfg.log_dir or ".") / f".synth_{model.replace('/','_')}_ep{ep_idx}_tools.png").resolve())
    _sys_text = cenv.system_prompt       # non-LLM policies only record it, for provenance
    rec = {
        # Recorded so the analysis can pair episode i of one variant against episode i
        # of another; None when the run was unseeded.
        "seed": (int(base_seed) + int(ep_idx)) if base_seed is not None else None,
        # 2: every policy acts through tool calls (baselines' menu picks are converted), so baseline
        # records before this version are not comparable with later ones.
        "record_version": 2,
        "model": model, "policy": policy, "episode": ep_idx, "rounds": [], "error": None,
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
           "system_variant": cenv.pack_name,
           "prompt_ablation": ablation or None,
           "prompt_sha": cenv.prompt_sha,
           "reasoning_effort": cfg.local.reasoning_effort if cfg.local else None,
           # Total-generation cap in force on a local server (None = hosted API default). Worth
           # stamping: a local model that reasons in the CONTENT channel (qwen3:4b does) is cut
           # off mid-prose by a low cap and emits no tool call, so the cap decides the result.
           "max_tokens": cfg.local.max_tokens if cfg.local else None,
           "temperature": temperature,           # requested experimental level
           "temperature_sent": eff_temp,         # actually sent (Anthropic clamped to <=1.0)
           "system_prompt": _sys_text}
    try:
        user_text, _ = cenv.reset()
        env = cenv.game
        # Which scenario this episode actually ran: map fingerprint/source, parameter source and
        # seed as the GAME reports them (not as requested), so runs on different maps or sheets
        # are never pooled by accident.
        rec["scenario"] = (env.game_state or {}).get("scenario")
        total = 0.0
        actions_requested = actions_executed = action_failures = invalid_calls = 0
        min_budget = float("inf")
        built = hired = False
        # Append-only context buffer for history-carrying play (K>1): holds prior (state, action)
        # messages that ask_tools prepends to each call. None => stateless K=1 (the default).
        # max_pairs caps it to the K-1 most-recent prior turns (ask_tools adds the current turn).
        cmd_history = [] if (state_only and history and history > 1) else None
        max_pairs = 2 * (history - 1) if (history and history > 1) else 0
        for rnd in range(rounds):
            state = cenv.observation
            raw = rtrace = None; rtok = None; parsed_ok = None
            if policy == "noop":
                dec = {"tool_calls": []}
            elif policy in POLICIES:
                # A baseline picks from the menu; it acts through the same tool calls as a model.
                # It plans against the game's length, not the decision cap.
                dec = POLICIES[policy](env, rnd, DECISIONS); raw = json.dumps(dec)
                dec["tool_calls"] = tool_calls(env, dec)
            else:                                               # llm
                img_b64 = decision_image(image_mode, env, grid, tmp_png) if use_image else None
                if use_image:
                    rec["images_attached" if img_b64 else "images_missing"] = \
                        rec.get("images_attached" if img_b64 else "images_missing", 0) + 1
                try:
                    dec, raw, rtrace, rtok, parsed_ok = ask_tools(
                        client, model, cenv.system_prompt, cenv.tools, user_text, img_b64, eff_temp,
                        cmd_history, cfg.local)
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
                except Exception as e:
                    # hard API/network error: end the episode
                    rec["error"] = f"LLM error r{rnd}: {e}"
                    break
            # ── execute: every policy's tool calls go through the one executor ──
            user_text, reward, term, trunc, info = cenv.step(dec["tool_calls"])
            call_results = info["call_results"]
            parsed_ok = not info["malformed"]
            if not parsed_ok:
                rec["parse_failures"] = rec.get("parse_failures", 0) + 1
            total += reward
            exres = info.get("execution_results") or []
            actions_requested += sum(len(cr.action_indices) for cr in call_results)
            actions_executed += sum(1 for r in exres if r.get("success"))
            action_failures += sum(1 for r in exres if not r.get("success"))
            invalid_calls += sum(1 for cr in call_results if cr.status == "invalid")
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
            "actionFailures": action_failures, "invalidCalls": invalid_calls,
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
        cenv.close()
        port_pool.put(port)
    return rec
