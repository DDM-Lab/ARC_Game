# ARCGameGymEnv Quick Start Guide

## 🚀 Get Started in 5 Minutes

### Step 1: Verify Installation ✅

```bash
cd /mnt/c/Users/cwill/ARC_Game/ARC_Game/ARC_Game_New
python3 -c "from arc_game_gym_env import ARCGameGymEnv; print('✅ Ready to use!')"
```

**Expected output**: `✅ Ready to use!`

### Step 2: Start Unity

**Option A - GUI Mode** (easiest for testing):
1. Open Unity project
2. Press Play
3. Unity will listen on WebSocket port 9876

**Option B - Headless Mode** (for training):
```bash
./Build/Headless/Windows/ARC_Headless.exe -websocket-port 9876
```

### Step 3: Run Test Episode

```bash
python3 test_arc_game_gym_env.py
```

**Expected output**:
```
✅ Episode ran for exactly 10 steps as configured
Total reward: [varies]
```

---

## 📝 Minimal Working Example

```python
from arc_game_gym_env import ARCGameGymEnv

# Connect to Unity (must be running on port 9876)
env = ARCGameGymEnv(unity_port=9876, max_episode_steps=10)

# Reset environment
obs, info = env.reset()
print(f"Starting satisfaction: {info['satisfaction']:.1f}")

# Run 10 steps
total_reward = 0
for step in range(10):
    # Select action 0 (first valid action)
    obs, reward, terminated, truncated, info = env.step("0")
    total_reward += reward

    print(f"Step {step+1}: reward={reward:+.1f}, satisfaction={info['satisfaction']:.1f}")

    if terminated or truncated:
        break

print(f"\nTotal reward: {total_reward:+.1f}")
env.close()
```

**Save as**: `my_first_episode.py`

**Run**:
```bash
python3 my_first_episode.py
```

---

## 🎯 Common Use Cases

### Use Case 1: Random Action Selection

```python
import random
from arc_game_gym_env import ARCGameGymEnv

env = ARCGameGymEnv(unity_port=9876, max_episode_steps=50)
obs, info = env.reset()

for step in range(50):
    # Pick random action
    action_idx = random.randint(0, len(env.valid_actions) - 1)
    obs, reward, terminated, truncated, info = env.step(str(action_idx))

    if terminated or truncated:
        break

env.close()
```

### Use Case 2: Greedy High-Value Actions

```python
from arc_game_gym_env import ARCGameGymEnv

env = ARCGameGymEnv(unity_port=9876, max_episode_steps=50)
obs, info = env.reset()

for step in range(50):
    # Find cheapest action we can afford
    affordable = [
        (i, a) for i, a in enumerate(env.valid_actions)
        if a['cost'] <= info['budget']
    ]

    if affordable:
        action_idx = min(affordable, key=lambda x: x[1]['cost'])[0]
        obs, reward, terminated, truncated, info = env.step(str(action_idx))
    else:
        print("No affordable actions!")
        break

    if terminated or truncated:
        break

env.close()
```

### Use Case 3: With Verlog/LLM Integration

```python
from arc_game_gym_env import ARCGameGymEnv
import anthropic

env = ARCGameGymEnv(unity_port=9876, max_episode_steps=10)
client = anthropic.Anthropic(api_key="your-api-key")

obs, info = env.reset()

for step in range(10):
    # Format prompt with action descriptions
    prompt = f"""
You are playing a disaster response game.

Current State:
- Satisfaction: {info['satisfaction']:.1f}/100
- Budget: ${info['budget']:,.0f}

Available Actions:
{chr(10).join(env.get_action_descriptions()[:20])}

Choose ONE action by responding with just the number.
"""

    # Query LLM
    response = client.messages.create(
        model="claude-sonnet-4-5-20250929",
        max_tokens=10,
        messages=[{"role": "user", "content": prompt}]
    )

    action = response.content[0].text.strip()

    # Execute
    obs, reward, terminated, truncated, info = env.step(action)

    if terminated or truncated:
        break

env.close()
```

---

## 🔧 Troubleshooting

### Problem: "ModuleNotFoundError: No module named 'websocket'"

**Solution**:
```bash
pip3 install websocket-client
```

### Problem: "Failed to connect to Unity WebSocket"

**Check**:
1. Is Unity running? Check Task Manager
2. Is Unity listening on port 9876? Check Unity logs
3. Firewall blocking? Temporarily disable and test

**Solution**:
```bash
# Start Unity manually first
./Build/Headless/Windows/ARC_Headless.exe -websocket-port 9876

# In another terminal
python3 test_arc_game_gym_env.py
```

### Problem: "Invalid action index: 999"

**Explanation**: Action index exceeds valid range

**Solution**:
```python
# Always check valid range
print(f"Valid actions: 0 to {len(env.valid_actions)-1}")

# Ensure action is in range
action_idx = min(action_idx, len(env.valid_actions)-1)
```

### Problem: Episode terminates immediately

**Explanation**: Satisfaction <= 0 (game over)

**Check**:
```python
obs, info = env.reset()
print(f"Starting satisfaction: {info['satisfaction']}")  # Should be > 0
```

---

## 📚 Next Steps

1. **Read Full Documentation**: `ARC_GAME_GYM_ENV_README.md`
2. **See Implementation Details**: `IMPLEMENTATION_SUMMARY.md`
3. **Integrate with Training Framework**: Verlog, Ray, or Stable-Baselines3
4. **Run Distributed Training**: Multiple Unity instances on cluster

---

## 🎓 Key Concepts

**Observation**: Full game state dictionary from Unity

**Action**: String of action indexes (e.g., "5" or "5,12,3")

**Reward**: Satisfaction delta (current - previous)

**Terminated**: Satisfaction <= 0 (game over)

**Truncated**: Step >= max_episode_steps (time limit)

---

## ✅ Verification Checklist

Before reporting issues, verify:

- [ ] `websocket-client` installed (`pip3 install websocket-client`)
- [ ] Unity running (GUI or headless build)
- [ ] Unity listening on correct port (default 9876)
- [ ] Import works (`from arc_game_gym_env import ARCGameGymEnv`)
- [ ] Test script exists (`test_arc_game_gym_env.py`)

---

**Ready to train your RL agent on ARC Game! 🚀**

For questions, see:
- `ARC_GAME_GYM_ENV_README.md` - Full API reference
- `IMPLEMENTATION_SUMMARY.md` - Technical details
- Original summary conversation logs
