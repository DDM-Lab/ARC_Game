"""
Declarative prompt packs for the CORA benchmark.

A *prompt pack* is a single JSON file that holds the director system prompt as a set
of named, editable text *sections* plus a `template` string that composes them. It lets
someone with light coding experience swap or A/B a prompt WITHOUT touching Python: edit
the JSON, point the benchmark at it (`--prompt-pack <name-or-path>`), read the numbers.

Design goals
------------
* **Low-code:** sections are plain text; the template is a `{section}` string a
  collaborator can reorder/rename to "present the information as they'd like".
* **Byte-exact provenance:** the built-in packs render byte-identically to the historical
  hardcoded prompts, so `prompt_sha` (sha1[:12] of the rendered system text) is preserved
  and old runs stay reproducible. Each pack records its expected sha in `provenance`.
* **Server-ready:** the flat JSON shape matches `config/*.json`, so a pack can later be
  uploaded to the scenario server and served through a scoped API key (admin dashboard TBD).

Pack schema (schema_version 1)
------------------------------
{
  "schema_version": 1,
  "name": "cmd_minimal",
  "description": "human-readable one-liner",
  "format": "cmd" | "idx",          # which action format this prompt targets
  "variant": "minimal",             # label recorded per-episode as system_variant
  "sections": { "<name>": "<text>", ... },
  "template": "{intro}{objective}...{transfer_doc}{image_preamble}",
  "gates": { "transfer_doc": "manual_transfers", "image_preamble": "image" },
  "provenance": { "prompt_sha_task_only": "...", "prompt_sha_manual": "..." }
}

Gating
------
A placeholder listed in `gates` is included only when its runtime condition holds:
  * "manual_transfers" -> included only when the env exposes standalone transfers.
  * "image"            -> the `image_preamble` slot is filled from
                          sections["image_preamble_<mode>"] only when an image is attached.
Ungated placeholders are always substituted from `sections` (missing -> "").
Because the built-in templates are a strict *partition* of the original prompt text
(contiguous slices, no separators added between them), concatenation reproduces the
original string exactly.
"""
import os
import re
import json
import glob
import hashlib

PACKS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "prompts")

_PLACEHOLDER = re.compile(r"\{(\w+)\}")


def prompt_sha(text):
    """The canonical per-episode prompt fingerprint: sha1[:12] over the rendered system text."""
    return hashlib.sha1(text.encode("utf-8")).hexdigest()[:12]


def pack_path(name_or_path):
    """Resolve a pack reference: an explicit .json path, or a bare name in prompts/."""
    if name_or_path.endswith(".json") and os.path.exists(name_or_path):
        return os.path.abspath(name_or_path)
    cand = os.path.join(PACKS_DIR, name_or_path + ".json")
    if os.path.exists(cand):
        return cand
    if os.path.exists(name_or_path):            # a path without the suffix
        return os.path.abspath(name_or_path)
    raise FileNotFoundError(
        f"prompt pack {name_or_path!r} not found (looked for {cand} and a literal path). "
        f"Available: {', '.join(list_packs()) or '(none)'}")


def load_pack(name_or_path):
    """Load + lightly validate a pack JSON. Returns the dict."""
    p = pack_path(name_or_path)
    with open(p, "r", encoding="utf-8") as f:
        pack = json.load(f)
    for key in ("name", "format", "sections", "template"):
        if key not in pack:
            raise ValueError(f"prompt pack {p} missing required field {key!r}")
    if pack["format"] not in ("cmd", "idx"):
        raise ValueError(f"prompt pack {p}: format must be 'cmd' or 'idx', got {pack['format']!r}")
    pack["_path"] = p
    return pack


def list_packs():
    """Names of every built-in / installed pack under prompts/."""
    return sorted(os.path.splitext(os.path.basename(p))[0]
                  for p in glob.glob(os.path.join(PACKS_DIR, "*.json")))


def render(pack, manual_transfers=False, image_mode="none", has_image=False):
    """Compose the pack's sections into the final system prompt string.

    Mirrors the historical composition exactly: base sections, then the transfer grammar
    only when manual transfers are enabled, then the mode-specific image line only when an
    image is attached. Gated slots collapse to "" when their condition is off, so a
    text-only / task_only render is byte-identical to the original hardcoded prompt.
    """
    sections = pack.get("sections", {})
    gates = pack.get("gates", {})

    def _fill(match):
        key = match.group(1)
        gate = gates.get(key)
        if gate == "manual_transfers" and not manual_transfers:
            return ""
        if gate == "image" or key == "image_preamble":
            if not has_image:
                return ""
            return sections.get(f"image_preamble_{image_mode}", "")
        return sections.get(key, "")

    return _PLACEHOLDER.sub(_fill, pack["template"])


def render_from(name_or_path, manual_transfers=False, image_mode="none", has_image=False):
    """Convenience: load + render in one call."""
    return render(load_pack(name_or_path), manual_transfers, image_mode, has_image)


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description="Inspect / render a prompt pack.")
    ap.add_argument("pack", nargs="?", help="pack name or path (omit to list all)")
    ap.add_argument("--manual-transfers", action="store_true")
    ap.add_argument("--image-mode", default="none", choices=["none", "synthetic", "real"])
    args = ap.parse_args()
    if not args.pack:
        for n in list_packs():
            pk = load_pack(n)
            print(f"{n:28s} format={pk['format']:3s} variant={pk.get('variant','?'):11s} "
                  f"{pk.get('description','')}")
        raise SystemExit(0)
    pk = load_pack(args.pack)
    has_img = args.image_mode != "none"
    text = render(pk, manual_transfers=args.manual_transfers,
                  image_mode=args.image_mode, has_image=has_img)
    print(f"# pack={pk['name']} format={pk['format']} sha={prompt_sha(text)} len={len(text)}\n")
    print(text)
