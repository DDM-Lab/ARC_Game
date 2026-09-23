#!/usr/bin/env python3
"""LaTeX tables for the CORA benchmark — the table form of analysis/plot_components.py.

Emits one ranked longtable per score/cost/spend component, plus two wide summary tables,
plus a master tables.tex that \\input's them all with the required preamble documented.

ESCAPING IS NOT OPTIONAL HERE. Config strings contain underscores ("min_v3") and middots.
Four of the existing .tex files in analysis_four_panel/ emit those raw, which is a hard
LaTeX error ("Missing $ inserted") in text mode. Every cell here goes through tex_escape(),
and check_tex() re-scans the finished file for any special character that survived.

USAGE
  python analysis/make_tables.py                       # all 84 runs -> analysis/tables
  python analysis/make_tables.py --top 10              # slide-ready short tables
  python analysis/make_tables.py --by-model            # best configuration per model
"""
import argparse, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from plot_components import (read, best_per_model, label, short, f, CI95,
                             SAT, COST, SPEND)

# LaTeX specials that must never reach the output raw. Backslash first or it re-escapes.
_ESC = [("\\", r"\textbackslash{}"), ("&", r"\&"), ("%", r"\%"), ("$", r"\$"),
        ("#", r"\#"), ("_", r"\_"), ("{", r"\{"), ("}", r"\}"),
        ("~", r"\textasciitilde{}"), ("^", r"\textasciicircum{}")]


def tex_escape(s):
    """Escape a plain-text cell. Middot becomes a command so the file is 7-bit safe."""
    s = str(s)
    for a, b in _ESC:
        s = s.replace(a, b)
    return s.replace("·", r"\textperiodcentered{}").replace("≥", r"$\geq$")


def tex_label(row):
    """Model in roman, the machine-readable cfg/run tag in \texttt -- one middot between.

    label() joins its parts with a double space; splitting there keeps the model name out
    of the monospace run so the column stays scannable.
    """
    parts = [x.strip() for x in label(row).split("  ·  ")]
    head = tex_escape(parts[0])
    rest = [r"\texttt{%s}" % tex_escape(x) for x in parts[1:]]
    return r" $\cdot$ ".join([head] + rest)


def check_tex(path):
    """Re-scan a written file for specials that escaped the escaper. Math mode is exempt."""
    bad = []
    for i, line in enumerate(open(path), 1):
        stripped = re.sub(r"\$[^$]*\$", "", line)       # drop math spans
        stripped = re.sub(r"\\[A-Za-z]+", "", stripped)  # drop commands
        for ch in ("_", "#", "^"):
            if ch in stripped.replace("\\" + ch, ""):
                bad.append((i, ch, line.rstrip()[:90]))
    return bad


def pm(mean, sem, dp=3, scale=1.0):
    """mean ± 95% CI, typeset the way the deck does it."""
    if mean is None:
        return "--"
    m = mean * scale
    if sem is None:
        return f"${m:.{dp}f}$"
    return f"${m:.{dp}f}\\,{{\\scriptstyle\\pm\\,{sem * scale * CI95:.{dp}f}}}$"


def ncols(spec):
    """Number of columns in a tabular preamble.

    Must strip @{...}, >{...} and <{...} decorations before counting, or the characters
    inside \\raggedright are counted as columns -- which silently produces a \\multicolumn
    span far wider than the table and a cascade of "Extra alignment tab" errors.
    """
    s = re.sub(r"[@><]\{[^}]*\}", "", spec)      # drop decorations
    s = re.sub(r"[pmb]\{[^}]*\}", "p", s)        # fixed-width column -> one letter
    return len(re.findall(r"[lcrpmbX]", s))


