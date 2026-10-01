"""The benchmark: LLMs and baseline policies playing full games through the shared core (cora/).

    cli.py         python -m bench — runs games, writes episodes.jsonl + summary.json
    episode.py     one game: fresh Unity process, one decision per decision point, the record
    llm.py         the LLM policy (system prompt + tool schema + observation -> tool calls)
    baselines/     non-learning policies: greedy, build-potential, combined, pareto
    results.py     aggregates, console table, WandB logging
    images.py      decision-time images for the vision arms (dashboard_render.py, export_map_grid.py)
    plots.py, compare.py, export_sft.py, probe.py   analysis of results and diagnostics
"""
