"""Reliability + explainability layer for the choices agent.

Pure, side-effect-free helpers the router calls to make choice proposals
ROBUST (the director never receives an empty / malformed / duplicate set of
packages) and EXPLAINABLE (each package carries an engine-computed cost, and the
pre-choices summary is grounded), without inventing numbers.

Everything here is opt-in via AgentConfig flags (see agent_config.py):
  * choices_max_retries  – extra LLM re-queries if a parse underdelivers
  * choices_min_packages – floor below which we retry / fall back
  * choices_fallback     – synthesize deterministic packages to fill the set
  * explain_grounded     – prepend summed $cost to each package description
  * explain_summary      – prepend grounded context to the pre-choices summary

The functions take plain dicts (packages, actions, game_state) so they are unit
testable with no router / Unity / network dependency.
"""
from __future__ import annotations

import re
from typing import List, Optional

from obs_encoder import build_observation, _num

# Compact caps. The Unity choice card auto-grows to fit (within a max height),
# so the per-package outcome can be a full phrase; these are just backstops
# against a runaway model. Truncation is always on a WORD boundary with an
# ellipsis (see _trim) — never mid-word.
_MAX_OUTCOME_CHARS = 90
_MAX_RATIONALE_CHARS = 180
_MAX_SUMMARY_CHARS = 420
_FALLBACK_CONFIDENCE = 0.5

# Leading money token the model sometimes prefixes to its own outcome text
# (e.g. "$2,100; adds shelter…"). We strip it before prepending the
# authoritative engine-summed cost, so the card shows one grounded number
# instead of "$2,100 · $2,100" (or a contradictory model-claimed figure).
_LEADING_MONEY_RE = re.compile(r"^\$[\d,]+(?:\s*[·;:,\-–—]\s*|\s+)")


def _money(v) -> str:
    return f"${_num(v):,}"


def _trim(text: str, limit: int) -> str:
    """Truncate to <= limit chars WITHOUT splitting a word. If truncated, end
    with an ellipsis. Trailing punctuation before the ellipsis is dropped."""
    text = (text or "").strip()
    if len(text) <= limit:
        return text
    # Reserve one char for the ellipsis, cut back to the last space.
    cut = text[: max(0, limit - 1)]
    sp = cut.rfind(" ")
    if sp > 0:
        cut = cut[:sp]
    return cut.rstrip(" ,;.:-–—") + "…"


def _strip_leading_money(text: str) -> str:
    """Drop a leading '$1,234 · ' / '$1,234; ' style money token the model may
    have put at the front of its own outcome phrase."""
    return _LEADING_MONEY_RE.sub("", (text or "").lstrip()).lstrip()


def _strip_money_clauses(text: str) -> str:
    """Remove any clause that states a $ figure. The model's budget arithmetic is
    unreliable (it routinely mis-computes 'remaining'), so we drop those clauses and
    let the caller append a single ENGINE-grounded 'leaves $X' instead. Clauses are
    split on ';', '·' and ',' — a fragment containing '$' is dropped, the rest kept."""
    text = (text or "").strip()
    if not text:
        return ""
    # Shield commas INSIDE numbers ("$7,400") so we don't split them into fragments.
    protected = re.sub(r"(?<=\d),(?=\d)", "\x00", text)
    parts = re.split(r"\s*[;·,]\s*", protected)
    kept = [p.replace("\x00", ",").strip() for p in parts if p.strip() and "$" not in p]
    joined = "; ".join(kept)
    # Backstop: strip any residual $-token that survived inside a kept clause.
    joined = re.sub(r"\s*\$[\d,]+", "", joined)
    return re.sub(r"\s{2,}", " ", joined).strip(" ,;·-")


def _jaccard(a: List[int], b: List[int]) -> float:
    sa, sb = set(a), set(b)
    if not sa and not sb:
        return 1.0
    if not sa or not sb:
        return 0.0
    return len(sa & sb) / len(sa | sb)


def _action_type_set(action_indices: List[int], actions: List[dict]) -> frozenset:
    return frozenset(
        actions[i].get("action_type") for i in action_indices
        if 0 <= i < len(actions)
    )


def enforce_diversity(packages: List[dict], actions: List[dict],
                      jaccard_threshold: float = 0.7) -> List[dict]:
    """Drop packages that offer the SAME strategy as one already kept, so the human
    sees genuinely different bets (not one plan at three spend levels). Two packages
    are 'the same' if they touch the same set of action TYPES (e.g. both build+hire+
    transfer) or share >= `jaccard_threshold` of their exact actions. Whatever this
    drops is later back-filled by build_fallback_packages with distinct archetypes
    (address-needs / cheapest-progress / hold). Keeps the first occurrence."""
    kept: List[dict] = []
    for p in packages:
        idx = p.get("action_indices") or []
        tset = _action_type_set(idx, actions)
        dup = False
        for k in kept:
            kidx = k.get("action_indices") or []
            if tset and tset == _action_type_set(kidx, actions):
                dup = True
                break
            if _jaccard(idx, kidx) >= jaccard_threshold:
                dup = True
                break
        if not dup:
            kept.append(p)
    return kept


