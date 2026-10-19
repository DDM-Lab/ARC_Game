---
name: cora-rl-training
description: Train an LLM on the CORA game with RL (PPO via the Verlog/veRL fork) on the Auton cluster — on the Python surrogate or the Unity build — including the settings that fit a 4B model on 4xA6000, resuming, and diagnosing failures. Use when asked to run, resume, check or debug CORA RL training.
---

# RL on CORA

The environment is `rl.cora_env.CoraEnv` in this repo: one step is one decision (the policy sees
the system prompt, the tools and the turn's observation, and answers with tool calls), and the
reward is the change in score, so an episode's rewards sum to its final score. The trainer is
the Verlog fork of veRL (separate repo, branch `release/cora-rl-v1`), whose adapter
`verl/envs/environments/arc_game/cora_adapter.py` passes `envs.arc_game_kwargs` straight into
`CoraEnvConfig`.

## Run it

From the Verlog checkout on the cluster:

```bash
export REPO_DIR=$PWD ARC_GAME_PATH=/path/to/ARC_Game_New ARC_BACKEND=sim \
       ACTOR_USE_DYNAMIC_BSZ=True TOTAL_TRAINING_STEPS=60 EXPERIMENT_NAME=qwen3_4b_sim_ppo60
sbatch --job-name=rl_ppo60 --output=logs/slurm-%j.out --error=logs/slurm-%j.err \
  train_arc_game_qwen3_4B_tools_v3.sbatch actor_rollout_ref.model.use_fused_kernels=True
```

- `ARC_BACKEND=sim` plays the surrogate (`oracle.sim.SimEnv`): no Unity process, no ports, and
  games that match Unity 1.0.2 exactly (see the `cora-surrogate-parity` skill). `unity` (default) needs
  `$ARC_GAME_PATH/Build/Headless/Linux/ARC_Headless.x86_64`. Set the backend only through
  `ARC_BACKEND`: the sbatch already passes `++envs.arc_game_kwargs.backend`, so an extra
  `+envs.arc_game_kwargs.backend=sim` on the command line clashes with it.
- Before any GPU job, check the environment on a CPU (no GPU, no Unity): whole games of the rules
  baseline through the trainer's own env path, with the rewards checked against the final score.

  ```bash
  ARC_GAME_PATH=/path/to/ARC_Game_New python smoke_arc_game_env.py --backend sim --policy rules --episodes 3
  ```

  Each game should print `turns=29 ... OK`.
- Then a GPU smoke test before a long run: `TOTAL_TRAINING_STEPS=2 CRITIC_WARMUP=0 SAVE_FREQ=-1
  PPO_LOGGER='["console"]'` (about 80 min including startup).
- Submit with plain exported variables. `sbatch --export=ALL,VAR=x` leaves the job held on
  this cluster.

## What a step costs (Qwen3-4B, 16 games x 29 decisions = 464 rows, 4xA6000)

| Phase | Time |
|---|---|
| rollout generation | ~2.7 min |
| critic update | ~15 min |
| actor update (after the critic warmup, step 11+) | ~15 min |

Steps 1-10 are critic warmup (default `CRITIC_WARMUP=10`): the actor is frozen, so the score
moving there is noise. Plan ~22 min a step during warmup and ~37 min after: 60 steps is about
35 h, so a run passes the 24 h wall once.

## Settings that are load-bearing

- **Actor packing at 14k tokens needs fused kernels.** `ACTOR_USE_DYNAMIC_BSZ=True` without
  `actor_rollout_ref.model.use_fused_kernels=True` ran out of memory in the actor update
  (full-vocabulary logits); with it the peak was 47 GB.
- **`PYTORCH_CUDA_ALLOC_CONF` must not enable expandable segments**: vLLM's sleep mode asserts
  "Expandable segments are not compatible with memory pool" and the job hangs until its time
  limit. The sbatch defaults to `expandable_segments:False`.
- `NCCL_P2P_DISABLE=1` (default) and Triton attention for the rollout (FlashAttention with
  prefix caching corrupts batched decoding on vLLM 0.10) are explained in the sbatch.

## Resuming

Resubmit with the same `EXPERIMENT_NAME`; it resumes from
`checkpoints/CORA_RL/$EXPERIMENT_NAME/latest_checkpointed_iteration.txt`. A checkpoint (actor +
critic + optimizer state) is ~53 GB and grows once the actor's optimizer exists; two are kept
(`MAX_CKPT_TO_KEEP`, saved every `SAVE_FREQ` steps).

## When a run dies

1. `sacct -j <id> -o JobID,State,Elapsed,ExitCode,MaxRSS` and the tail of `logs/slurm-<id>.out`.
2. The Ray worker logs hold the real traceback, on the compute node's local disk:
   `srun -w <node> -c1 --mem=1G -t 5 bash -c 'ls /tmp/ray_cora_qwen3_4b_<id>/session_*/logs'`,
   then grep `worker-*.err` for `Traceback|RuntimeError`.
3. `torch.save ... unexpected pos N vs M` while writing a checkpoint is a failed write: check
   `df -h /zfsauton/scratch` (the shared pool runs ~97% full). Delete the half-written
   `global_step_*` (0-byte optimizer files), free space, and resubmit to resume from the last
   complete checkpoint.
