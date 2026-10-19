#!/bin/bash
# Benchmark one decision model (bench --policy decision) on the cluster: serve it on this node,
# play the sweep's seeds through the Linux build, and compare with the sweep's LLM and baseline arms.
#
#   MODEL_NAME=clef-flash CKPT=/zfsauton/scratch/$USER/models/decision/Cloudflare__clef-flash \
#     sbatch ops/benchmark_decision.sbatch
#   BACKEND=jev MODEL_NAME=jev-9b CKPT=/zfsauton/scratch/$USER/models/decision/autotrust__JEV-9B \
#     sbatch ops/benchmark_decision.sbatch      # vLLM + ops/serve_jev.py
#
# Defaults match the LLM arms of the 1.0.2 sweep (seeds 7000.., prompt minimal_v7), and every
# turn's record keeps each question's options and full answer probabilities (rounds[i].raw).
#
#SBATCH --job-name=cora_decision
#SBATCH --partition=general
#SBATCH --qos=qos_general
#SBATCH --gres=gpu:a6000:1
#SBATCH --cpus-per-task=16
#SBATCH --mem=64G
#SBATCH --time=4:00:00
#SBATCH --output=/zfsauton/scratch/%u/CORA/logs/bench/%x_%j.out
set -uo pipefail
ARC=${ARC_GAME_PATH:-/zfsauton/scratch/$USER/ARC_Game_ddm/ARC_Game_New}
cd "$ARC"
export ARC_GAME_PATH="$ARC" PYTHONUNBUFFERED=1 HF_HUB_OFFLINE=1
PY=${PY:-/zfsauton/scratch/$USER/conda_envs/vllm027/bin/python}
# The model server needs accelerate; the qwen35 env has it, with the torch (2.11) Clef was tested on.
SERVER_PY=${SERVER_PY:-/zfsauton/scratch/$USER/conda_envs/qwen35/bin/python}
MODEL_NAME=${MODEL_NAME:-clef-flash}
CKPT=${CKPT:-/zfsauton/scratch/$USER/models/decision/Cloudflare__clef-flash}
EPISODES=${EPISODES:-32}; SEED=${SEED:-7000}; WORKERS=${WORKERS:-8}; PROMPT=${PROMPT:-minimal_v7}
ENV=${ENV:-unity}                  # sim: play the surrogate (no Unity build needed)
SCHEMA=${SCHEMA:-v1}               # v2: one build question per free site (bench --decision-schema)
BACKEND=${BACKEND:-clef}           # clef: ops/serve_clef.py | jev: vLLM + ops/serve_jev.py | jev-hf: serve_jev.py --backend hf | llm: vLLM + ops/serve_llm_decision.py
BP=$(( 20000 + (${SLURM_JOB_ID:-0} % 180) * 64 )); PORT=$((BP + 900))
OUT=${OUT:-benchmark_results/decision_${MODEL_NAME}_${SLURM_JOB_ID:-local}}
mkdir -p "$OUT"
echo "=== $MODEL_NAME  job=${SLURM_JOB_ID:-local} node=$(hostname) $(git log --oneline -1) $(date)"
if [ "$BACKEND" = jev ]; then
  VPORT=$((BP + 901))
  $PY -m vllm.entrypoints.openai.api_server --model "$CKPT" --served-model-name "$MODEL_NAME" \
      --enable-lora --max-lora-rank 32 --lora-modules jev-decision="$CKPT/adapter_vllm" \
      --logprobs-mode processed_logprobs --max-model-len ${MAX_LEN:-16384} \
      --enable-prefix-caching --mamba-cache-mode align --port $VPORT > "$OUT/vllm.log" 2>&1 &
  VLLM=$!
  for i in $(seq 1 90); do
    curl -sf "http://127.0.0.1:$VPORT/health" >/dev/null && { echo "    vllm ready after ${i}0s"; break; }
    kill -0 $VLLM 2>/dev/null || { echo "FATAL: vllm died"; tail -30 "$OUT/vllm.log"; exit 1; }
    sleep 10
  done
  $PY ops/serve_jev.py "$CKPT" --vllm "http://127.0.0.1:$VPORT" --port $PORT --name "$MODEL_NAME" > "$OUT/server.log" 2>&1 &