def summed_cost(action_indices: List[int], actions: List[dict]) -> int:
    """Grounded total cost of a package = sum of its actions' engine-listed costs."""
    total = 0
    for i in action_indices:
        if 0 <= i < len(actions):
            total += _num(actions[i].get("cost"))
    return total


def dedupe_packages(packages: List[dict]) -> List[dict]:
    """Drop packages whose action_indices set duplicates an earlier one (order-insensitive).
    Keeps the first occurrence. An empty package is its own distinct 'hold' option."""
    seen = set()
    out = []
    for p in packages:
        key = frozenset(p.get("action_indices") or [])
        if key in seen:
            continue
        seen.add(key)
        out.append(p)
    return out


def _compact_action(desc: str) -> str:
    """Shorten one engine action description for a compact, grounded card summary.

    Drops cost/qualifier parentheticals ("($100 each)", "(s)"), a trailing site
    clause ("... at AbandonedSite_12"), and a transfer origin ("from X ... to Y"
    -> "to Y") — keeping the authoritative verb + counts the engine reported."""
    d = (desc or "").strip()
    if not d:
        return ""
    d = re.sub(r"\s*\([^)]*\)", "", d)          # "($100 each)", "worker(s)" -> ""
    d = re.sub(r"\s+from\s+.+?\s+to\s+", " to ", d)  # "from Charleston to Motel" -> "to Motel"
    d = re.sub(r"\s+at\s+\S+\s*$", "", d)        # trailing "... at AbandonedSite_12"
    return re.sub(r"\s+", " ", d).strip()


def grounded_action_summary(action_indices: List[int], actions: List[dict],
                            limit: int = _MAX_OUTCOME_CHARS) -> str:
    """Compact, grounded one-line 'what it does' built from the engine's OWN action
    descriptions (authoritative counts / sites) — never the model's free text, so it
    cannot carry hallucinated numbers. Joined by ' · ' and word-trimmed to `limit`."""
    parts = []
    for i in action_indices:
        if 0 <= i < len(actions):
            a = actions[i]
            c = _compact_action(a.get("description") or a.get("action_id") or "")
            if c:
                parts.append(c)
    return _trim(" · ".join(parts), limit)


def _grounded_why(model_rationale: str, cost: int, budget: Optional[int]) -> str:
    """Build the 'Why' text: the model's QUALITATIVE trade-off (with any $-clauses
    stripped — its budget math is unreliable) plus a single ENGINE-grounded
    'leaves $X' clause (budget - cost). Either half may be absent."""
    qual = _trim(_strip_money_clauses(model_rationale), _MAX_RATIONALE_CHARS - 20)
    tail = ""
    if budget is not None:
        tail = f"leaves {_money(_num(budget) - cost)}"
    return "; ".join(x for x in (qual, tail) if x)


def apply_grounded_explanations(packages: List[dict], actions: List[dict],
                                game_state: Optional[dict] = None) -> List[dict]:
    """Rewrite each package description to a compact, grounded block:
        "$2,400 · <engine action summary>
         Why: <qualitative model trade-off>; leaves $5,600"
    Both the $cost and the action summary are engine-computed (never LLM-invented),
    and the 'leaves $X' remaining-budget figure is engine-computed too — so the card
    can't show a number the model hallucinated. The model supplies only the label and
    the qualitative 'why'. Pass game_state to enable the grounded 'leaves $X' clause.
    Mutates and returns the same list."""
    budget = None
    if game_state is not None:
        budget = build_observation(game_state).get("budget")

    for p in packages:
        idx = p.get("action_indices") or []
        cost = summed_cost(idx, actions)
        summary = grounded_action_summary(idx, actions)
        head = f"{_money(cost)} · {summary}" if summary else _money(cost)

        # The model's per-package rationale ("why choose this") — the explainable
        # trade-off — with its unreliable $ figures replaced by a grounded one.
        why = _grounded_why(p.get("rationale", ""), cost, budget)
        if why:
            p["description"] = f"{head}\nWhy: {why}"
        else:
            p["description"] = head
        p["cost"] = cost          # structured, for logging / autonomous director
        p["rationale"] = why       # structured, normalized (grounded)
    return packages