def longtable(fh, ncol_spec, header, rows, caption, tag, note="", size=""):
    if size:
        fh.write("{%s\\setlength{\\tabcolsep}{3pt}\n" % size)
    fh.write("\\begin{longtable}{%s}\n" % ncol_spec)
    fh.write("\\caption{%s}\\label{tab:%s}\\\\\n" % (caption, tag))
    fh.write("\\toprule\n%s \\\\\n\\midrule\n\\endfirsthead\n" % header)
    fh.write("\\multicolumn{%d}{l}{\\small\\itshape Table~\\ref{tab:%s} continued}\\\\\n"
             % (ncols(ncol_spec), tag))
    fh.write("\\toprule\n%s \\\\\n\\midrule\n\\endhead\n" % header)
    fh.write("\\midrule\\multicolumn{%d}{r}{\\small\\itshape continued}\\\\\n\\endfoot\n"
             % ncols(ncol_spec))
    fh.write("\\bottomrule\n\\endlastfoot\n")
    for r in rows:
        fh.write(" & ".join(r) + " \\\\\n")
    fh.write("\\end{longtable}\n")
    if note:
        fh.write("\\noindent{\\footnotesize %s\\par}\n" % note)
    if size:
        fh.write("}\n")


CI_NOTE = ("Uncertainty is the 95\\,\\% CI ($1.96\\times$SEM). "
           "Configuration is variant\\,$\\cdot$\\,H(history)\\,$\\cdot$\\,"
           "t/m(transfers)\\,$\\cdot$\\,effort; a trailing token disambiguates runs that "
           "share a configuration but differ in another setting (e.g.\\ thinking budget).")


def ranked_table(rows, mean_key, sem_key, nice, tag, out,
                 descending=True, dp=3, scale=1.0, unit=""):
    data = [(r, f(r, mean_key), f(r, sem_key)) for r in rows]
    data = [d for d in data if d[1] is not None]
    if not data:
        return None
    data.sort(key=lambda t: t[1], reverse=descending)
    body = [(str(i), tex_label(r), str(r.get("n", "")), pm(m, s, dp, scale))
            for i, (r, m, s) in enumerate(data, 1)]
    order = "highest first" if descending else "lowest first"
    cap = "%s, every run ranked %s." % (nice, order)
    with open(out, "w") as fh:
        longtable(fh, "@{}r>{\\raggedright\\arraybackslash}p{0.50\\linewidth}rr@{}",
                  "Rank & Configuration & $n$ & %s" % (unit or nice),
                  body, cap, tag, CI_NOTE, size="\\small")
    return out


def wide_table(rows, cols, sort_key, caption, tag, out, dp=3, scale=1.0,
               descending=True, label_frac=None):
    data = sorted([r for r in rows if f(r, sort_key) is not None],
                  key=lambda r: f(r, sort_key), reverse=descending)
    frac = label_frac if label_frac else (0.16 if len(cols) > 5 else 0.22)
    spec = ("@{}r>{\\raggedright\\arraybackslash}p{%.2f\\linewidth}r" % frac
            + "r" * len(cols) + "@{}")
    header = "Rank & Configuration & $n$ & " + " & ".join(h for _, h in cols)
    body = []
    for i, r in enumerate(data, 1):
        cells = [pm(f(r, f"{k}_mean"), f(r, f"{k}_sem"), dp, scale) for k, _ in cols]
        body.append([str(i), tex_label(r), str(r.get("n", ""))] + cells)
    with open(out, "w") as fh:
        longtable(fh, spec, header, body, caption, tag, CI_NOTE,
                  size="\\tiny" if len(cols) > 5 else "\\scriptsize")
    return out


