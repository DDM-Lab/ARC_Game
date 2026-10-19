#!/bin/bash
# Search agents (oracle.search) on the cluster: one (ALGO, INFO, BUDGET) setting over a block of
# seeds, every seed in parallel. Each worker plays the game on the surrogate with the agent
# choosing every move, writes the realized plan, then replays that plan on the headless Unity
# build (oracle.sim.capture) -- the score reported is Unity's, the surrogate's is kept beside it.
#
# Export the settings, then submit with a plain --export=ALL (on this cluster
# `--export=ALL,VAR=x` makes Slurm rebuild the user environment on the node, which fails and
# leaves the job held):
#   export ALGO=mcts INFO=fair BUDGET=4000 OUT=/zfsauton/scratch/$USER/CORA/search/mcts_fair_4000
#   sbatch -J search_mcts_fair_4000 --export=ALL ops/search_cluster.sbatch
#
#SBATCH --job-name=cora_search
#SBATCH --partition=general
#SBATCH --qos=qos_general
#SBATCH --cpus-per-task=34
#SBATCH --mem=120G
#SBATCH --time=12:00:00
#SBATCH --output=/zfsauton/scratch/%u/CORA/logs/search/%x_%j.out
set -uo pipefail
ARC=${ARC_GAME_PATH:-$(pwd)}
cd "$ARC"
export ARC_GAME_PATH="$ARC" PYTHONUNBUFFERED=1 PYTHONDONTWRITEBYTECODE=1 PYTHONPATH="$ARC"
export ARC_HEADLESS_EXE="${ARC_HEADLESS_EXE:-$ARC/Build/Headless/Linux/ARC_Headless.x86_64}"
CONDA_ENV=${CONDA_ENV-/zfsauton/scratch/$USER/conda_envs/vllm027}
PY=${PY:-$CONDA_ENV/bin/python}
ALGO=${ALGO:?set ALGO (mcts | rhea | rules)}
INFO=${INFO:-fair}
BUDGET=${BUDGET:-1000}
SEED0=${SEED0:-7000}
NSEEDS=${NSEEDS:-32}
SEARCH_SEED=${SEARCH_SEED:-0}
SEARCH_ARGS=${SEARCH_ARGS:-}    # extra oracle.search flags, e.g. "--crn 32 --reuse"
LOGDATA=${LOGDATA:-}            # non-empty: also write training data (data_<seed>.jsonl.gz)
REPLAY=${REPLAY:-1}            # 0: surrogate score only, no Unity replay of the plan
TREELOG=${TREELOG:-}            # non-empty: also write each decision's tree coverage (tree_<seed>.jsonl)
OUT=${OUT:?set OUT}
mkdir -p "$OUT" "/zfsauton/scratch/$USER/CORA/logs/search"
# Unity ports in 10000-19999, clear of the benchmark jobs' 20000-32767 block (its vLLM takes
# BASE_PORT+900 there): a shared node once handed a benchmark's vLLM an "Address already in use".
BASE_PORT=$(( 10000 + (${SLURM_JOB_ID:-0} % 100) * 96 ))
echo "=== $ALGO $INFO budget=$BUDGET seeds $SEED0..$((SEED0 + NSEEDS - 1)) on $(hostname) $(date)"

one() {
  s=$1
  i=$(( s - SEED0 ))
  "$PY" -m oracle.search "$s" --algo "$ALGO" --info "$INFO" --budget "$BUDGET" \
      --search-seed "$SEARCH_SEED" --plan "$OUT/plan_$s.json" --quiet \
      ${LOGDATA:+--log-data "$OUT/data_$s.jsonl.gz"} ${TREELOG:+--tree-log "$OUT/tree_$s.jsonl"} $SEARCH_ARGS > "$OUT/search_$s.json" 2> "$OUT/search_$s.err" || return
  [ "$REPLAY" = "0" ] && { cp "$OUT/search_$s.json" "$OUT/result_$s.json"; return; }
  "$PY" -m oracle.sim.capture "$s" --calls "$OUT/plan_$s.json" --out "$OUT/replay_$s" \
      --port $(( BASE_PORT + i * 3 )) > "$OUT/replay_$s.log" 2>&1
  "$PY" - "$OUT" "$s" <<'EOF'
import json, sys
from cora.scoring import score_components
out, s = sys.argv[1], sys.argv[2]
r = json.load(open(f"{out}/search_{s}.json"))
try:
    trace = json.load(open(f"{out}/replay_{s}/staff_{s}.json"))
    r["unity_score"] = round(score_components(trace[-1]["after"].get("rewardMetrics"))["score"], 4)
    # Calls Unity refused or rejected that the surrogate executed: the planner's model error.
    want = [c for st in trace if st.get("decision") is not None for c in (st.get("calls") or [])]
    r["unity_not_executed"] = sum(1 for c in want if c.get("status") != "executed")
except Exception as e:
    r["unity_error"] = f"{type(e).__name__}: {e}"
open(f"{out}/result_{s}.json", "w").write(json.dumps(r) + "\n")
EOF
}
export -f one
export PY ALGO INFO BUDGET SEARCH_SEED OUT SEED0 BASE_PORT LOGDATA TREELOG SEARCH_ARGS REPLAY
seq "$SEED0" $(( SEED0 + NSEEDS - 1 )) | xargs -P "${SLURM_CPUS_PER_TASK:-32}" -I{} bash -c 'one {}'
cat "$OUT"/result_*.json > "$OUT/results.jsonl" 2>/dev/null
echo "=== exit $(date): $(wc -l < "$OUT/results.jsonl") results"
