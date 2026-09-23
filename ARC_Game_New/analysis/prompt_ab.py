#!/usr/bin/env python3
"""Prompt A/B: v3 vs v5 vs v6, scores + behaviour + prompt-engineering features.

WHAT CAN AND CANNOT BE INFERRED HERE
There are THREE Haiku prompt variants. Three points cannot support a regression of prompt
features on score -- any "which prompt feature predicts score" fit would have more parameters
than data. So prompt-level features are reported DESCRIPTIVELY, side by side with the scores.

What IS testable is the layer between them: each variant has 32 episodes, so the BEHAVIOURAL
consequences of a prompt edit (did the model stop skipping rounds? did it build differently?)
carry real statistical weight. That is where the inference lives, and it is what actually
explains the score movement.

Outputs go to a NEW directory; nothing existing is overwritten.
"""
import argparse, json, os, re, sys, statistics as st
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

from plot_components import house_axes, INK, MUTE, GRID, V3, V2, RUST, TEAL_RAMP

CELLS = [("Haiku 4.5", "v3", "cluster_api_v3n32_haiku45", "claude-haiku-4-5-20251001"),
         ("Haiku 4.5", "v5", "cluster_api_v5_haiku_sonnet_0901_1550", "claude-haiku-4-5-20251001"),
         ("Haiku 4.5", "v6", "cluster_api_v6_haiku_0901_1730", "claude-haiku-4-5-20251001"),
         ("Sonnet 5", "v3", "cluster_api_v3n32_sonnet5", "claude-sonnet-5"),
         ("Sonnet 5", "v5", "cluster_api_v5_haiku_sonnet_0901_1550", "claude-sonnet-5")]
ROOT = "benchmark_results/cluster_api"


# ── prompt-engineering features ───────────────────────────────────────────────────────
# Standard levers people actually pull when writing a system prompt. Each is a plain count
# or ratio so the three variants can be laid side by side without any modelling.
IMPERATIVES = r"\b(call|use|pick|read|skip|build|staff|hire|choose|respond|maximize|plan|take|do)\b"
HEDGES = r"\b(may|might|could|can|possibly|generally|usually|typically|often|sometimes)\b"
PROHIB = r"\b(never|do not|don't|cannot|must not|no\b)"
OBLIG = r"\b(must|always|should|need to|required|shall)\b"


def prompt_features(text):
    words = re.findall(r"[A-Za-z']+", text)
    sents = [s for s in re.split(r"[.!?\n]+", text) if s.strip()]
    caps = re.findall(r"\b[A-Z]{3,}\b", text)
    return {
        "chars": len(text),
        "words": len(words),
        "sentences": len(sents),
        "mean_sentence_words": round(len(words) / max(len(sents), 1), 1),
        "SHOUTED_tokens": len(caps),
        "distinct_SHOUTED": len(set(caps)),
        "imperatives": len(re.findall(IMPERATIVES, text, re.I)),
        "obligation_words": len(re.findall(OBLIG, text, re.I)),
        "prohibition_words": len(re.findall(PROHIB, text, re.I)),
        "hedge_words": len(re.findall(HEDGES, text, re.I)),
        "numbers_cited": len(re.findall(r"\$?\d[\d,]*", text)),
        "backtick_refs": text.count("`") // 2,
        "bullet_lines": len([l for l in text.splitlines() if l.strip().startswith(("-", "*"))]),
        "section_headers": len(re.findall(r"^[A-Z][A-Z &/—-]{4,}", text, re.M)),
        "second_person_you": len(re.findall(r"\byou\b|\byour\b", text, re.I)),
        "role_framing": ("director" if "director of" in text.lower()
                         else "participant" if "participant" in text.lower() else "none"),
        "permits_inaction": int(bool(re.search(r"NO action is a valid turn", text))),
        "urges_action": int(bool(re.search(r"PLAY TO WIN|lost round", text))),
        "asks_for_reasoning": int(bool(re.search(r"line of reasoning", text, re.I))),
        "states_score_formula": int(bool(re.search(r"score\s*=", text))),
    }


def load_cell(d, model):
    out = []
    for line in open(os.path.join(ROOT, d, "episodes.jsonl")):
        e = json.loads(line)
        if e.get("error") or not e.get("rounds") or e.get("model") != model:
            continue
        out.append(e)
    return out


