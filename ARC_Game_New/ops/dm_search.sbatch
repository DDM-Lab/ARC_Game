#!/bin/bash
# Serve one decision model on this node, then run a command against it with URL set to the
# server: the leaf-value probe (oracle.dm_leaf) or searches with the model at the leaf
# (oracle.search --leaf model).
#
#   BACKEND=jev-hf CKPT=/zfsauton/scratch/$USER/models/decision/autotrust__JEV-9B OUT=... \
#     CMD='$PY -m oracle.dm_leaf probe $DATA --url $URL --out $OUT/probe.jsonl' sbatch ops/dm_search.sbatch
#
#SBATCH --job-name=cora_dm_search
#SBATCH --partition=general
#SBATCH --qos=qos_general
#SBATCH --gres=gpu:a6000:1
#SBATCH --cpus-per-task=16
#SBATCH --mem=64G
#SBATCH --time=12:00:00
#SBATCH --output=/zfsauton/scratch/%u/CORA/logs/search/%x_%j.out
set -uo pipefail
ARC=${ARC_GAME_PATH:-$(pwd)}
cd "$ARC"
export ARC_GAME_PATH="$ARC" PYTHONPATH="$ARC" PYTHONUNBUFFERED=1 PYTHONDONTWRITEBYTECODE=1 HF_HUB_OFFLINE=1
export PY=${PY:-/zfsauton/scratch/$USER/conda_envs/vllm027/bin/python}
SERVER_PY=${SERVER_PY:-/zfsauton/scratch/$USER/conda_envs/qwen35/bin/python}
BACKEND=${BACKEND:-jev-hf}          # jev-hf: ops/serve_jev.py --backend hf | clef: ops/serve_clef.py | llm: vLLM + ops/serve_llm_decision.py
CKPT=${CKPT:?set CKPT (a decision-model checkpoint)}
OUT=${OUT:?set OUT (an output directory)}
CMD=${CMD:?set CMD (run with URL pointing at the server)}
mkdir -p "$OUT"
PORT=$(( 20000 + (${SLURM_JOB_ID:-0} % 180) * 64 + 900 ))
export URL="http://127.0.0.1:$PORT" OUT
echo "=== $BACKEND $CKPT job=${SLURM_JOB_ID:-local} node=$(hostname) $(date)"
VLLM=
if [ "$BACKEND" = clef ]; then
  $SERVER_PY ops/serve_clef.py "$CKPT" --port $PORT > "$OUT/server.log" 2>&1 &
elif [ "$BACKEND" = llm ]; then        # CKPT = an LLM id or path (27B: TP=2, --gres=gpu:a6000:2)
  VPORT=$((PORT + 1))
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
  $PY ops/serve_llm_decision.py "$CKPT" --vllm "http://127.0.0.1:$VPORT" --port $PORT \
      --state ${STATE_FORMAT:-compact} > "$OUT/server.log" 2>&1 &
else
  $SERVER_PY ops/serve_jev.py "$CKPT" --backend hf --port $PORT > "$OUT/server.log" 2>&1 &
fi
SRV=$!
trap 'kill $SRV $VLLM 2>/dev/null' EXIT
for i in $(seq 1 90); do
  curl -sf "$URL/health" >/dev/null && { echo "    server ready after ${i}0s"; break; }
  kill -0 $SRV 2>/dev/null || { echo "FATAL: server died"; tail -30 "$OUT/server.log"; exit 1; }
  sleep 10
done
echo "    $CMD"
eval "$CMD"
echo "=== exit $? $(date)  server: $(curl -s $URL/health)"
