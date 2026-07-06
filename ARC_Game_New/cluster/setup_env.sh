#!/usr/bin/env bash
# One-time Python env setup on the cluster login/compute node.
# Run from the repo root after cloning/copying the repo there.
#   bash cluster/setup_env.sh
set -euo pipefail
cd "$(dirname "$0")/.."

PYBIN="${PYBIN:-python3}"
"$PYBIN" -m venv .venv
source .venv/bin/activate
pip install --upgrade pip wheel

# Benchmark/gym deps (same as local) + vLLM for serving.
# NOTE: pin vLLM to a version new enough for the models you serve. As of mid-2026,
# gpt-oss / Qwen3 / Gemma-3 need a recent vLLM (>= 0.6.x w/ the right reasoning parsers).
# If a model fails to load, upgrade vLLM first — that's the usual cause.
pip install gymnasium numpy openai pillow requests
pip install "vllm"        # TODO: pin, e.g. vllm==0.6.4, matched to your CUDA/torch

echo
echo "[setup] done. Activate with: source .venv/bin/activate"
echo "[setup] verify the gym imports:"
python -c "import gymnasium, openai, numpy; print('  deps OK')"
echo "[setup] verify the Linux Unity build is present + executable:"
ls -l Build/Headless/Linux/ARC_Headless.x86_64 2>/dev/null || \
  echo "  MISSING -> build on the Mac (HeadlessBuildScript.BuildLinux) and copy Build/Headless/Linux/ here"
