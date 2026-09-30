# Unity gym: `set_seed` + `save_state` / `load_state`

Two capabilities, very different cost. **Do the seed first** — it is ~15 lines, unlocks reproducible
evaluation immediately, and makes replay-based search against real Unity possible on its own.

I could not compile or test these (no Unity toolchain on the cluster), so they are written to be
applied and built in the editor. Everything below was read from the current source.

---

## 1. `set_seed` — cheap, high value

**Why it works:** all 18 randomness call sites in `Assets/Scripts` go through `UnityEngine.Random`
(`WeatherSystem.cs:149`, `Flood/FloodSystem.cs` ×15, and the task generators). That is a single
global stream, so `UnityEngine.Random.InitState(seed)` makes an episode reproducible end to end.

**Why it matters:** the scenario is currently stochastic — 27 distinct task streams across 30 `noop`
episodes, diverging by round 5 (relocation counts vary 14–20). With a seed:
  * evaluation stops needing n=32 to average out scenario luck; agents can be compared on the SAME
    scenario, which removes the largest variance term in the benchmark;
  * replay-based tree search against real Unity becomes possible (re-run a prefix deterministically);
  * a regression that only appears on one scenario becomes reproducible.

### `GymServerManager.cs` — add to the dispatch switch (~line 371, next to `reset_game`)

```csharp
                case "set_seed":
                    return HandleSetSeed(request);
```

### Add the handler and the field

```csharp
    // Seed applied on every reset so a whole EPISODE is reproducible, not just the tail after the
    // command. -1 means "leave Unity's default non-deterministic seeding alone".
    int gymSeed = -1;

    string HandleSetSeed(GymRequest request)
    {
        gymSeed = request.seed;
        UnityEngine.Random.InitState(gymSeed);
        Debug.Log($"[GymServer] set_seed {gymSeed}");
        return $"{{\"type\":\"seed_set\",\"seed\":{gymSeed}}}";
    }
```

Add `public int seed;` to the `GymRequest` struct so the field deserialises.

### Re-apply on reset

In `ResetRoutine()` (the coroutine `HandleResetGame` starts), immediately **before** the scene
reload, add:

```csharp
        if (gymSeed >= 0) UnityEngine.Random.InitState(gymSeed);
```

Without this the seed only affects the current episode, and the next `reset_game` diverges again.

### Python side — `arc_game_gym_env_tcp.py`

```python
    def set_seed(self, seed: int):
        """Make the NEXT episode reproducible. Call before reset()."""
        return self._send({"type": "set_seed", "seed": int(seed)})
```
and call it from `reset(seed=...)` so the gymnasium seeding contract is honoured.

### Acceptance test
Two episodes with the same seed and the same actions must produce identical task streams:
```
env.set_seed(7); env.reset(); a = [sorted(t['taskTitle'] for t in s['allActiveTasks']) for s in noop_rollout(env)]
env.set_seed(7); env.reset(); b = ...
assert a == b
```
Today that assertion fails 27 times in 30.

---

## 2. `save_state` / `load_state` — harder, still worth it

A full scene serialisation is not the right target. Serialise the **game-logic** state only and
rebuild from it; the visual scene can be reconstructed by the existing spawn paths.

State that must round-trip (each already has an accessor used by `HandleGetGameState`):

| subsystem | fields |
|---|---|
| `GlobalClock` | `currentDay`, `currentTimeSegment` |
| `SatisfactionAndBudget` | `satisfaction`, `budget` |
| buildings | per building: type, site id, status, `assignedWorkforce`, `BuildingResourceStorage` amounts |
| prebuilt | Motel / Community populations (`PrebuiltBuildings`) |
| `TaskSystem` | `activeTasks` (id, title, affected facility, roundsRemaining, status, linked delivery ids) |
| `DeliverySystem` | `pendingTasks` + in-flight `DeliveryTask`s |
| vehicles | per vehicle: position, `currentTask`, cargo |
| `RewardMetricsTracker` | all counters — **required**, or the reward is wrong after a load |
| `MotelCostManager` | accrued charge state |
| RNG | `UnityEngine.Random.state` (a serialisable struct — capture and restore it verbatim) |

```csharp
                case "save_state":  return HandleSaveState(request);   // {"type":"save_state","slot":"a"}
                case "load_state":  return HandleLoadState(request);   // {"type":"load_state","slot":"a"}
```

Store slots in memory (`Dictionary<string, GymSnapshot>`) and optionally mirror to disk as JSON so a
state can be shared between processes and checked into an eval suite.

**Ordering trap:** restore the RNG state *last*, after every subsystem has been rebuilt — rebuilding
spawns objects and some spawn paths draw from `UnityEngine.Random`, which would otherwise advance the
stream you just restored and silently break determinism.

**Suggested evaluation use:** a curated set of saved mid-game states (day 3 with a food backlog, day 5
vehicle-starved, day 6 with a casework queue) turns the benchmark from "play 32 rounds and average"
into targeted probes of specific competencies — much lower variance per unit of compute.

---

## Sequencing
`set_seed` alone removes scenario variance from the benchmark and is worth doing on its own, before
any snapshot work. It is also a prerequisite for trusting snapshot tests, since without it a loaded
state still evolves non-deterministically.
