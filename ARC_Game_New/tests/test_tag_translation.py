#!/usr/bin/env python3
"""Hermetic tests for the typed-tool -> cmd-tag bridge (cora_tools.tag_for / translate_tool_calls).

Regression guard for the delimiter-corruption class: the tag body is comma-joined and the tag
is angle-bracket delimited, neither escaped, so an argument value carrying one of those
characters used to change the call's ARITY silently. cmd_parser's `split(body, n)` accepts
`len(parts) >= n`, so `staff(site="Kitchen, 0", count=2)` -> `<staff>Kitchen, 0,2</staff>` was
read as site="Kitchen", count=0 -- a wrong action reported as {valid: 1, bad_args: 0}.

Five params are free-text (staff.site, deconstruct.site, transfer.source/dest, task.task_id),
so this is reachable from ordinary model output, and it corrupts the training corpus rather
than just the turn. No network, no Unity, no LLM.

Run: ./.venv/bin/python test_tag_translation.py
"""
from __future__ import annotations

import sys

import cora_tools as ct

FAILS: list[str] = []


def check(label: str, cond: bool, detail: str = "") -> None:
    print(f"  {'PASS' if cond else 'FAIL'}  {label}" + (f"  -- {detail}" if detail and not cond else ""))
    if not cond:
        FAILS.append(label)


def test_wellformed_roundtrip() -> None:
    """Every tool renders its canonical tag, and nothing is flagged."""
    print("\n[1] well-formed calls still translate")
    cases = [
        (("build", {"type": "kitchen", "site_id": 3}), "<build>kitchen,3</build>"),
        (("hire", {"kind": "trained", "count": 2}), "<hire>trained,2</hire>"),
        (("train", {"count": 4}), "<train>4</train>"),
        (("staff", {"site": "Kitchen_0", "count": 2}), "<staff>Kitchen_0,2</staff>"),
        (("deconstruct", {"site": "Motel"}), "<deconstruct>Motel</deconstruct>"),
        (("task", {"task_id": "BUDGET_DAILY", "choice_id": 1}), "<task>BUDGET_DAILY,1</task>"),
        (("transfer", {"resource": "food", "source": "Kitchen_0", "dest": "Shelter_1", "qty": 50}),
         "<transfer>food,Kitchen_0,Shelter_1,50</transfer>"),
    ]
    for call, expected in cases:
        tag, meta = ct.translate_tool_calls([call])
        check(f"{call[0]} -> {expected}",
              tag == expected and meta["valid"] == 1 and meta["bad_args"] == 0,
              f"got {tag!r} meta={meta}")


def test_comma_rejected() -> None:
    """A comma in a free-text arg is REJECTED, not silently re-arity'd."""
    print("\n[2] comma in a free-text arg -> bad_args (the original bug)")
    for call in [
        ("staff", {"site": "Kitchen, 0", "count": 2}),
        ("deconstruct", {"site": "Motel, Community01"}),
        ("transfer", {"resource": "food", "source": "K_0, K_1", "dest": "S_1", "qty": 5}),
        ("task", {"task_id": "BUDGET_DAILY, FOOD_C01", "choice_id": 0}),
    ]:
        tag, meta = ct.translate_tool_calls([call])
        check(f"{call[0]} with comma rejected",
              tag == "" and meta["bad_args"] == 1 and meta["valid"] == 0,
              f"got {tag!r} meta={meta}")
    # and the reason is actionable, not opaque
    _, meta = ct.translate_tool_calls([("staff", {"site": "Kitchen, 0", "count": 2})])
    check("rejection carries a human-readable reason",
          bool(meta["errors"]) and "delimiter" in meta["errors"][0],
          f"errors={meta['errors']}")


def test_tag_injection_rejected() -> None:
    """An arg cannot close its own tag and open another (fabricated extra actions)."""
    print("\n[3] angle brackets in an arg -> bad_args (injection)")
    tag, meta = ct.translate_tool_calls(
        [("deconstruct", {"site": "Motel</deconstruct><hire>trained,99"})])
    check("tag-closing injection rejected",
          tag == "" and meta["bad_args"] == 1, f"got {tag!r} meta={meta}")
    check("no fabricated <hire> reached the parser", "<hire>" not in tag, f"got {tag!r}")


def test_batch_isolation() -> None:
    """One bad call does not poison the good ones in the same batch."""
    print("\n[4] a rejected call does not drop its well-formed siblings")
    tag, meta = ct.translate_tool_calls([
        ("build", {"type": "kitchen", "site_id": 3}),
        ("staff", {"site": "A,B", "count": 1}),          # bad
        ("hire", {"kind": "trained", "count": 2}),
    ])
    check("good calls survive", tag == "<build>kitchen,3</build> <hire>trained,2</hire>",
          f"got {tag!r}")
    check("counts are honest", meta["received"] == 3 and meta["valid"] == 2
          and meta["bad_args"] == 1, f"meta={meta}")


def test_none_still_means_unknown_name() -> None:
    """tag_for's None contract is unchanged: None == unknown tool, raise == bad args."""
    print("\n[5] tag_for contract: None vs TagArgError stay distinguishable")
    check("unknown tool -> None", ct.tag_for("teleport", {"x": 1}) is None)
    try:
        ct.tag_for("staff", {"site": "a,b", "count": 1})
        check("delimiter -> TagArgError", False, "no exception raised")
    except ct.TagArgError:
        check("delimiter -> TagArgError", True)
    _, meta = ct.translate_tool_calls([("teleport", {})])
    check("unknown name counted separately from bad args",
          meta["unknown_name"] == 1 and meta["bad_args"] == 0, f"meta={meta}")


def main() -> int:
    print("=" * 72)
    print("cora_tools: typed tool_call -> cmd-tag translation")
    print("=" * 72)
    test_wellformed_roundtrip()
    test_comma_rejected()
    test_tag_injection_rejected()
    test_batch_isolation()
    test_none_still_means_unknown_name()
    print("\n" + "=" * 72)
    if FAILS:
        print(f"FAILED ({len(FAILS)}): " + "; ".join(FAILS))
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
