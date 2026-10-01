"""System prompts, defined as prompt packs (the single source of prompt text).

A prompt pack is a JSON file in prompts/: named text `sections` composed by a `{section}`
`template`, plus `gates` that switch sections on the run's settings. Someone without coding
experience adds or edits a prompt by copying a JSON file; nothing in Python holds prompt text.

    {
      "schema_version": 2,
      "name": "minimal_v6_1",
      "description": "...",
      "sections": {"preamble": "...", "how_header_typed": "...", ...},
      "template": "{preamble}{how_header}{tool_signatures}...",
      "gates": {"how_header": "wire_format", "transfer_signature": "manual_transfers", ...},
      "observation": {"mark_unavailable_choices": true}      # optional
    }

Gates:
  wire_format       the placeholder X is filled from sections["X_<wire_format>"] (typed | hermes)
  manual_transfers  included only when standalone transfers are offered
  image             filled from sections["image_preamble_<image_mode>"] when an image is shown

`observation` lists the observation features the prompt's text relies on (minimal_v6_1 says that
unavailable choices are marked, so the observation must mark them). Callers pass it on to
cora.observation.

The rendered text is fingerprinted with prompt_sha (sha1[:12]); every benchmark record stores it.
"""
from __future__ import annotations

import glob
import hashlib
import json
import os
import re
from dataclasses import dataclass, field

PACKS_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "prompts")
DEFAULT_PACK = "minimal_v6_1"
WIRE_FORMATS = ("typed", "hermes")
IMAGE_MODES = ("none", "synthetic", "real")

_PLACEHOLDER = re.compile(r"\{(\w+)\}")


@dataclass(frozen=True)
class PromptPack:
    name: str
    description: str
    sections: dict
    template: str
    gates: dict = field(default_factory=dict)
    observation: dict = field(default_factory=dict)
    path: str = ""


def prompt_sha(text: str) -> str:
    """The prompt fingerprint recorded with every episode: sha1[:12] of the rendered text."""
    return hashlib.sha1(text.encode("utf-8")).hexdigest()[:12]


def list_packs() -> list:
    return sorted(os.path.splitext(os.path.basename(p))[0]
                  for p in glob.glob(os.path.join(PACKS_DIR, "*.json")))


def load_pack(name_or_path: str = DEFAULT_PACK) -> PromptPack:
    """Load a pack by name (a file in prompts/) or by path."""
    path = name_or_path if name_or_path.endswith(".json") else os.path.join(PACKS_DIR, name_or_path + ".json")
    if not os.path.exists(path):
        raise FileNotFoundError(f"prompt pack {name_or_path!r} not found; available: {', '.join(list_packs())}")
    with open(path, encoding="utf-8") as f:
        raw = json.load(f)
    if raw.get("schema_version") != 2:
        raise ValueError(f"{path}: schema_version must be 2 (tool-call prompts)")
    for key in ("name", "sections", "template"):
        if key not in raw:
            raise ValueError(f"{path}: missing required field {key!r}")
    return PromptPack(name=raw["name"], description=raw.get("description", ""), sections=raw["sections"],
                      template=raw["template"], gates=raw.get("gates", {}),
                      observation=raw.get("observation", {}), path=os.path.abspath(path))


def render(pack: PromptPack, *, manual_transfers: bool = False, wire_format: str = "typed",
           image_mode: str = "none") -> str:
    """Compose a pack into the system prompt for one run's settings."""
    if wire_format not in WIRE_FORMATS:
        raise ValueError(f"wire_format must be one of {WIRE_FORMATS}, got {wire_format!r}")
    if image_mode not in IMAGE_MODES:
        raise ValueError(f"image_mode must be one of {IMAGE_MODES}, got {image_mode!r}")

    def fill(match):
        key = match.group(1)
        gate = pack.gates.get(key)
        if gate == "wire_format":
            return pack.sections[f"{key}_{wire_format}"]
        if gate == "manual_transfers":
            return pack.sections.get(key, "") if manual_transfers else ""
        if gate == "image":
            return pack.sections.get(f"{key}_{image_mode}", "") if image_mode != "none" else ""
        return pack.sections.get(key, "")

    return _PLACEHOLDER.sub(fill, pack.template)


def system_prompt(pack_name: str = DEFAULT_PACK, *, manual_transfers: bool = False,
                  wire_format: str = "typed", image_mode: str = "none", ablation: str = "") -> str:
    """Load + render a pack, optionally applying a rule ablation (see cora.prompt_ablation)."""
    text = render(load_pack(pack_name), manual_transfers=manual_transfers,
                  wire_format=wire_format, image_mode=image_mode)
    if ablation:
        from cora.prompt_ablation import ablate
        text = ablate(text, ablation)
    return text


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description="Print a prompt pack exactly as a model receives it.")
    ap.add_argument("pack", nargs="?", default=DEFAULT_PACK, help=f"pack name or path ({', '.join(list_packs())})")
    ap.add_argument("--manual-transfers", action="store_true")
    ap.add_argument("--wire-format", default="typed", choices=WIRE_FORMATS)
    ap.add_argument("--image-mode", default="none", choices=IMAGE_MODES)
    ap.add_argument("--ablate", default="")
    a = ap.parse_args()
    text = system_prompt(a.pack, manual_transfers=a.manual_transfers, wire_format=a.wire_format,
                         image_mode=a.image_mode, ablation=a.ablate)
    print(text)
    print(f"\n--- prompt_sha {prompt_sha(text)} ({len(text)} chars)")