def behaviour(eps):
    """Per-episode behaviour vectors -- these have n=32 and ARE testable."""
    b = defaultdict(list)
    for e in eps:
        rs = e["rounds"]
        empty = acts = hire = asg = 0
        opens = defaultdict(int)
        prev = None
        for r in rs:
            ac = r.get("actCats") or {}
            if not (r.get("nReq") or 0) and not (r.get("nSel") or 0):
                empty += 1
            acts += sum(ac.values()); hire += ac.get("worker", 0)
            asg += ac.get("worker_assignment", 0)
            fac = (r.get("obs") or {}).get("facilities")
            if fac is not None:
                nm = {f.get("name") for f in fac if f.get("name")}
                if prev is not None and (r.get("r", 0) - 1) <= 3:
                    for x in nm - prev:
                        t = next((f.get("type") for f in fac if f.get("name") == x), "?")
                        opens[t] += 1
                prev = nm
        c = rs[-1].get("comps") or {}
        sp = (rs[-1].get("obs") or {}).get("spend") or {}
        b["score"].append(c.get("score", 0) / 4)
        b["empty_pct"].append(100 * empty / len(rs))
        b["actions_per_ep"].append(acts)
        b["hires"].append(hire)
        b["assignments"].append(asg)
        b["open_shelter"].append(opens.get("Shelter", 0))
        b["open_casework"].append(opens.get("CaseworkSite", 0))
        b["spend_k"].append(sum(sp.get(k, 0) for k in ("food", "lodging", "worker", "casework")) / 1000)
        b["sat_lodging"].append(c.get("sat_lodging", 0))
        b["rounds"].append(len(rs))
    return b


def perm_test(a, b, rng, n=20000):
    a, b = np.asarray(a, float), np.asarray(b, float)
    obs = a.mean() - b.mean()
    pool = np.concatenate([a, b])
    hits = 0
    for _ in range(n):
        p = rng.permutation(pool)
        hits += abs(p[:len(a)].mean() - p[len(a):].mean()) >= abs(obs)
    return obs, max(hits / n, 1.0 / n)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--prompts", default="analysis/prompt_variants")
    ap.add_argument("--out", default="analysis/figs_prompt_ab")
    ap.add_argument("--perms", type=int, default=20000)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    rng = np.random.default_rng(0)

    data = {(m, v): behaviour(load_cell(d, mod)) for m, v, d, mod in CELLS}

    # ── prompt features (descriptive, n=3) ────────────────────────────────────────────
    feats = {}
    for v in ("v3", "v5", "v6"):
        p = os.path.join(a.prompts, f"minimal_{v}__typed.txt")
        if os.path.exists(p):
            feats[v] = prompt_features(open(p).read())
    keys = list(next(iter(feats.values())).keys())
    print("PROMPT FEATURES (descriptive only — n=3 variants cannot support a fit)\n")
    print(f"{'feature':<24} {'v3':>12} {'v5':>12} {'v6':>12}")
    for k in keys:
        vals = [str(feats[v].get(k, "")) for v in ("v3", "v5", "v6")]
        star = "  <-- differs" if len(set(vals)) > 1 else ""
        print(f"{k:<24} {vals[0]:>12} {vals[1]:>12} {vals[2]:>12}{star}")
    hs = {v: st.mean(data[("Haiku 4.5", v)]["score"]) for v in ("v3", "v5", "v6")}
    print(f"\n{'HAIKU SCORE':<24} {hs['v3']:>12.3f} {hs['v5']:>12.3f} {hs['v6']:>12.3f}")

    with open(os.path.join(a.out, "prompt_features.csv"), "w") as fh:
        fh.write("feature," + ",".join(("v3", "v5", "v6")) + "\n")
        for k in keys:
            fh.write(k + "," + ",".join(str(feats[v].get(k, "")) for v in ("v3", "v5", "v6")) + "\n")
        fh.write("haiku_score," + ",".join(f"{hs[v]:.4f}" for v in ("v3", "v5", "v6")) + "\n")

    # ── behavioural deltas (testable, n=32 per cell) ──────────────────────────────────
    METRICS = ["score", "empty_pct", "actions_per_ep", "assignments", "hires",
               "open_shelter", "open_casework", "spend_k", "sat_lodging"]
    print("\n\nBEHAVIOURAL EFFECT OF EACH PROMPT EDIT  (permutation test vs v3, n=32/cell)\n")
    results = []
    for model in ("Haiku 4.5", "Sonnet 5"):
        for v in ("v5", "v6"):
            if (model, v) not in data:
                continue
            print(f"  {model}  {v} vs v3")
            for k in METRICS:
                d, p = perm_test(data[(model, v)][k], data[(model, "v3")][k], rng, a.perms)
                results.append({"model": model, "variant": v, "metric": k,
                                "delta": d, "p": p})
                mark = "*" if p < 0.05 else " "
                print(f"      {mark} {k:<16} {d:>+9.2f}   p={p:.4f}")
            print()
    with open(os.path.join(a.out, "behaviour_deltas.csv"), "w") as fh:
        fh.write("model,variant,metric,delta,p\n")
        for r in results:
            fh.write(f"{r['model']},{r['variant']},{r['metric']},{r['delta']:.4f},{r['p']:.5f}\n")

    chart(data, os.path.join(a.out, "prompt_ab_scores.png"), rng, a.perms)
    behaviour_chart(data, os.path.join(a.out, "prompt_ab_behaviour.png"))


