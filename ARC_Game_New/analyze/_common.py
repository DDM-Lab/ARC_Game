"""Shared helpers for the CORA benchmark analysis toolkit.

Standard library + pandas/matplotlib only. Nothing here is hard-coded to a
particular model, run name, or set of configuration values: the corpus is
described entirely by the data in the results tree.
"""
from __future__ import annotations

import argparse
import glob as _glob
import json
from pathlib import Path

import numpy as np
import pandas as pd

RESULTS_DEFAULT = ("benchmark_results/cluster*",)

# Config axes that vary between otherwise-identical models. A "fixed config"
# pins each of these; any model-vs-model comparison must state the pin.
CONFIG_AXES = ("history", "max_tokens", "reasoning_effort", "transfers")
PROMPT_AXIS = "prompt_sha"

# Okabe-Ito colour-blind-safe palette, plus tints for >8 series.
PALETTE = [
    "#0072B2", "#E69F00", "#009E73", "#CC79A7",
    "#56B4E9", "#D55E00", "#F0E442", "#4D4D4D",
    "#82B4D8", "#EBBD5E", "#7ECBA8", "#DCA9C4",
    "#A8D5F2", "#E8985F", "#E9D98B", "#8A8A8A",
]
MARKERS = ["o", "s", "^", "D", "v", "P", "X", "*", "h", "+"]


def parse_common_args(argv=None, extra=None) -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--results", action="append", metavar="GLOB",
                   help="repeatable glob/dir of results trees "
                        "(default: %s)" % " ".join(RESULTS_DEFAULT))
    p.add_argument("--out", default="analyze/out",
                   help="output directory (default: analyze/out)")
    if extra:
        extra(p)
    args = p.parse_args(argv)
    if not args.results:
        args.results = list(RESULTS_DEFAULT)
    args.out = Path(args.out)
    return args


def expand_results(patterns) -> list:
    """Expand --results patterns to a deterministic list of
    (tree_root: Path, episodes_file: Path) pairs."""
    pairs = {}
    for pat in patterns:
        matches = sorted(_glob.glob(pat, recursive=True))
        if not matches:
            raise SystemExit(f"error: --results pattern {pat!r} matched nothing (check cwd)")
        for m in matches:
            root = Path(m)
            if root.is_dir():
                files = sorted(root.rglob("episodes.jsonl"))
            elif root.name == "episodes.jsonl":
                files = [root]
                root = root.parent
            else:
                continue
            for f in files:
                pairs.setdefault(f.resolve(), (root, f))
    if not pairs:
        raise SystemExit("error: no episodes.jsonl found under --results paths")
    return [pairs[k] for k in sorted(pairs)]


def run_identity(tree: Path, f: Path, file: Path) -> dict:
    """Derive run/source_tree/run_dir from (tree root, episodes file).

    Tree layout is <tree>/<run>/.../episodes.jsonl with an arbitrary number of
    intermediate directories (e.g. baselines_33217/<policy>/). The run id is
    the relative directory path so same-named subdirs never collide.
    """
    rel = [p for p in file.relative_to(tree).parts[:-1]]
    run_dir = "/".join(rel) if rel else file.parent.name
    return {
        "source_tree": tree.name,
        "run": (rel or [file.parent.name])[-1],
        "run_dir": run_dir,
        "run_id": f"{tree.name}/{run_dir}",
    }


def ep_ok(obj: dict) -> bool:
    """An episode counts as completed iff it errored out and has no rounds."""
    if obj.get("error") is not None:
        return False
    s = obj.get("summary") or {}
    rp = s.get("roundsPlayed")
    return rp is not None and rp > 0


def check_schema(obj, where: str) -> None:
    """Fail loudly on a structurally wrong object (not a bad value)."""
    if not isinstance(obj, dict):
        raise SystemExit(f"schema error in {where}: episode object is not a mapping")
    if not isinstance(obj.get("model"), str):
        raise SystemExit(f"schema error in {where}: missing/invalid 'model' string field")
    if not isinstance(obj.get("episode"), int):
        raise SystemExit(f"schema error in {where}: missing/invalid 'episode' int field")
    if not isinstance(obj.get("rounds"), list):
        raise SystemExit(f"schema error in {where}: 'rounds' is not a list")
    s = obj.get("summary")
    if s is not None and not isinstance(s, dict):
        raise SystemExit(f"schema error in {where}: 'summary' is not a dict")


def check_round(rd, where: str) -> None:
    if not isinstance(rd, dict):
        raise SystemExit(f"schema error in {where}: round entry is not a mapping")
    if not isinstance(rd.get("r"), int):
        raise SystemExit(f"schema error in {where}: round 'r' is not an int")


