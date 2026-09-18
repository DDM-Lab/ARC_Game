#!/usr/bin/env python3
"""
Test ARCGameGymEnv with Claude AI making decisions.

This script runs a 10-turn episode where Claude analyzes the game state
and selects actions strategically.
"""

import sys
import os
from pathlib import Path

# Add current directory to path
sys.path.insert(0, str(Path(__file__).parent))

from arc_game_gym_env_tcp import ARCGameGymEnv
import anthropic


def load_api_key():
    """Load Anthropic API key from .env file"""
    env_file = Path(__file__).parent / '.env'

    if 'ANTHROPIC_API_KEY' in os.environ:
        return os.environ['ANTHROPIC_API_KEY']

    if env_file.exists():
        with open(env_file, 'r') as f:
            for line in f:
                if line.startswith('ANTHROPIC_API_KEY='):
                    key = line.strip().split('=', 1)[1].strip('"\'')
                    os.environ['ANTHROPIC_API_KEY'] = key
                    return key

    raise ValueError("ANTHROPIC_API_KEY not found in environment or .env file")


def format_game_state_for_claude(env, info):
    """Format game state into a clear prompt for Claude"""

    lines = []
    lines.append("=== DISASTER RESPONSE GAME ===\n")

    # Current status
    lines.append(f"Day {info['day']}, Segment {info['segment']}")
    lines.append(f"Satisfaction: {info['satisfaction']:.1f}/100")
    lines.append(f"Budget: ${info['budget']:,.0f}\n")

    # Workforce info
    if env.game_state and 'workforceState' in env.game_state:
        ws = env.game_state['workforceState']
        lines.append("WORKFORCE:")
        lines.append(f"  Trained: {ws.get('freeTrainedWorkers', 0)} free, {ws.get('workingTrainedWorkers', 0)} working")
        lines.append(f"  Untrained: {ws.get('freeUntrainedWorkers', 0)} free, {ws.get('workingUntrainedWorkers', 0)} working\n")

    # Facilities
    if env.game_state and 'mapState' in env.game_state:
        facilities = env.game_state['mapState'].get('facilities', [])
        if facilities:
            lines.append(f"FACILITIES ({len(facilities)}):")
            for fac in facilities[:5]:
                name = fac.get('facilityName', 'Unknown')
                status = fac.get('buildingStatus', 'Unknown')
                workers = fac.get('assignedWorkforce', 0)
                required = fac.get('requiredWorkforce', 4)
                lines.append(f"  {name}: {workers}/{required} workers ({status})")
            if len(facilities) > 5:
                lines.append(f"  ... and {len(facilities) - 5} more")
            lines.append("")

    # Available actions (limit to first 15 for token efficiency). Shown as plain
    # descriptions — the policy composes command tags from the grammar, not indices.
    lines.append(f"AVAILABLE ACTIONS (showing 15 of {len(env.valid_actions)}):")
    for action in env.valid_actions[:15]:
        desc = action.get('description', 'Unknown')
        cost = action.get('cost', 0)
        lines.append(f"  - {desc} (Cost: ${cost:,})")

    if len(env.valid_actions) > 15:
        lines.append(f"  ... and {len(env.valid_actions) - 15} more actions")

    return "\n".join(lines)


def query_claude(prompt, api_key, step_num):
    """Query Claude for action decision"""

    client = anthropic.Anthropic(api_key=api_key)

    system_prompt = """You are an expert disaster response manager playing a city management game.

Your goal is to maximize satisfaction while managing a limited budget.

Key strategies:
- Build shelters and kitchens early (prevent satisfaction loss)
- Hire workers to staff buildings (buildings need 4+ workforce value to operate)
- Train workers for better efficiency (trained = 2 workforce value, untrained = 1)
- Balance budget - don't overspend early
- Prioritize actions that address immediate needs

Respond with one or more COMMAND TAGS, one per line, choosing from this grammar:
  <build>TYPE,SITE</build>          e.g. <build>Kitchen,1</build>  (TYPE: Kitchen|Shelter|CaseworkSite)
  <hire>KIND,N</hire>               e.g. <hire>untrained,4</hire>  (KIND: untrained|trained)
  <train>N</train>                  e.g. <train>2</train>
  <staff>BUILDING,N</staff>         e.g. <staff>Kitchen Alpha,2</staff>
  <deconstruct>NAME</deconstruct>   e.g. <deconstruct>Kitchen Alpha</deconstruct>
  <transfer>food|people,SRC,DST,N</transfer>
  <task>TOKEN,CHOICE</task>         answer a listed task
Emit ONLY tags (no prose). To do nothing this round, respond with an empty line."""

    try:
        print(f"\n🤖 Querying Claude (step {step_num})...")

        message = client.messages.create(
            model="claude-sonnet-4-5-20250929",
            max_tokens=200,
            temperature=0.7,
            system=system_prompt,
            messages=[{"role": "user", "content": prompt}]
        )

        response = message.content[0].text.strip()
        print(f"   Claude's response: '{response}'")
        # The response IS the action: the shared parser resolves the tags against
        # the round's menu (unresolved tags no-op and surface in info["parse_errors"]).
        return response

    except Exception as e:
        print(f"   ❌ Claude API error: {e}")
        print(f"   Defaulting to no-op this round")
        return ""


