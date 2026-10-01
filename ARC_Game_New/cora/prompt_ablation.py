"""Rule ablation for prompt studies: remove or reword individual rules of a rendered prompt.

The rule table (prompts/ablation/rules.json) maps rule ids (R01..R34) to byte-exact passages of
the rendered minimal_v6 prompt; paraphrase arms (prompts/ablation/paraphrases.json) reword one
rule while holding the rest of the prompt constant. A spec selects what to apply:

    "R07"                 remove rule R07
    "R07,R09,R10"         remove several rules (Plackett-Burman / cumulative strip-K designs)
    "R07_P1_direct"       replace rule R07 with that paraphrase arm
    "" or "NONE"          the full prompt

A passage is matched in the HOW-TO-ACT section first and otherwise anywhere in the prompt, and only
its first occurrence is changed. Removing a passage collapses the blank lines it leaves. An
unknown rule, or a rule whose text is not in the prompt, is an error: a silently unchanged
"ablated" prompt would quietly be the baseline. (Some rules exist in one rendering only: R33 is
the hermes tool header, R10 the execution-order sentence without transfers.)
"""
from __future__ import annotations

import json
import os
from functools import lru_cache

from cora.prompts import PACKS_DIR

_HOW_ANCHOR = "HOW TO ACT"


@lru_cache(maxsize=None)
def _tables():
    d = os.path.join(PACKS_DIR, "ablation")
    with open(os.path.join(d, "rules.json"), encoding="utf-8") as f:
        rules = json.load(f)["rules"]
    with open(os.path.join(d, "paraphrases.json"), encoding="utf-8") as f:
        arms = json.load(f)["arms"]
    return rules, arms


def rule_ids() -> list:
    return sorted(_tables()[0])


def paraphrase_arms() -> list:
    return sorted(_tables()[1])


def _replace_first(text: str, rule: str, old: str, new: str) -> str:
    how = text.find(_HOW_ANCHOR)
    at = text.find(old, how) if how >= 0 else -1
    if at < 0:
        at = text.find(old)
    if at < 0:
        raise ValueError(f"rule {rule}'s passage is not in this prompt (some rules belong to one wire "
                         f"format or transfer mode only): {old[:60]!r}")
    out = text[:at] + new + text[at + len(old):]
    if not new:
        while "\n\n\n" in out:
            out = out.replace("\n\n\n", "\n\n")
    return out


def ablate(text: str, spec: str) -> str:
    spec = (spec or "").strip()
    if not spec or spec.upper() == "NONE":
        return text
    rules, arms = _tables()
    if spec in arms:
        arm = arms[spec]
        return _replace_first(text, arm["rule"], rules[arm["rule"]], arm["text"])
    ids = [s.strip().upper() for s in spec.split(",") if s.strip()]
    unknown = [r for r in ids if r not in rules]
    if unknown:
        raise ValueError(f"unknown ablation rule(s) {unknown}; rules: {rule_ids()}; "
                         f"paraphrase arms: {paraphrase_arms()}")
    for rid in ids:
        text = _replace_first(text, rid, rules[rid], "")
    return text
