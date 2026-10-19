#!/bin/bash
# Annotate a decision-model run (oracle.annotate): every option of every question the model was
# asked, scored by simulation from the exact state (mean final score and the chance of a top-10%
# run), next to the model's probability. One worker per game, every game in parallel.
#
#   export RUN=/zfsauton/scratch/$USER/CORA/bench_v7/results102_decision/jev-9b K=32
#   sbatch -J ann_jev-9b --export=ALL ops/annotate_cluster.sbatch
#
#SBATCH --job-name=cora_annotate
#SBATCH --partition=general
#SBATCH --qos=qos_general
#SBATCH --cpus-per-task=34
#SBATCH --mem=96G
#SBATCH --time=12:00:00
#SBATCH --output=/zfsauton/scratch/%u/CORA/logs/search/%x_%j.out
set -uo pipefail
ARC=${ARC_GAME_PATH:-$(pwd)}
cd "$ARC"
export PYTHONPATH="$ARC" PYTHONUNBUFFERED=1 PYTHONDONTWRITEBYTECODE=1
PY=${PY:-/zfsauton/scratch/$USER/conda_envs/vllm027/bin/python}
RUN=${RUN:?set RUN (a decision-model run directory holding episodes.jsonl)}
K=${K:-32}
EP=$(ls "$RUN"/episodes.jsonl "$RUN"/*/episodes.jsonl 2>/dev/null | head -1)
[ -n "$EP" ] || { echo "no episodes.jsonl under $RUN"; exit 1; }
mkdir -p "$RUN/annotate"
N=$(grep -c . "$EP")
echo "=== annotate $EP ($N games) K=$K on $(hostname) $(date)"
export PY EP K RUN
seq 0 $((N - 1)) | xargs -P "${SLURM_CPUS_PER_TASK:-32}" -I{} bash -c \
  '"$PY" -m oracle.annotate "$EP" --episode {} --k "$K" --out "$RUN/annotate/ann_{}.jsonl" 2> "$RUN/annotate/ann_{}.err"'
cat "$RUN"/annotate/ann_*.jsonl > "$RUN/annotations.jsonl"
echo "=== exit $(date): $(wc -l < "$RUN/annotations.jsonl") question rows"
