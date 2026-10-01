"""Make the ARC_Game_New package modules importable from tests/.

The tests were written when they sat next to the modules they import
(`import cmd_parser`, `from cora.env import GameEnv`). They were moved into
tests/ for repo hygiene; this keeps those imports working without editing each file.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