def read_frames(out: Path):
    """Load the CSVs written by load.py."""
    for name in ("episodes.csv", "rounds.csv"):
        p = out / name
        if not p.exists():
            raise SystemExit(
                f"error: {p} not found. Run `python analyze/load.py --results "
                f"'benchmark_results/cluster*' --out {out}` first.")
    return (pd.read_csv(out / "episodes.csv", low_memory=False),
            pd.read_csv(out / "rounds.csv", low_memory=False))


def load_stats(out: Path) -> dict:
    p = out / "LOAD_STATS.json"
    if not p.exists():
        return {}
    return json.loads(p.read_text())


def num(s: pd.Series) -> pd.Series:
    """Coerce a mixed object column to float, leaving blanks as NaN."""
    return pd.to_numeric(s, errors="coerce")


def mean_ci(vals):
    """Mean and 95% CI half-width; (mean, 0.0, n) for n<2."""
    v = np.asarray([x for x in vals if x is not None and not (isinstance(x, float) and np.isnan(x))], dtype=float)
    n = len(v)
    if n == 0:
        return float("nan"), 0.0, 0
    if n == 1:
        return float(v[0]), 0.0, 1
    return float(v.mean()), float(1.96 * v.std(ddof=1) / np.sqrt(n)), n


def series_style(models) -> dict:
    """Stable model -> (colour, marker, linestyle) map derived from sorted names."""
    styles = {}
    for i, m in enumerate(sorted(models)):
        styles[m] = (PALETTE[i % len(PALETTE)],
                     MARKERS[i % len(MARKERS)],
                     (":" if (i // len(PALETTE)) else "-"))
    return styles


def detect_baseline_models(ep: pd.DataFrame) -> set:
    """A model is a non-learning baseline when its calls carry no LLM token
    budget (max_tokens is null on every one of its episodes). This is a
    property of the data, not of the name; override with --baseline/--no-baseline."""
    if "max_tokens" not in ep.columns:
        return set()
    out = set()
    for m, g in ep.groupby("model"):
        mt = g["max_tokens"]
        if len(mt) and mt.isna().all():
            out.add(m)
    return out


def short_name(m: str) -> str:
    return m.split("/")[-1]


def set_style():
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    plt.rcParams.update({
        "font.size": 9,
        "axes.titlesize": 10,
        "axes.labelsize": 9,
        "axes.grid": True,
        "grid.alpha": 0.3,
        "axes.axisbelow": True,
        "figure.dpi": 100,
        "savefig.dpi": 200,
        "savefig.bbox": "tight",
        "legend.fontsize": 8,
        "pdf.fonttype": 42,
    })


def save_fig(fig, out: Path, name: str):
    out.mkdir(parents=True, exist_ok=True)
    fdir = out / "figures"
    fdir.mkdir(parents=True, exist_ok=True)
    fig.savefig(fdir / f"{name}.png")
    fig.savefig(fdir / f"{name}.pdf")
    import matplotlib.pyplot as plt
    plt.close(fig)


def config_axes_seen(ep: pd.DataFrame) -> list:
    return [a for a in (*CONFIG_AXES, PROMPT_AXIS) if a in ep.columns]


def pick_col(df, *names):
    for n in names:
        if n in df.columns:
            return n
    return None


def axis_levels(ser: pd.Series):
    """Deterministically ordered levels; numeric-aware for max_tokens."""
    vals = sorted(set(ser.dropna().astype(str)))
    def key(s: str):
        try:
            return (0, float(s), "")
        except ValueError:
            return (1, 0.0, s)
    return sorted(vals, key=key)


def fmt_level(v) -> str:
    """Normalize a config level to a canonical string (512.0 -> '512', NaN -> None)."""
    if v is None:
        return None
    try:
        f = float(v)
    except (TypeError, ValueError):
        return str(v)
    if f != f:  # NaN
        return None
    return str(int(f)) if f == int(f) else str(f)


def norm_series(ser: pd.Series) -> pd.Series:
    return ser.map(fmt_level)


def fixed_config(ep: pd.DataFrame, model_ok: pd.Series, fixes: dict) -> tuple:
    """Filter completed episodes to a fixed configuration.

    fixes: {axis: value-or-None}; None means "most common level in the data".
    Returns (filtered_df, label_string).
    """
    axes = list(CONFIG_AXES) + [PROMPT_AXIS]
    resolved = {}
    for a in axes:
        if a not in ep.columns:
            resolved[a] = None
            continue
        v = fixes.get(a)
        v = fmt_level(v)
        if v is None:
            vc = norm_series(ep.loc[model_ok, a]).dropna().value_counts()
            v = vc.index[0] if len(vc) else None
        resolved[a] = v
    mask = model_ok.copy()
    for a, v in resolved.items():
        if v is None:
            continue
        mask &= norm_series(ep[a]) == v
    label = ", ".join(f"{a}={v}" for a, v in resolved.items())
    return ep[mask], label
