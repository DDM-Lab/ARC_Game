"""Prompt packs render exactly the prompts the benchmark has run with, and ablation edits them."""
import pytest

from cora.prompt_ablation import ablate, paraphrase_arms, rule_ids
from cora.prompts import list_packs, load_pack, prompt_sha, system_prompt

# Fingerprints of every rendering in use; changing any of these changes what models are shown.
# 2026-10: the execution-order rule now says hired workers arrive a few rounds later (it used to
# claim same-turn hire+staff works). The Sep 2026 cluster benchmark ran minimal_v6 task_only/typed
# as 09b23ffd5e31 (and minimal_v6_1 as 7ac0b7045b0f); git tag pre-cleanup-2026-10 has that text.
PINNED = {
    ("minimal_v6", False, "typed"): "0ec3d3cd3386",
    ("minimal_v6", False, "hermes"): "735473498175",
    ("minimal_v6", True, "typed"): "e991502de489",
    ("minimal_v6", True, "hermes"): "d25328d58492",
    ("minimal_v6_1", False, "typed"): "f25fab57715f",
    ("minimal_v6_1", False, "hermes"): "f21e32411c65",
    ("minimal_v6_1", True, "typed"): "75d19adf7b8a",
    ("minimal_v6_1", True, "hermes"): "6952078619b6",
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