def main():
    print("=" * 80)
    print("ARCGAMEGYMENV + CLAUDE AI TEST")
    print("=" * 80)
    print("\nThis test runs a 10-turn episode with Claude making strategic decisions\n")

    # Load API key
    try:
        api_key = load_api_key()
        print(f"✅ API key loaded: {api_key[:20]}...\n")
    except ValueError as e:
        print(f"❌ {e}")
        print("\nPlease set ANTHROPIC_API_KEY in .env file or environment")
        sys.exit(1)

    # Create environment
    print("=" * 80)
    print("CREATING ENVIRONMENT")
    print("=" * 80)
    print("\n🔌 Connecting to Unity on port 9876...")
    print("   (Unity must be running!)\n")

    try:
        env = ARCGameGymEnv(
            unity_exe_path=None,  # Connect to existing Unity instance
            unity_port=9876,
            max_episode_steps=10,
            render_mode="human",
            auto_start_unity=False,
            connection_timeout=30.0
        )
        print("✅ Connected to Unity successfully!\n")

    except ConnectionError as e:
        print(f"❌ Failed to connect to Unity: {e}\n")
        print("Please start Unity first:")
        print("  1. Open Unity GUI and press Play, OR")
        print("  2. Run: ./Build/Headless/Windows/ARC_Headless.exe -websocket-port 9876\n")
        sys.exit(1)

    except Exception as e:
        print(f"❌ Error creating environment: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)

    # Run episode
    print("=" * 80)
    print("STARTING 10-TURN EPISODE WITH CLAUDE")
    print("=" * 80)

    try:
        # Reset
        obs, info = env.reset()
        print(f"\n✅ Episode started")
        print(f"   Initial satisfaction: {info['satisfaction']:.1f}/100")
        print(f"   Initial budget: ${info['budget']:,.0f}")
        print(f"   Available actions: {info['valid_action_count']}")

        env.render()

        total_reward = 0
        step = 0
        terminated = False
        truncated = False

        # Run episode
        while not (terminated or truncated):
            step += 1

            print(f"\n{'─' * 80}")
            print(f"STEP {step}/10")
            print(f"{'─' * 80}")

            # Format prompt for Claude
            prompt = format_game_state_for_claude(env, info)
            print(f"\n📋 Current State:")
            print(f"   Satisfaction: {info['satisfaction']:.1f}/100")
            print(f"   Budget: ${info['budget']:,.0f}")
            print(f"   Valid actions: {len(env.valid_actions)}")

            # Get Claude's decision: a command-tag string (the env parses it).
            action_tags = query_claude(prompt, api_key, step)
            print(f"\n➡️  Submitting command tags:\n{action_tags or '   (empty — no-op)'}")

            # Execute the tags. Resolution + no-op-on-unresolved happens in step().
            obs, reward, terminated, truncated, info = env.step(action_tags)
            total_reward += reward
            if info.get('parse_errors'):
                print(f"   ⚠️  Unresolved tags: {info['parse_errors']}")

            # Show results
            print(f"\n📊 Step Results:")
            print(f"   Reward: {reward:+.1f}")
            print(f"   Total Reward: {total_reward:+.1f}")
            print(f"   Satisfaction: {info['satisfaction']:.1f}/100")
            print(f"   Budget: ${info['budget']:,.0f}")

            if info.get('executed_actions'):
                print(f"   Executed: {info['executed_actions']}")

            if terminated:
                print(f"\n🏁 Episode TERMINATED (satisfaction <= 0)")
            if truncated:
                print(f"\n⏱️  Episode TRUNCATED (max steps reached)")

            env.render()

        # Summary
        print("\n" + "=" * 80)
        print("EPISODE COMPLETE")
        print("=" * 80)
        print(f"✅ Total Steps: {step}")
        print(f"✅ Total Reward: {total_reward:+.1f}")
        print(f"✅ Final Satisfaction: {info['satisfaction']:.1f}/100")
        print(f"✅ Final Budget: ${info['budget']:,.0f}")

        if terminated:
            print(f"✅ Termination: Game Over (satisfaction <= 0)")
        elif truncated:
            print(f"✅ Termination: Max Steps Reached")

        # Verify
        if step == 10:
            print(f"\n🎉 SUCCESS: Episode ran for exactly 10 steps as configured")
        elif step < 10 and terminated:
            print(f"\n🎉 SUCCESS: Episode terminated early (game over at step {step})")
        else:
            print(f"\n⚠️  WARNING: Unexpected termination (step {step})")

        env.close()

        print("\n" + "=" * 80)
        print("✅ TEST COMPLETED SUCCESSFULLY")
        print("=" * 80)
        print("\nClaude successfully played the game using ARCGameGymEnv!")

    except KeyboardInterrupt:
        print("\n\n⚠️  Test interrupted by user")
        env.close()

    except Exception as e:
        print(f"\n❌ Error during episode: {e}")
        import traceback
        traceback.print_exc()
        try:
            env.close()
        except:
            pass
        sys.exit(1)


if __name__ == "__main__":
    main()
