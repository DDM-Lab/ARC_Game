# Bug-fix push — six fixes, branch `bugfixes-only`

Branch `bugfixes-only` branches from `main-bugfixes` and contains **only** these six
fixes, one per commit, so each can be reviewed, reverted or held back on its own.
V4 (`v1_merge_test`) contains the same six plus the LLM/agent work.

Each entry below answers the same four questions: what the code does now, why that
is a bug, what the player actually sees, and what changes if we fix it.

---

## Quick table

| # | Area | Player-visible symptom | Changes a score? | Risk of merging |
|---|------|------------------------|------------------|-----------------|
| D1 | Casework generation | Tasks appear for buildings being torn down | no | low — re-baselines seeded replays |
| D3 | Daily report | Day's satisfaction change is ~10x wrong, always positive | display only | none |
| D24 | Flood + delivery | Same seed produces different games | no | low — re-baselines seeded replays |
| E1 | Startup config | Sheet's budget/satisfaction silently ignored | **yes** | **check the sheet first** |
| E2 | Startup config | Desktop builds load the wrong map | **yes** | **check config.json first** |
| E3 | Task generation | Budget runs away to ~930k in five days | **yes** | low |

E1 and E2 are the two that will change numbers on day one, because both of them
make configuration that was silently inert start taking effect. Neither changes
game logic — they change which inputs the game runs on.

---

## D1 — Casework requests raised for facilities being deconstructed

**Now.** `ClientStayTracker.CheckClientStayDurations()` rolls a per-round probability
for every client group with an unmet casework need and, on success, generates a
casework task naming that group's current facility. The roll is unconditional.

**Why it's a bug.** Deconstruction is not instantaneous. While a facility is in
`IsDeconstructing()` its clients are being moved out, but the tracker still counts
them as resident and still rolls for them. The task it generates points at a
building that is disappearing.

**What happens.** Tear down an occupied shelter and, with probability that grows
each round the group has been resident, a casework request appears for it. It sits
in the queue until it expires, occupying a task slot.

**If fixed.** Groups in a deconstructing facility are skipped. Fewer spurious tasks;
nothing else reads this path.

**Side effect, catalogued not accidental.** Skipping the group also skips its
`Random` draw, and every stochastic system shares one global stream — so a skipped
draw shifts every later flood, weather and trigger roll in that episode. Seeded
episodes recorded before the fix will not replay identically after it.

---

## D3 — Daily report's satisfaction baseline is on the wrong scale

**Now.** `DailyReportUI.DisplayDailyReport()` seeds the day's starting satisfaction
from `GetDayStartSatisfaction()` and displays "start → end" plus the delta, on a
0–1000 scale. The *end* value is already scaled up to 0–1000. The *start* value is
not.

**Why it's a bug.** Both numbers go into the same subtraction on different scales.
Satisfaction is stored 0–100 and shown 0–1000, so the baseline enters ten times too
small.

**What happens.** The report overstates the day's change by roughly 10x, always in
the positive direction. A day that started at 50 (shown 500) and ended at 480 reads
as **+430** instead of **−20** — the report tells the player they had a good day on
a day they lost ground. This is the number players use to judge their decisions.

**If fixed.** The baseline is scaled to match and the reported change equals the real
one. Display only: no simulation value is read from this path, so no scores, saved
runs or RL rewards change. Only the number on the report screen does, and it becomes
correct.

---

## D24 — `HashSet` order is indexed into with `Random`

**Now.** Four places take a `HashSet<Vector3Int>`, turn it into a `List`, then index
into that list with `UnityEngine.Random.Range`:

- `FloodSystem.RiverTilesInOrder()` — river tiles
- `FloodSystem.FloodTilesInOrder()` — currently flooded tiles
- `FloodSystem.ExpandFlood()` — the dedup'd expansion candidates
- `DeliverySystem.AdjustSceneCount()` — road tiles, choosing a vehicle spawn

**Why it's a bug.** `HashSet` does not define its enumeration order. What comes out
is a function of internal bucket layout, which depends on insertion order and hash
collisions — neither stable across runs, map loads, or Unity versions. Indexing into
that with a seeded `Random` makes the seed meaningless: the *draw* is the same, the
*element it lands on* is not.

**What happens.** Two runs of the same seed on the same map can flood different tiles
and spawn vehicles on different roads. From the first divergent tile on, they are
different games — different facilities cut off, different tasks triggered, different
scores. Seeded replay doesn't work, bug reports aren't reproducible, and RL training
signal is noisier than the policy's own variance.

**If fixed.** Each list is sorted by (x, y, z) before it is indexed, so the same seed
picks the same tile every time. No mechanic and no probability changes — only *which*
member of an equally-likely set gets selected becomes stable. One-time re-baseline of
recorded seeds.

---

## E1 — The config sheet's budget and satisfaction never reach the economy

**Now.** `SatisfactionAndBudget.InitializeWithCentralConfig()` waits for
`GameConfigLoader` to finish fetching the parameter sheet, then copies the sheet's
starting budget and satisfaction into the live economy. It waits with
`yield return new WaitForSeconds(0.1f)`.

**Why it's a bug.** `WaitForSeconds` is scaled by `Time.timeScale`. `GlobalClock`
holds `timeScale` at 0 while the game sits paused at startup — which is exactly when
this coroutine runs. At `timeScale` 0 a scaled wait never advances, so the loop never
steps, the 10-second guard never counts up, and the assignment is never reached.

