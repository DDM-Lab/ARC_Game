# analysis/

Maintained analysis and diagnostic scripts. Run them from the repo root with the project venv
(`.venv/bin/python analysis/<script> ...`).

| script | what it does |
|---|---|
| `diag_action_coverage.py` | the parity check: a scripted, seeded headless game that uses every tool (`--exe`, `--seed`, `--rounds`, `--out`); `--compare a.json b.json` checks two runs round by round. See docs/ARCHITECTURE.md, "Scenario and parity" |
| `view_transcript.py` | render a benchmark `episodes.jsonl` round by round (`--episode`, `--round`, `--errors`, `--obs`, `--prompt`) |
| `analyze_session.py` | split a router session log (`logs/sessions/<label>/<session>.jsonl`) into can't-execute, inert and effective officer actions, from the `outcome` fields the router stamps |
| `analyze_timing.py` | LLM and simulator timing per agent and turn, from a directory of `episode_*.jsonl` files; writes `timing_analysis.json` there |
