# Gym Environment Testing Guide

## Quick Start - 3 Easy Steps

### Step 1: Start Unity with Gym Server
1. Open Unity Editor
2. Open `MainScene.unity`
3. Click **Play** ▶️
4. Look for this in Console:
   ```
   [GymServerInitializer] Created GymServerManager instance
   [GymServer] ✅ Gym server listening on port 9876
   ```

**That's it!** The `GymServerInitializer` script automatically creates and configures the server.

---

### Step 2: Test Connection
```bash
cd /mnt/c/Users/cwill/ARC_Game/ARC_Game/ARC_Game_New
python3 test_connection.py
```

**Expected Output:**
```
🔌 Testing connection to localhost:9876...
✅ Connected!
📤 Sent: get_game_state
📥 Response type: game_state

✅ Game State:
   Day: 1
   Segment: 1
   Satisfaction: 50
   Budget: $10,000

🎉 SUCCESS! Unity GymServer is working.
```

---

### Step 3: Run Full Test with Claude (Optional)
```bash
# Make sure .env has ANTHROPIC_API_KEY
python3 test_gym_env_with_claude.py
```

This runs a 10-turn episode with Claude making decisions.

---

## Troubleshooting

### ❌ "Connection refused"
**Problem:** Unity isn't running or GymServer isn't started

**Solution:**
1. Make sure Unity is in Play mode
2. Check Console for `[GymServer] ✅ Gym server listening on port 9876`
3. If you see `[GymServer] WebSocketManager is active, gym server disabled by default`:
   - Either disable WebSocketManager
   - Or run Unity with: `-gym-server` flag to force enable

---

### ❌ "Port already in use"
**Problem:** Something else is using port 9876

**Solution:**
```bash
# Check what's using the port
netstat -an | grep 9876

# Kill old Unity processes
pkill -f Unity
```

---

### ⚠️ No console message about GymServer
**Problem:** GymServerInitializer might not be running

**Solution:**
1. Check `Assets/Scripts/GymServerInitializer.cs` exists
2. Unity should auto-detect it
3. Or manually add `GymServerManager` component to any GameObject

---

## What's Next?

Once `test_connection.py` works:

1. **Test action execution:** Modify `arc_game_gym_env_tcp.py` to execute an action
2. **Run Claude episode:** Use `test_gym_env_with_claude.py` for full 10-turn test
3. **Train RL agent:** Use the gym environment with your favorite RL library!

---

## Files Overview

- `test_connection.py` - Simple connection test (start here!)
- `arc_game_gym_env_tcp.py` - Full Gymnasium environment
- `test_gym_env_with_claude.py` - 10-turn episode with Claude
- `GymServerManager.cs` - Unity TCP server component
- `GymServerInitializer.cs` - Auto-creates GymServerManager

---

## Advanced: Headless Mode

To run Unity headless for automated testing:

```bash
# Start Unity headless with gym server
"/mnt/c/Program Files/Unity/Hub/Editor/2022.3.27f1/Editor/Unity.exe" \
  -batchmode -nographics \
  -projectPath "." \
  -gym-server \
  -logFile gym_server.log &

# Wait a moment for startup
sleep 5

# Run test
python3 test_connection.py

# Stop Unity
pkill -f Unity
```

---

**Status:** ✅ All code implemented and ready to test!
