#!/bin/bash
# CORA model sweep on the cluster: the non-LLM baselines, then each model in turn, all on the same
# seeds (episode i plays SEED+i), then one comparison table. Each model is served by
# ops/benchmark_cluster.sbatch (MODE=local: vllm serve on this node, stopped before the next).
#
#   sbatch ops/benchmark_sweep.sbatch
#   EPISODES=20 MODELS_ONLY="qwen3-4b,gpt-oss-20b" sbatch ops/benchmark_sweep.sbatch
#
# A model entry is  label|hf-id or path|tensor-parallel size|extra env (space-separated K=V)
#   Qwen hybrids run with thinking off (benchmark_cluster.sbatch's NOTHINK default).
#   gpt-oss cannot turn reasoning off; it runs at its lowest effort.
#
#SBATCH --job-name=cora_sweep
#SBATCH --partition=general
#SBATCH --qos=qos_general
#SBATCH --gres=gpu:a6000:2
#SBATCH --cpus-per-task=32
#SBATCH --mem=160G
#SBATCH --time=16:00:00
#SBATCH --output=/zfsauton/scratch/%u/CORA/logs/bench/%x_%j.out

set -uo pipefail
ARC=${ARC_GAME_PATH:-/zfsauton/scratch/$USER/ARC_Game_ddm/ARC_Game_New}
cd "$ARC"
export ARC_GAME_PATH="$ARC" PYTHONUNBUFFERED=1
PY=${PY:-/zfsauton/scratch/$USER/conda_envs/vllm027/bin/python}
EPISODES=${EPISODES:-10}
SEED=${SEED:-5501}
WORKERS=${WORKERS:-8}
TAG=${TAG:-$(date +%m%d_%H%M)}
ROOT=${ROOT:-benchmark_results/sweep_${TAG}}
mkdir -p "$ROOT" "/zfsauton/scratch/$USER/CORA/logs/bench"
BP=$(( 20000 + (${SLURM_JOB_ID:-0} % 180) * 64 ))     # see benchmark_cluster.sbatch: below 32768

MODELS=(
  "qwen3-4b|Qwen/Qwen3-4B|1|"
  "qwen3.5-4b|Qwen/Qwen3.5-4B|1|"
  "gpt-oss-20b|openai/gpt-oss-20b|1|NOTHINK=0 REASONING_EFFORT=low"
  "qwen3.8-27b|Qwen/Qwen3.8-27B|2|"
)
BASELINES=${BASELINES:-noop,greedy,build-potential,combined,pareto}

echo "=== sweep $TAG  job=${SLURM_JOB_ID:-local}  node=$(hostname)  $(git log --oneline -1)  $(date)"
echo "    episodes=$EPISODES seed=$SEED workers=$WORKERS root=$ROOT"

for b in ${BASELINES//,/ }; do
  echo "=== [baseline] $b"
  $PY -u -m bench --policy "$b" --episodes "$EPISODES" --workers "$WORKERS" --seed "$SEED" --rounds 40 \
      --base-port "$BP" --out "$ROOT/$b" > "$ROOT/$b.log" 2>&1 || echo "    FAILED (see $ROOT/$b.log)"
done

for entry in "${MODELS[@]}"; do
  IFS='|' read -r label model tp extra <<< "$entry"
  if [ -n "${MODELS_ONLY:-}" ] && [[ ",$MODELS_ONLY," != *",$label,"* ]]; then continue; fi
  echo "=== [model] $label ($model, tp=$tp${extra:+, $extra})  $(date +%H:%M)"
  ( export MODE=local MODEL="$model" TP="$tp" EPISODES WORKERS SEED ROUNDS=40 \
           BASE_PORT="$BP" OUT="$ROOT/$label" TAG="${TAG}_$label"
    for kv in $extra; do export "$kv"; done
    bash ops/benchmark_cluster.sbatch ) > "$ROOT/$label.log" 2>&1
  echo "    exit=$?  $(grep -E 'reward=|FATAL' "$ROOT/$label.log" | tail -1)"
  pkill -f "vllm serve" 2>/dev/null; sleep 15          # free the GPUs for the next model
done

echo "=== summary  $(date)"
$PY - "$ROOT" <<'PYEOF'
import collections, glob, json, os, statistics as st, sys
root = sys.argv[1]
rows = []
for d in sorted(os.listdir(root)):
    paths = glob.glob(os.path.join(root, d, "**", "episodes.jsonl"), recursive=True)
    eps = [json.loads(l) for p in paths for l in open(p) if l.strip()]
    if not eps:
        continue
    score = [e["rounds"][-1]["comps"]["score"] for e in eps if e.get("rounds")]
    budget = [e["rounds"][-1]["budget"] for e in eps if e.get("rounds")]
    calls = collections.Counter(c["status"] for e in eps for r in e.get("rounds", []) for c in r.get("calls", []))
    empty = sum(1 for e in eps for r in e.get("rounds", []) if not r.get("calls"))
    turns = sum(len(e.get("rounds", [])) for e in eps)
    full = sum(1 for e in eps if len(e.get("rounds", [])) == 29)
    n = len(score)
    rows.append((d, n, full, st.mean(score), st.pstdev(score) if n > 1 else 0.0, st.mean(budget),
                 calls["executed"] / n, calls["refused"] / n, calls["invalid"] / n, empty / max(turns, 1)))
print(f"{'arm':18s} {'n':>3s} {'full':>4s} {'score':>7s} {'sd':>6s} {'budget':>12s} {'exec/g':>7s} {'refu/g':>7s} {'inval/g':>7s} {'empty':>6s}")
for d, n, full, m, sd, b, ex, rf, iv, em in sorted(rows, key=lambda r: -r[3]):
    print(f"{d:18s} {n:3d} {full:4d} {m:7.4f} {sd:6.4f} {b:12,.0f} {ex:7.1f} {rf:7.1f} {iv:7.1f} {em:6.1%}")
PYEOF
echo "=== done $(date)"
