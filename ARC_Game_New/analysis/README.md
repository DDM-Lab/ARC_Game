# analysis/

Post-hoc analysis of benchmark output. Nothing here runs the game; these read
`benchmark_results/*/episodes.jsonl` and `arc_benchmarks/*/`.

| script | what it does |
|---|---|
| `view_transcript.py` | render one episode's rounds as readable text (`--prompt` shows the system prompt) |
| `analyze_session.py` | per-session summary of a run |
| `analyze_timing.py` | wall-clock / throughput breakdown |
| `action_trajectories.py` | action mix over rounds |
| `cluster_end_states.py` | end-of-episode state clustering across runs |
| `compare_rb_vs_v2_trajectories.py` | rule-based vs v2 trajectory diff |
| `_compare_agents.py`, `_rescore_convex.py` | ad-hoc comparisons (underscore = internal) |

Run them with the benchmark env:
`/zfsauton/scratch/cpulling/conda_envs/verlog/bin/python analysis/view_transcript.py ...`