elif [ "$BACKEND" = llm ]; then         # any chat LLM answering the typed questions (ops/serve_llm_decision.py)
  VPORT=$((BP + 901))                  # CKPT = the model id or path; 27B needs TP=2 and --gres=gpu:a6000:2
  export VLLM_USE_FLASHINFER_SAMPLER=0 VLLM_WORKER_MULTIPROC_METHOD=spawn
  $PY -m vllm.entrypoints.openai.api_server --model "$CKPT" --served-model-name "$CKPT" \
      --tensor-parallel-size ${TP:-1} --attention-backend TRITON_ATTN --gpu-memory-utilization 0.85 \
      --logprobs-mode processed_logprobs --max-logprobs 32 --max-model-len ${MAX_LEN:-16384} \
      --enable-prefix-caching --mamba-cache-mode align --port $VPORT > "$OUT/vllm.log" 2>&1 &
  VLLM=$!
  for i in $(seq 1 120); do
    curl -sf "http://127.0.0.1:$VPORT/health" >/dev/null && { echo "    vllm ready after ${i}0s"; break; }
    kill -0 $VLLM 2>/dev/null || { echo "FATAL: vllm died"; tail -30 "$OUT/vllm.log"; exit 1; }
    sleep 10
  done
  $PY ops/serve_llm_decision.py "$CKPT" --vllm "http://127.0.0.1:$VPORT" --port $PORT --name "$MODEL_NAME" \
      --state ${STATE_FORMAT:-compact} > "$OUT/server.log" 2>&1 &
elif [ "$BACKEND" = jev-hf ]; then       # JEV in-process (transformers + peft): no vLLM needed
  VLLM=
  $SERVER_PY ops/serve_jev.py "$CKPT" --backend hf --port $PORT --name "$MODEL_NAME" > "$OUT/server.log" 2>&1 &
else
  VLLM=
  $SERVER_PY ops/serve_clef.py "$CKPT" --port $PORT --name "$MODEL_NAME" ${CLEF_ARGS:-} > "$OUT/server.log" 2>&1 &
fi
SRV=$!
trap 'kill $SRV $VLLM 2>/dev/null' EXIT
for i in $(seq 1 90); do
  curl -sf "http://127.0.0.1:$PORT/health" >/dev/null && { echo "    server ready after ${i}0s"; break; }
  kill -0 $SRV 2>/dev/null || { echo "FATAL: server died"; tail -30 "$OUT/server.log"; exit 1; }
  sleep 10
done
$PY -u -m bench --policy decision --models "$MODEL_NAME" --base-url "http://127.0.0.1:$PORT" \
    --episodes "$EPISODES" --workers "$WORKERS" --seed "$SEED" --rounds 40 --base-port $BP --out "$OUT" \
    --obs_encoding compact --prompt "$PROMPT" --env "$ENV" --decision-schema "$SCHEMA" \
    > "$OUT/bench.log" 2>&1
echo "    bench exit=$?  server: $(curl -s http://127.0.0.1:$PORT/health)"
grep -E "Error|error" "$OUT/bench.log" | head -5
$PY - "$OUT" "${COMPARE:-}" <<'PYEOF'
import collections, glob, json, os, statistics as st, sys
def arm(paths):
    eps = [json.loads(l) for p in paths for l in open(p) if l.strip()]
    eps = [e for e in eps if e.get("rounds")]
    if not eps: return None
    sc = [e["rounds"][-1]["comps"]["score"] for e in eps]; n = len(eps)
    calls = collections.Counter(c["status"] for e in eps for r in e["rounds"] for c in r.get("calls", []))
    empty = sum(1 for e in eps for r in e["rounds"] if not r.get("calls")) / sum(len(e["rounds"]) for e in eps)
    return (n, sum(len(e["rounds"]) == 29 for e in eps), st.mean(sc), st.pstdev(sc), st.mean(e["rounds"][-1]["budget"] for e in eps),
            calls["executed"] / n, calls["refused"] / n, calls["invalid"] / n, empty)
rows = {os.path.basename(sys.argv[1].rstrip("/")): arm(glob.glob(sys.argv[1] + "/**/episodes.jsonl", recursive=True))}
if sys.argv[2]:
    for d in sorted(os.listdir(sys.argv[2])):
        r = arm(glob.glob(os.path.join(sys.argv[2], d, "**", "episodes.jsonl"), recursive=True))
        if r: rows[d] = r
print(f"{'arm':30s} {'n':>3s} {'full':>4s} {'score':>7s} {'sd':>6s} {'budget':>12s} {'exec/g':>7s} {'refu/g':>7s} {'inval/g':>7s} {'empty':>6s}")
for d, r in sorted(rows.items(), key=lambda kv: -(kv[1][2] if kv[1] else -9)):
    if r: print(f"{d:30s} {r[0]:3d} {r[1]:4d} {r[2]:7.4f} {r[3]:6.4f} {r[4]:12,.0f} {r[5]:7.1f} {r[6]:7.1f} {r[7]:7.1f} {r[8]:6.1%}")
PYEOF
echo "=== done $(date)"
