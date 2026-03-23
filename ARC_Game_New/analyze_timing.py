#!/usr/bin/env python3
"""
Timing Analysis for ARC Game Rollouts

Analyzes episode logs to extract performance metrics:
- LLM inference time per agent
- Simulator update time
- Total time per episode
- Per-turn breakdown

Usage:
    python analyze_timing.py rollout_openai_32/
"""

import json
import sys
import re
from pathlib import Path
from datetime import datetime
from typing import List, Dict, Tuple
from collections import defaultdict
import statistics


def parse_timestamp(ts_str: str) -> datetime:
    """Parse ISO timestamp from logs"""
    return datetime.fromisoformat(ts_str.replace('Z', '+00:00'))


def extract_llm_timing_from_router_log(log_path: Path) -> List[Dict]:
    """
    Extract LLM query timing from router logs.

    Looks for patterns like:
        [llm_query] [Agent Name] Querying OPENAI...
    And tracks time between query start and response parsing.
    """
    timings = []

    with open(log_path, 'r') as f:
        lines = f.readlines()

    current_query = None
    for i, line in enumerate(lines):
        # Detect LLM query start
        if '[llm_query]' in line and 'Querying' in line:
            match = re.search(r'\[(\w+ \w+)\] Querying (\w+)', line)
            if match:
                agent_name = match.group(1)
                provider = match.group(2)
                current_query = {
                    'agent': agent_name,
                    'provider': provider,
                    'start_line': i
                }

        # Detect response completion (when agent starts processing results)
        elif current_query and f"[{current_query['agent']}]" in line and 'LLM chose' in line:
            current_query['end_line'] = i
            # Estimate timing based on line positions (rough proxy)
            current_query['line_diff'] = i - current_query['start_line']
            timings.append(current_query)
            current_query = None

    return timings


def analyze_episode_jsonl(jsonl_path: Path) -> Dict:
    """
    Analyze timing from episode JSONL log.

    Returns:
        Dict with turn-by-turn timing breakdown
    """
    turns = []

    with open(jsonl_path, 'r') as f:
        for line in f:
            if line.strip():
                turns.append(json.loads(line))

    if not turns:
        return {'error': 'No turns found'}

    # Calculate inter-turn timing
    turn_timings = []
    for i in range(len(turns) - 1):
        t1 = parse_timestamp(turns[i]['timestamp'])
        t2 = parse_timestamp(turns[i + 1]['timestamp'])
        delta = (t2 - t1).total_seconds()

        turn_timings.append({
            'round': turns[i].get('round', 0),
            'agent': turns[i].get('agent_name', 'unknown'),
            'duration_seconds': delta,
            'tokens_used': turns[i].get('tokens_used', None)
        })

    # Overall episode timing
    start_time = parse_timestamp(turns[0]['timestamp'])
    end_time = parse_timestamp(turns[-1]['timestamp'])
    total_duration = (end_time - start_time).total_seconds()

    # Group by agent
    agent_timings = defaultdict(list)
    for t in turn_timings:
        agent_timings[t['agent']].append(t['duration_seconds'])

    return {
        'total_turns': len(turns),
        'total_duration_seconds': total_duration,
        'turn_timings': turn_timings,
        'agent_stats': {
            agent: {
                'count': len(times),
                'mean': statistics.mean(times) if times else 0,
                'stdev': statistics.stdev(times) if len(times) > 1 else 0,
                'min': min(times) if times else 0,
                'max': max(times) if times else 0,
            }
            for agent, times in agent_timings.items()
        }
    }


def analyze_rollout_directory(rollout_dir: Path) -> Dict:
    """
    Analyze all episodes in a rollout directory.

    Returns comprehensive timing statistics.
    """
    episode_files = sorted(rollout_dir.glob('episode_*.jsonl'))

    if not episode_files:
        return {'error': f'No episode files found in {rollout_dir}'}

    print(f"Found {len(episode_files)} episode logs")

    all_episodes = []
    episode_durations = []
    all_turn_durations = []
    agent_turn_times = defaultdict(list)

    for ep_file in episode_files:
        episode_num = ep_file.stem.replace('episode_', '')
        print(f"  Analyzing episode {episode_num}...")

        ep_analysis = analyze_episode_jsonl(ep_file)

        if 'error' in ep_analysis:
            print(f"    ⚠️  {ep_analysis['error']}")
            continue

        all_episodes.append({
            'episode': episode_num,
            **ep_analysis
        })

        episode_durations.append(ep_analysis['total_duration_seconds'])

        for turn_timing in ep_analysis['turn_timings']:
            all_turn_durations.append(turn_timing['duration_seconds'])
            agent_turn_times[turn_timing['agent']].append(turn_timing['duration_seconds'])

    # Calculate aggregate statistics
    result = {
        'rollout_summary': {
            'total_episodes': len(all_episodes),
            'total_rollout_time_seconds': sum(episode_durations),
            'total_rollout_time_hours': sum(episode_durations) / 3600,
        },
        'episode_stats': {
            'mean_duration_seconds': statistics.mean(episode_durations) if episode_durations else 0,
            'stdev_duration_seconds': statistics.stdev(episode_durations) if len(episode_durations) > 1 else 0,
            'min_duration_seconds': min(episode_durations) if episode_durations else 0,
            'max_duration_seconds': max(episode_durations) if episode_durations else 0,
            'mean_turns_per_episode': statistics.mean([ep['total_turns'] for ep in all_episodes]) if all_episodes else 0,
        },
        'turn_stats': {
            'total_turns_analyzed': len(all_turn_durations),
            'mean_turn_duration_seconds': statistics.mean(all_turn_durations) if all_turn_durations else 0,
            'stdev_turn_duration_seconds': statistics.stdev(all_turn_durations) if len(all_turn_durations) > 1 else 0,
            'min_turn_duration_seconds': min(all_turn_durations) if all_turn_durations else 0,
            'max_turn_duration_seconds': max(all_turn_durations) if all_turn_durations else 0,
        },
        'agent_stats': {
            agent: {
                'total_turns': len(times),
                'mean_turn_duration_seconds': statistics.mean(times),
                'stdev_turn_duration_seconds': statistics.stdev(times) if len(times) > 1 else 0,
                'min_turn_duration_seconds': min(times),
                'max_turn_duration_seconds': max(times),
            }
            for agent, times in agent_turn_times.items()
        },
        'episodes': all_episodes
    }

    return result


