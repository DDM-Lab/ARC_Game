"""Make the ARC_Game_New package modules importable from tests/.

The tests were written when they sat next to the modules they import
(`import router.session`, `from cora.env import GameEnv`). They were moved into
tests/ for repo hygiene; this keeps those imports working without editing each file.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# The Unity captures under tests/fixtures were recorded on builds that ran the gym at the old
# 0.3 s timestep (before the GUI-speed change made 0.2 s the default), so the surrogate is checked
# against them at 0.3. Set before any test imports oracle.sim (MapSpec caches per process).
os.environ.setdefault("ARC_GYM_FIXED_DELTA", "0.3")