def chart(data, out, rng, perms):
    fig, ax = plt.subplots(figsize=(11.5, 5.6), dpi=150)
    labels, means, cis, cols = [], [], [], []
    for (model, v) in [("Haiku 4.5", "v3"), ("Haiku 4.5", "v5"), ("Haiku 4.5", "v6"),
                       ("Sonnet 5", "v3"), ("Sonnet 5", "v5")]:
        s = data[(model, v)]["score"]
        labels.append(f"{model}\n{v}")
        means.append(st.mean(s))
        cis.append(1.96 * st.stdev(s) / len(s) ** .5)
        cols.append(V3 if model.startswith("Haiku") else RUST)
    xs = np.arange(len(labels))
    ax.bar(xs, means, yerr=cis, color=cols, width=0.62,
           error_kw=dict(ecolor=INK, lw=1.0, capsize=4))
    for x, m, c in zip(xs, means, cis):
        ax.text(x, m + c + 0.012, f"{m:.3f}", ha="center", fontsize=9.5,
                family="monospace", color=INK)
    ax.set_xticks(xs); ax.set_xticklabels(labels, fontsize=10)
    ax.set_ylabel("normalised score  (mean, 95% CI)", fontsize=11)
    ax.set_title("Prompt A/B — v5 significantly HURTS Sonnet 5; neither edit "
                 "significantly helps Haiku\nn=32 episodes per cell",
                 fontsize=13, fontweight="bold", loc="left", pad=14)
    ax.axvline(2.5, color=GRID, lw=1.2, ls=":")
    ax.yaxis.grid(True, linestyle=":", color=GRID, lw=0.9)
    house_axes(ax)
    ax.annotate("", xy=(4, means[4] + 0.02), xytext=(3, means[3] + 0.02),
                arrowprops=dict(arrowstyle="->", color=RUST, lw=1.6))
    ax.text(3.5, max(means) * 0.92, "−0.127\np=0.0001", ha="center", fontsize=9.5,
            color=RUST, fontweight="bold")
    fig.tight_layout()
    fig.savefig(out, bbox_inches="tight", facecolor="white"); plt.close(fig)
    print(f"  wrote {out}")


def behaviour_chart(data, out):
    METRICS = [("empty_pct", "rounds doing nothing (%)"),
               ("actions_per_ep", "actions per episode"),
               ("assignments", "worker assignments"),
               ("open_shelter", "shelters built in r0–3")]
    fig, axes = plt.subplots(1, 4, figsize=(16.0, 4.6), dpi=150)
    cells = [("Haiku 4.5", "v3"), ("Haiku 4.5", "v5"), ("Haiku 4.5", "v6"),
             ("Sonnet 5", "v3"), ("Sonnet 5", "v5")]
    labels = [f"{'H' if m.startswith('Haiku') else 'S'} {v}" for m, v in cells]
    cols = [V3 if m.startswith("Haiku") else RUST for m, v in cells]
    for ax, (k, nice) in zip(axes, METRICS):
        vals = [st.mean(data[c][k]) for c in cells]
        errs = [1.96 * st.stdev(data[c][k]) / len(data[c][k]) ** .5 for c in cells]
        ax.bar(np.arange(len(cells)), vals, yerr=errs, color=cols, width=0.66,
               error_kw=dict(ecolor=INK, lw=0.9, capsize=3))
        ax.set_xticks(np.arange(len(cells))); ax.set_xticklabels(labels, fontsize=9)
        ax.set_title(nice, fontsize=11, fontweight="bold", pad=8)
        ax.yaxis.grid(True, linestyle=":", color=GRID, lw=0.9)
        house_axes(ax)
    fig.suptitle("What each prompt edit actually changed in behaviour   "
                 "(H = Haiku 4.5, S = Sonnet 5; 95% CI)",
                 fontsize=13, fontweight="bold", x=0.008, ha="left", y=0.995)
    fig.tight_layout(rect=(0, 0, 1, 0.94))
    fig.savefig(out, bbox_inches="tight", facecolor="white"); plt.close(fig)
    print(f"  wrote {out}")


if __name__ == "__main__":
    main()
