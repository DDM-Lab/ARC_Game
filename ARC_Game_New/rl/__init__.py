"""Trainer-facing glue that is not tied to one RL framework: CoraEnv (rl/cora_env.py)."""
from rl.cora_env import CoraEnv, CoraEnvConfig

__all__ = ["CoraEnv", "CoraEnvConfig"]
