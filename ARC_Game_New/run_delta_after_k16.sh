#!/usr/bin/env bash
cd "$(dirname "$0")"
echo "[chain] waiting for K=16 run to complete before starting delta..."
until grep -q "K=16 A/B COMPLETE" bench_format_ablation/k16_run.log 2>/dev/null; do sleep 30; done
echo "[chain] K=16 done -> launching delta K=32"
./run_delta_k32_ab.sh
