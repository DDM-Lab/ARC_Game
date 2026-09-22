"""Observation adapters — gym env → obs_encoder, carrying the benchmark A/B toggles.

`obs_encoder` is the PURE renderer: (game_state, valid_actions) -> text, deliberately
decoupled from the gym env. These thin adapters are the coupling layer: they (a) unpack
a gym `env` into `(env.game_state, env.get_valid_actions())` and (b) thread the two
benchmark A/B flags (`_NEW` prompt/obs version, `_V2` minimal-v2 fix layer) into the
encoder calls — so the benchmark harness and the Verlog RL wrapper share ONE observation
surface without recoupling obs_encoder to the env.

Moved out of `llm_smoke_test.py` (now just the playthrough script), which re-exports these
names for back-compat. Output is identical to the pre-move wrappers (same obs_encoder calls,
same flags).
"""
import os

import obs_encoder

# ── Prompt/obs version toggle (A/B) ─────────────────────────────────────────
# ARC_PROMPT_VERSION=old -> the original mechanics-only prompt + lean obs. =new (default) ->
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
