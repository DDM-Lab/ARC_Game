"""Prompt packs render exactly the prompts the benchmark has run with, and ablation edits them."""
import pytest

from cora.prompt_ablation import ablate, paraphrase_arms, rule_ids
from cora.prompts import list_packs, load_pack, prompt_sha, system_prompt

# Fingerprints of every rendering in use. minimal_v6 task_only/typed is the Sep 2026 cluster
# benchmark prompt (09b23ffd5e31); changing any of these changes what models were shown.
PINNED = {
    ("minimal_v6", False, "typed"): "09b23ffd5e31",
    ("minimal_v6", False, "hermes"): "b4ff3b2cf458",
    ("minimal_v6", True, "typed"): "78d175df9843",
    ("minimal_v6", True, "hermes"): "e007d2daa5e9",
    ("minimal_v6_1", False, "typed"): "7ac0b7045b0f",
    ("minimal_v6_1", False, "hermes"): "684f01573802",
    ("minimal_v6_1", True, "typed"): "3e5a00d77d4c",
    ("minimal_v6_1", True, "hermes"): "f7fc46e7fdf0",
}


@pytest.mark.parametrize("pack,manual,wire", sorted(PINNED))
def test_pack_renders_pinned_prompt(pack, manual, wire):
    assert prompt_sha(system_prompt(pack, manual_transfers=manual, wire_format=wire)) == PINNED[(pack, manual, wire)]


def test_packs_listed_and_v6_1_marks_unavailable_choices():
    assert {"minimal_v6", "minimal_v6_1"} <= set(list_packs())
    assert load_pack("minimal_v6_1").observation == {"mark_unavailable_choices": True}
    assert load_pack("minimal_v6").observation == {}


def test_transfer_line_only_with_manual_transfers():
    assert "transfer(resource" not in system_prompt("minimal_v6")
    assert "transfer(resource" in system_prompt("minimal_v6", manual_transfers=True)


def test_image_preamble_only_with_image():
    assert "ALSO given" not in system_prompt("minimal_v6")
    assert "ALSO given a rendered top-down map" in system_prompt("minimal_v6", image_mode="synthetic")


def test_ablation_removes_each_rule_once():
    base = system_prompt("minimal_v6")
    for rid in rule_ids():
        if rid == "R33":                      # hermes-only passage
            continue
        assert len(ablate(base, rid)) < len(base), rid


def test_ablation_errors_instead_of_silent_baseline():
    with pytest.raises(ValueError, match="R33"):
        system_prompt("minimal_v6", ablation="R33")
    with pytest.raises(ValueError, match="unknown"):
        system_prompt("minimal_v6", ablation="R99")


def test_paraphrase_arm_changes_text():
    base = system_prompt("minimal_v6")
    arm = paraphrase_arms()[0]
    assert ablate(base, arm) != base
