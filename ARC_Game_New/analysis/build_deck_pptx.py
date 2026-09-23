#!/usr/bin/env python3
"""Build the 10 new benchmarking slides as a .pptx for import into the Google Slides deck.

WHY A PPTX: nothing available here can edit a Google Slides presentation in place. Slides'
File > Import slides reads a .pptx, so this is the transport. Only the NEW slides are in
here -- the existing deck is never round-tripped through pptx, so slides 1-12 keep their
formatting untouched.

LAYOUT: figure across the top, empty text box as a band underneath (the blurb goes there by
hand). Geometry is computed per figure from its real pixel size at 150 dpi, so every image
keeps its aspect ratio and is centred; nothing is stretched.

Speaker notes carry the proposed title and the numbers behind each chart, so the slide
itself stays clean but the facts travel with the file.

USAGE
  python analysis/build_deck_pptx.py                 # -> analysis/deck_2026-09-02/CORA_new_slides.pptx
"""
import os
from PIL import Image
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor

HERE = os.path.dirname(os.path.abspath(__file__))
SRC  = os.path.join(HERE, "deck_2026-09-02")
OUT  = os.path.join(SRC, "CORA_new_slides.pptx")

SLIDE_W, SLIDE_H = 10.0, 5.625      # inches -- matches the deck's 720x405 pt page
MARGIN, TOP      = 0.30, 0.30
BAND_H, GAP      = 1.05, 0.12
DPI              = 150.0            # every figure is savefig'd at this dpi
MUTE             = RGBColor(0x5A, 0x6B, 0x78)

# (file, proposed title, the facts the blurb has to work with)
SLIDES = [
    ("01_net_score.png",
     "Net score — top 10 runs (n≥24)",
     "Qwen3.8-27B·v2eff_low 0.646, ·v3eff_low 0.643, ·v2·med 0.638; Haiku 4.5·v3n32 0.609; "
     "Qwen3.5-27B 0.599; Haiku·think2000 0.590; Haiku·H4 0.588; Qwen3.6-27B 0.585; "
     "Sonnet 5·v3n32 0.584; Qwen3.8-27B·v3n32 0.580. All n=32. Every run is still short of the "
     "0.790 scripted policy, and the CIs overlap across the whole top 10 — the ordering is not "
     "separated. Replaces old slide 4."),
    ("02_score_decomposition.png",
     "Score decomposition — same 10 runs",
     "Net score split into +food/+lodging/+worker/+casework against the four cost penalties. "
     "Worker use is saturated (0.94–1.00 across all ten); lodging 0.72–0.86; food 0.35–0.77; "
     "casework 0.23–0.41 is the component nobody buys. Sonnet 5 is the food outlier at 0.35. "
     "Replaces old slide 5."),
    ("03_pareto_overall.png",
     "Overall score: Pareto frontier",
     "7 of 26 runs non-dominated. Knee at ~$225k / 0.64 (Qwen3.8-27B v2eff_low and v3eff_low). "
     "gpt-oss-20b anchors the cheap corner at ~$72k / 0.13. Nothing buys score above 0.65 at any "
     "price."),
    ("04_pareto_lodging.png",
     "Lodging: Pareto frontier",
     "8 of 26 non-dominated. The frontier climbs from $65k / 0.08 (gpt-oss-20b) to ~$210k / 0.84 "
     "(Qwen3.8-27B v3eff_low). This is the axis that sets total spend."),
    ("05_pareto_food.png",
     "Food: Pareto frontier",
     "6 of 26 non-dominated, on a ~$2.5–11k axis. Haiku 4.5·think2000 buys 0.66 for $3.6k; "
     "Qwen3.8-27B pays 2–3× more for 0.74–0.77."),
    ("06_spend_share.png",
     "Where the money goes — share of spend",
     "Lodging is 92–97% of every top-10 run's spend. Totals $225.9k (Qwen3.8·v3eff_low) to "
     "$270.5k (Haiku·H4). Food is the only other visible slice, at 2–5%."),
    ("07_spend_by_category.png",
     "Spend by category — each on its own axis",
     "Food ~$3.5–10.8k · lodging ~$205–260k · worker ~$1.3–4.9k · casework ~$2.0–4.7k. "
     "Sonnet 5 is the outlier on both worker (~$4.9k) and casework (~$4.7k) spend."),
    ("08_cost_penalty.png",
     "Total cost penalty — least first",
     "Qwen3.5-27B 0.220 is the cheapest of the top 10; Qwen3.6-27B 0.324 the most expensive. "
     "The score leaders sit mid-table (v3eff_low 0.296, v2eff_low 0.304) — cheapest is not best."),
    ("09_total_satisfaction.png",
     "Total satisfaction",
     "2.888 down to 2.593 across the whole top 10 — a much tighter band than net score. "
     "The score spread is being driven by cost, not by needs met."),
    ("10_lodging_satisfaction.png",
     "Lodging satisfaction",
     "0.857 (Qwen3.8·v2eff_low) down to 0.722 (Qwen3.8·v3n32). Pair with the lodging frontier "
     "and the spend share: the runs that pay most for lodging are not the ones that meet the "
     "need best."),
]

NOTE_HEAD = ("Source: analysis/deck_2026-09-02/%s  (n≥24 run set: figs_top10_n24 / "
             "figs_pareto_n24, v1-prompt runs excluded)")


def main():
    prs = Presentation()
    prs.slide_width, prs.slide_height = Inches(SLIDE_W), Inches(SLIDE_H)
    blank = prs.slide_layouts[6]          # the only layout with no placeholders

    for fname, title, facts in SLIDES:
        path = os.path.join(SRC, fname)
        slide = prs.slides.add_slide(blank)

        # Fit the figure into what is left above the blurb band, preserving aspect.
        px_w, px_h = Image.open(path).size
        avail_w = SLIDE_W - 2 * MARGIN
        avail_h = SLIDE_H - TOP - BAND_H - GAP - 0.20
        scale = min(avail_w / (px_w / DPI), avail_h / (px_h / DPI))
        img_w, img_h = (px_w / DPI) * scale, (px_h / DPI) * scale
        slide.shapes.add_picture(path, Inches((SLIDE_W - img_w) / 2), Inches(TOP),
                                 Inches(img_w), Inches(img_h))

        # The blurb band: a real text box, left empty apart from a grey placeholder word so
        # it can actually be clicked in Slides (a truly empty box has no hit target).
        band_y = TOP + img_h + GAP
        box = slide.shapes.add_textbox(Inches(MARGIN), Inches(band_y), Inches(avail_w),
                                       Inches(SLIDE_H - band_y - 0.20))
        tf = box.text_frame
        tf.word_wrap = True
        run = tf.paragraphs[0].add_run()
        run.text = "Blurb"
        run.font.size, run.font.color.rgb = Pt(13), MUTE

        slide.notes_slide.notes_text_frame.text = (
            f"{title}\n\n{facts}\n\n" + NOTE_HEAD % fname)

    prs.save(OUT)
    print(f"{len(SLIDES)} slides  ->  {OUT}")


if __name__ == "__main__":
    main()