PREAMBLE = r"""% Required packages for the tables in this directory.
% longtable  -- tables span pages (84 runs does not fit on one)
% booktabs   -- \toprule / \midrule / \bottomrule
% textcomp   -- \textperiodcentered outside math
%
% array      -- >{\raggedright\arraybackslash} column modifiers
%
% \usepackage{longtable,booktabs,textcomp,array}
%
% Every table is a longtable, so use \begin{document} body context, NOT inside
% table/table* (longtable floats itself). For a fixed float, pass --top N and wrap
% the emitted tabular rows yourself.
"""


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--components", default="analysis_four_panel/components_per_run.csv")
    p.add_argument("--spend", default="analysis_four_panel/spend_per_run.csv")
    p.add_argument("--out", default="analysis/tables")
    p.add_argument("--top", type=int, default=None, help="keep only the top N runs by score")
    p.add_argument("--by-model", action="store_true")
    p.add_argument("--min-n", type=int, default=None,
                   help="drop runs with fewer than N episodes; the deck's top-10 uses 24")
    p.add_argument("--with-v1", action="store_true",
                   help="include v1-prompt runs (excluded by default: the v1 prompt never states the "
                        "Motel per-resident daily cost, which drives ~95%% of spend)")
    a = p.parse_args()
    os.makedirs(a.out, exist_ok=True)
    o = lambda n: os.path.join(a.out, n)

    comp, spend = read(a.components), read(a.spend)
    if not a.with_v1:
        drop = {r['dir'] for r in comp if r['_v'] == 'v1'}
        comp = [r for r in comp if r['dir'] not in drop]
        spend = [r for r in spend if r['dir'] not in drop]
        print(f'excluded {len(drop)} v1-prompt runs')
    if a.min_n:
        comp = [r for r in comp if int(r.get("n") or 0) >= a.min_n]
    if a.by_model:
        comp = best_per_model(comp, "score_mean")
    if a.top:
        comp = sorted(comp, key=lambda r: -(f(r, "score_mean") or -9))[:a.top]
    keep = {(r["dir"], r["model"], r["cfg"]) for r in comp}
    spend = [r for r in spend if (r["dir"], r["model"], r["cfg"]) in keep]

    # score_mean is the RAW cumulative score; every figure and the deck report it /4.
    for r in comp:
        for suf in ("mean", "sem"):
            v = f(r, f"score_{suf}")
            if v is not None:
                r[f"score_norm_{suf}"] = str(v / 4.0)

    written = []
    written.append(ranked_table(
        comp, "score_norm_mean", "score_norm_sem", "Normalised score", "rank-score",
        o("tab_rank_score.tex"), unit="Score $=$ total\\,/\\,4"))

    for k, nice in SAT:
        written.append(ranked_table(comp, f"{k}_mean", f"{k}_sem", nice,
                                    f"rank-sat-{k.replace('_','-')}",
                                    o(f"tab_rank_sat_{k}.tex"), unit=tex_escape(nice)))
    for k, nice in COST:
        written.append(ranked_table(comp, f"{k}_mean", f"{k}_sem", nice,
                                    f"rank-cost-{k.replace('_','-')}",
                                    o(f"tab_rank_cost_{k}.tex"), descending=False,
                                    unit=tex_escape(nice)))
    for k, nice in SPEND:
        written.append(ranked_table(spend, f"{k}_mean", f"{k}_sem", nice,
                                    f"rank-spend-{k}", o(f"tab_rank_spend_{k}.tex"),
                                    descending=False, dp=1, scale=1e-3,
                                    unit=tex_escape(nice) + " (\\$k)"))

    written.append(wide_table(
        comp, [("sat_food", "sFood"), ("sat_lodging", "sLodg"),
               ("sat_worker_use", "sWkr"), ("casework_processing_sat", "cwSat"),
               ("cost_food", "cFood"), ("cost_lodging", "cLodg"),
               ("cost_worker", "cWkr"), ("casework_efficiency", "cwEff"),
               ("score_norm", "Score")],
        "score_mean",
        "Score components per run. Satisfaction terms are positive, cost terms subtract; "
        "Score $=(\\Sigma\\mathrm{sat}-\\Sigma\\mathrm{cost})/4$.",
        "score-components", o("tab_score_components.tex"), dp=2))

    written.append(wide_table(
        spend, [("food", "Food"), ("lodging", "Lodging"), ("worker", "Worker"),
                ("casework", "Casework"), ("total", "Total")],
        "total_mean",
        "Spend per run by category, \\$ thousands. Lodging is 89--97\\,\\% of every run's "
        "total, which is why the categories need separate axes when plotted.",
        "spend-per-run", o("tab_spend.tex"), dp=1, scale=1e-3, descending=False,
        label_frac=0.18))

    written = [w for w in written if w]
    with open(o("tables.tex"), "w") as fh:
        fh.write(PREAMBLE + "\n")
        for w in sorted(written):
            fh.write("\\input{%s}\n" % os.path.splitext(os.path.basename(w))[0])

    print(f"{len(written)} tables -> {a.out}  ({len(comp)} runs)")
    bad = 0
    for w in written:
        for ln, ch, txt in check_tex(w):
            print(f"  UNESCAPED {ch!r} {os.path.basename(w)}:{ln}  {txt}")
            bad += 1
    print("escape check: clean" if not bad else f"escape check: {bad} problems")


if __name__ == "__main__":
    main()