def compose_summary(reasoning: str, packages: List[dict], actions: List[dict],
                    game_state: dict) -> str:
    """Prepend a compact grounded context clause to the agent's reasoning, e.g.
        "Day 3/8 · budget $4,200 · options $0–$2,400. <reasoning>"
    so the pre-choices summary states real numbers before the model's rationale."""
    obs = build_observation(game_state)
    bits = []
    day = obs.get("day")
    if day is not None:
        # finalDay lives on the raw payload's sessionInfo, not on the canonical obs
        # (the shared benchmark encoder emits only `day`=currentDay, no horizon key).
        final_day = (game_state or {}).get("sessionInfo", {}).get("finalDay")
        bits.append(f"Day {day}/{final_day}" if final_day is not None else f"Day {day}")
    if obs.get("budget") is not None:
        bits.append(f"budget {_money(obs['budget'])}")
    costs = [summed_cost(p.get("action_indices") or [], actions) for p in packages]
    if costs:
        lo, hi = min(costs), max(costs)
        bits.append(f"options {_money(lo)}–{_money(hi)}" if lo != hi else f"cost {_money(lo)}")
    ctx = " · ".join(bits)
    reasoning = (reasoning or "").strip()
    if ctx and reasoning:
        return _trim(f"{ctx}. {reasoning}", _MAX_SUMMARY_CHARS)
    return _trim(ctx or reasoning, _MAX_SUMMARY_CHARS)


REPROPOSE_HINT = ("You can also talk to me and ask me to repropose choices or clarify.")


def append_repropose_hint(reasoning: str) -> str:
    """Append a one-line discoverability nudge telling the director they can chat
    to request a fresh set of choices. Idempotent: won't double-append if the hint
    is already present (e.g. carried over across a reproposal)."""
    reasoning = (reasoning or "").strip()
    if REPROPOSE_HINT in reasoning:
        return reasoning
    return f"{reasoning}\n\n{REPROPOSE_HINT}" if reasoning else REPROPOSE_HINT


def _urgent_task_action_indices(actions: List[dict], max_n: int) -> List[int]:
    """Indices of task-resolving actions (select_task_choice), most useful first.
    These directly address open tasks, so they make a sensible 'address needs' package."""
    idx = [i for i, a in enumerate(actions) if a.get("action_type") == "select_task_choice"]
    return idx[:max_n]


def _cheapest_progress_indices(actions: List[dict], budget: Optional[int], max_n: int) -> List[int]:
    """Cheapest affordable, non-trivial actions (staffing / building / hiring) — a
    low-risk 'make progress within budget' bundle. Skips $0 no-ops so it differs
    from the hold package, and respects budget if known."""
    cand = []
    for i, a in enumerate(actions):
        if a.get("action_type") == "select_task_choice":
            continue
        c = _num(a.get("cost"))
        cand.append((c, i))
    cand.sort(key=lambda ci: ci[0])
    picked, spent = [], 0
    for c, i in cand:
        if budget is not None and spent + c > _num(budget):
            continue
        picked.append(i)
        spent += c
        if len(picked) >= max_n:
            break
    return picked


def build_fallback_packages(existing: List[dict], actions: List[dict], game_state: dict,
                            num_choices: int, max_per_package: int) -> List[dict]:
    """Deterministically synthesize sensible packages to fill up to num_choices.

    Guarantees the director always gets a non-empty, valid, de-duplicated set even
    when the LLM underdelivers. Candidate strategies, in order of preference:
      1. Address open tasks   – select_task_choice actions (if any)
      2. Cheapest progress    – lowest-cost affordable staffing/building/hiring
      3. Hold / save budget   – empty package ($0), always valid
    Only packages with action_index sets not already present are added.
    """
    obs = build_observation(game_state)
    budget = obs.get("budget")
    seen = {frozenset(p.get("action_indices") or []) for p in existing}
    out = list(existing)

    candidates = []
    turg = _urgent_task_action_indices(actions, max_per_package)
    if turg:
        candidates.append(("Address Needs", "resolve open tasks",
                           "tackles the most pressing open tasks first"))
    cheap = _cheapest_progress_indices(actions, budget, max_per_package)
    if cheap:
        candidates.append(("Cheapest Progress", "low-cost progress",
                           "makes progress while spending the least"))
    candidates.append(("Hold / Save Budget", "keep reserve",
                       "spends nothing; preserves budget for later needs"))  # always valid

    cand_idx = {"Address Needs": turg, "Cheapest Progress": cheap,
                "Hold / Save Budget": []}
    for label, outcome, rationale in candidates:
        if len(out) >= num_choices:
            break
        idx = cand_idx[label]
        key = frozenset(idx)
        if key in seen:
            continue
        seen.add(key)
        cost = summed_cost(idx, actions)
        # Prefer a grounded engine-action summary; fall back to the hand-written
        # phrase when the package has no actions (e.g. Hold / Save Budget).
        summary = grounded_action_summary(idx, actions) or outcome
        why = _grounded_why(rationale, cost, budget)
        out.append({
            "package_index": len(out),
            "label": label,
            "description": f"{_money(cost)} · {summary}\nWhy: {why}",
            "rationale": why,
            "confidence": _FALLBACK_CONFIDENCE,
            "action_indices": idx,
            "cost": cost,
            "fallback": True,
        })

    # Reindex package_index to be contiguous after merging.
    for n, p in enumerate(out):
        p["package_index"] = n
    return out
