"""CMU AI gateway client config for the LLM harnesses (playthrough + benchmark).

The gateway is OpenAI-compatible; callers construct `openai.OpenAI(api_key=load_env_key(),
base_url=GATEWAY_BASE)`. Moved out of `llm_smoke_test.py` so the key/endpoint config lives
in one appropriately named place instead of a script.
"""
import os
from pathlib import Path

GATEWAY_BASE = "https://ai-gateway.andrew.cmu.edu/v1"


def load_env_key():
    """OPENAI_API_KEY from the environment, falling back to a local .env file."""
    key = os.environ.get("OPENAI_API_KEY")
    if key:
        return key
    env_path = Path(__file__).parent / ".env"
    if env_path.exists():
        for line in env_path.read_text().splitlines():
            if line.startswith("OPENAI_API_KEY="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    raise RuntimeError("OPENAI_API_KEY not found in environment or .env")
