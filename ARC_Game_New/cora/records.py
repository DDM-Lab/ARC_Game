"""Reading episode records (episodes.jsonl) written by the benchmark.

One record per episode: settings, scenario, prompt fingerprint, per-round records under "rounds"
and a "summary". The loader is shared by the plots, the analysis scripts and the surrogate's
validators, so "a completed episode" means the same thing everywhere.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Iterator, Optional


def iter_episodes(path) -> Iterator[dict]:
    """Every record in an episodes.jsonl file, or in <dir>/episodes.jsonl."""
    p = Path(path)
    if p.is_dir():
        p = p / "episodes.jsonl"
    with open(p) as f:
        for line in f:
            if line.strip():
                yield json.loads(line)


def load_episodes(path, *, completed_only: bool = True, model: Optional[str] = None) -> list:
    """Records from `path`. completed_only keeps episodes that ran without error and have rounds
    and a summary (a crashed episode is never averaged in). Whether the game reached its end is
    record["terminated"]; the number of decisions is len(record["rounds"])."""
    out = []
    for r in iter_episodes(path):
        if model is not None and r.get("model") != model:
            continue
        if completed_only and (r.get("error") or not r.get("rounds") or not r.get("summary")):
            continue
        out.append(r)
    return out
