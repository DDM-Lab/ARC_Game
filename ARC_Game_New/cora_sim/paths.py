"""Where the surrogate keeps its runs. Everything the surrogate produces or validates against
lives under cora_sim/ (this directory) so the ARC_Game_New root stays the game's."""
import os

PKG = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(PKG)                       # ARC_Game_New: the gym, router and Unity project
RUNS = os.path.join(PKG, "runs")                  # evolution logs, headless validation captures (untracked)
VALIDATE = os.path.join(RUNS, "validate")         # staff_<seed>.json/.log oracle corpus (test_lockstep ratchet)
EVO_LOG = os.path.join(RUNS, "evo14.jsonl")       # the evolved plans the validation runs were driven by