def print_summary(analysis: Dict):
    """Pretty print analysis summary"""
    print("\n" + "="*70)
    print("ROLLOUT TIMING ANALYSIS")
    print("="*70)

    if 'error' in analysis:
        print(f"ERROR: {analysis['error']}")
        return

    rs = analysis['rollout_summary']
    es = analysis['episode_stats']
    ts = analysis['turn_stats']

    print(f"\n📊 Rollout Summary:")
    print(f"  Total Episodes: {rs['total_episodes']}")
    print(f"  Total Time: {rs['total_rollout_time_seconds']:.1f}s ({rs['total_rollout_time_hours']:.2f} hours)")

    print(f"\n⏱️  Episode Statistics:")
    print(f"  Mean Duration: {es['mean_duration_seconds']:.2f}s ± {es['stdev_duration_seconds']:.2f}s")
    print(f"  Min/Max: {es['min_duration_seconds']:.2f}s / {es['max_duration_seconds']:.2f}s")
    print(f"  Mean Turns: {es['mean_turns_per_episode']:.1f}")

    print(f"\n🔄 Turn Statistics (across all episodes):")
    print(f"  Total Turns: {ts['total_turns_analyzed']}")
    print(f"  Mean Turn Duration: {ts['mean_turn_duration_seconds']:.3f}s ± {ts['stdev_turn_duration_seconds']:.3f}s")
    print(f"  Min/Max: {ts['min_turn_duration_seconds']:.3f}s / {ts['max_turn_duration_seconds']:.3f}s")

    print(f"\n👥 Per-Agent Turn Times:")
    for agent, stats in sorted(analysis['agent_stats'].items()):
        print(f"  {agent}:")
        print(f"    Turns: {stats['total_turns']}")
        print(f"    Mean: {stats['mean_turn_duration_seconds']:.3f}s ± {stats['stdev_turn_duration_seconds']:.3f}s")
        print(f"    Range: [{stats['min_turn_duration_seconds']:.3f}s, {stats['max_turn_duration_seconds']:.3f}s]")

    # Estimated breakdown (LLM vs simulator)
    # Assume LLM inference is ~80% of turn time for LLM agents
    print(f"\n🤖 Estimated LLM Inference Time (rough estimate):")
    total_turn_time = ts['mean_turn_duration_seconds']
    estimated_llm_time = total_turn_time * 0.7  # Rough estimate
    estimated_sim_time = total_turn_time * 0.3
    print(f"  ~LLM Inference: {estimated_llm_time:.3f}s per turn (~70%)")
    print(f"  ~Simulator + Overhead: {estimated_sim_time:.3f}s per turn (~30%)")
    print(f"  (Note: This is a rough estimate. Add explicit timing instrumentation for accuracy)")

    print("\n" + "="*70)
    print(f"💡 Optimization Insights:")

    if ts['mean_turn_duration_seconds'] > 5:
        print("  ⚠️  High turn latency (>5s). Consider:")
        print("     - Faster LLM model (e.g., Haiku vs Sonnet)")
        print("     - Parallel agent execution")
        print("     - Caching for repeated states")

    if es['mean_duration_seconds'] > 300:
        print("  ⚠️  Long episodes (>5min). For large rollouts, consider:")
        print("     - Increase parallel workers")
        print("     - Reduce max_turns_per_episode")
        print("     - Use faster hardware/cloud instances")

    total_projected_100 = (rs['total_rollout_time_seconds'] / rs['total_episodes']) * 100
    print(f"\n  📈 Projected time for 100 episodes: {total_projected_100/3600:.2f} hours")
    print(f"  📈 Projected time for 1000 episodes: {total_projected_100*10/3600:.2f} hours")

    print("="*70 + "\n")


def main():
    if len(sys.argv) < 2:
        print("Usage: python analyze_timing.py <rollout_directory>")
        sys.exit(1)

    rollout_dir = Path(sys.argv[1])

    if not rollout_dir.exists():
        print(f"Error: Directory not found: {rollout_dir}")
        sys.exit(1)

    analysis = analyze_rollout_directory(rollout_dir)
    print_summary(analysis)

    # Save detailed results to JSON
    output_file = rollout_dir / "timing_analysis.json"
    with open(output_file, 'w') as f:
        json.dump(analysis, f, indent=2)
    print(f"Detailed analysis saved to: {output_file}")


if __name__ == "__main__":
    main()