**What happens.** The game silently starts on the hard-coded inspector defaults
rather than the sheet. Every value tuned in the sheet's budget/satisfaction columns
has no effect, and nothing reports a failure — the numbers just look like somebody
else's. **This is why edits to the sheet "didn't take."**

**If fixed.** `WaitForSecondsRealtime` is unaffected by `timeScale`, the wait
completes, and the sheet is applied.

> **Merge note.** This changes starting values for anyone whose sheet differs from the
> inspector defaults. Look at the sheet before merging — the fix is what makes it live.

---

## E2 — `config.json` is inert on every desktop build

**Now.** `GameConfigLoader.LoadMapConfigFromServer()` reads
`StreamingAssets/config.json` via
`UnityWebRequest.Get(Application.streamingAssetsPath + "/config.json")` to pick up
overrides — above all `mapConfigServerUrl`, which selects which map loads.

**Why it's a bug.** `UnityWebRequest` needs a URI, not a filesystem path. On WebGL
`streamingAssetsPath` is already an `http://` URL and this works. On macOS, Windows
and Linux players it is a bare path like
`/Applications/CORA.app/Contents/Resources/Data/StreamingAssets`, which
`UnityWebRequest` cannot resolve. The request fails, the failure is swallowed, and
the loader proceeds with its built-in defaults.

**What happens.** On desktop, `config.json` does nothing at all. The map URL is
ignored, so the build loads the built-in map instead of the configured one. Because
workforce counts, task frequency and baseline casework satisfaction are all
map-derived, a desktop build silently disagrees with a WebGL build about them — not
because the two disagree on game mechanics, but because they loaded different worlds.
(Separately, we have already hit one wrong-map incident from a different cause — the
map's location on the Talos deploy. Both paths lead to the same class of symptom,
which is worth knowing when one shows up.)

**If fixed.** The path is prefixed with `file://` when it has no scheme (the CSV read
a few lines away already does exactly this). `config.json` becomes effective on
desktop, and desktop and WebGL agree again.

---

## E3 — Nothing caps external-relation contacts; the intended limiter is dead code

**Now.** `TaskSystem.GenerateTasksFromDatabase()` gates Emergency tasks by total count
and round spacing, but places no limit at all on external-relation contacts
(`Budget_Advisory`, `Budget_Emergency`). It does keep a counter,
`currExternalRelationCount`, and a configured maximum, `numExternalRelationTasks`
(seeded from `InitialExternalRelationFrequency`) — the counter is incremented, but
nothing ever reads it.

**Why it's a bug.** The limit was meant to be enforced by
`ApplyInitExternalRelationFrequency`, which splits the configured frequency across two
day-intervals. That method reads the serialized fields `budgetAdvisoryER` and
`budgetEmergencyER` — **both unassigned (`{fileID: 0}`) in every scene that ships.**
The method is dead code, so the cap the designer set in config is never applied.

**What happens.** `Budget_Advisory` ("Storm Funding Advisory") grants **+100,000** and
regenerates as soon as it is resolved. With nothing bounding it, a player — or an
agent — who just takes the highest-value choice each time turns it into a money pump:
in a scripted 32-seed run the budget goes from 10,000 to roughly **930,000 within five
in-game days**. Every cost in the game then rounds to zero, so staffing, construction
and delivery stop being trade-offs, and the RL reward's cost term stops carrying
signal.

**If fixed.** The existing counter is actually consulted, so no more than
`numExternalRelationTasks` are generated per game. The configured value becomes
meaningful for the first time, and cost-bearing decisions remain decisions.

> The cap's *value* (default 3) is a design choice, not part of this fix — it is
> whatever `InitialExternalRelationFrequency` says. The commit only makes the game
> obey it.

---

# Not in this push — open design questions

These are behaviours where V3/V4 and `main-bugfixes` differ, but where "which is
correct" is a call for the team, not a defect with an obvious fix. They are left at
the upstream behaviour with an explanatory comment in the code.

| Ledger | Question | Today's behaviour | If changed |
|---|---|---|---|
| **D13** | Should satisfaction clamp to [0, 100]? | Unclamped — it runs past its own stated maximum (a seeded episode reads 313 on a 0–100 field by round 1) | Clamping is almost certainly right, but it changes reported scores for every existing run |
| **D22** | Should an unfulfilled task carry a penalty when it expires? | No penalty (`ApplyTaskPenalties` is commented out at both call sites) | ~1–3 satisfaction lost per expiry; makes ignoring tasks costly |
| **D15** | Should tasks generate on segment 4, and on day 1's segment 0? | Upstream's gate (`newSegment != 3`), i.e. segments 0,1,2,4; no start-of-day pass | Fewer tasks per day and none on day 1's opening — changes difficulty directly |
| **D21** | Should the Daily Budget Allocation grant the sheet's value or the task asset's? | The **asset's** (5000), not the sheet's `initialDailyBudgetAdditions` (3000) | 2000/day compounding — a median 292,000 gap over a 32-seed suite, against a 10,000 starting budget |
| **D6** | What *should* the external-relation cap be? | 3 (`InitialExternalRelationFrequency`) — now actually enforced, see E3 | Purely a tuning number |

D21 is the one worth deciding before human testing: the sheet and the task asset
disagree, and whichever the team means to be authoritative, they should not
disagree.
