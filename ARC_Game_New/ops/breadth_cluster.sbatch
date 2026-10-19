#!/bin/bash
# Diverse-state training data (oracle.breadth): one behaviour POLICY over a block of seeds, every
# game in parallel; each game logs its states and every option of every question scored on K
# imagined worlds. Concatenated into $OUT/breadth.jsonl.gz.
#
#   export POLICY=eps0.5 SEED0=10000 NSEEDS=256 K=32 OUT=/zfsauton/scratch/$USER/CORA/breadth/eps0.5
#   sbatch -J breadth_eps0.5 --export=ALL ops/breadth_cluster.sbatch
#
#SBATCH --job-name=cora_breadth
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
POLICY=${POLICY:?set POLICY (rules | eps0.2 | eps0.5 | f7 | random | motel)}
SEED0=${SEED0:-10000}; NSEEDS=${NSEEDS:-256}; K=${K:-32}; EVERY=${EVERY:-1}
OUT=${OUT:?set OUT}
mkdir -p "$OUT/games"
echo "=== breadth $POLICY seeds $SEED0..$((SEED0 + NSEEDS - 1)) K=$K on $(hostname) $(date)"
export PY POLICY K EVERY OUT
seq "$SEED0" $(( SEED0 + NSEEDS - 1 )) | xargs -P "${SLURM_CPUS_PER_TASK:-32}" -I{} bash -c \
  '"$PY" -m oracle.breadth {} --policy "$POLICY" --k "$K" --every "$EVERY" --out "$OUT/games/b_{}.jsonl" \
     > "$OUT/games/b_{}.json" 2> "$OUT/games/b_{}.err"'
cat "$OUT"/games/b_*.jsonl | gzip > "$OUT/breadth.jsonl.gz"
cat "$OUT"/games/b_*.json > "$OUT/scores.jsonl"
echo "=== exit $(date): $(wc -l < "$OUT/scores.jsonl") games, $(zcat "$OUT/breadth.jsonl.gz" | wc -l) rows"
