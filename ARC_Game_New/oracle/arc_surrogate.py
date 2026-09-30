"""Fast seeded surrogate of the ARC_Game 32-round scenario, for offline search.

WHY THIS EXISTS
Unity is the ground truth but cannot support tree search: the gym protocol has no state
snapshot/restore and no seed, and the scenario is stochastic (27 distinct task streams across 30
noop episodes, diverging by round 5). So "the same node" visited twice is two different worlds and
MCTS statistics are meaningless. This surrogate is seeded and runs ~1e4 rollouts/sec, so the search
happens here and the winning plans are then REPLAYED against Unity to measure the gap.

EVERY CONSTANT IS SOURCED, NOT GUESSED
  reward             reward_scoring.compute_score_components -- imported, not reimplemented, so the
                     objective is identical by construction.
  kitchen            Kitchen.prefab roundProduction: 10 packs/round, requiredResources [];
                     resourceCapacities.maxCapacity 20; BuildingResourceStorage.ProduceResources
                     gates on Building.IsOperational() and skips when storage is full.
  vehicle            Vehicle.maxCargoCapacity = 10 packs; DeliverySystem.cs:235-249 splits a request
                     into ceil(qty/capacity) loads, each needing its own vehicle.
  fleet              3 (max simultaneous idle observed across every recorded run).
  motel              MotelCostManager.costPerPersonPerDay = 200, charged every day a resident stays.
  build              state.costs.build = 2000; needWorkers 4; ~1 day (~4 rounds) construction.
  hire               state.costs.hireUntrained = 200 (1 workforce unit).
  shelter/casework   capacity 100 / 400 (observed populationCapacity).
  arrivals           measured over 30 noop episodes (agent-independent): food is DETERMINISTIC at
                     3 requests on one round per day for days 2..8 (21/episode); relocations average
                     2.3/day split across two segments and total 14-20/episode.
  demand units       RewardMetricsTracker.RecordTaskResolution: resolvedAdd = demandQuantity or 1.
                     Measured: food contributes ~1 per task (demandQuantity 0), lodging contributes
                     the PEOPLE count (~35/relocation) -- so lodging dominates the quantity ratios.
  expiry             ExpireTask calls RecordTaskResolution(fulfilled: false), so ignoring a task
                     books its full demand into the denominator with a zero numerator. Declining is
                     never free; that asymmetry is modelled.
  casework           caseworkRequested is AGENT-INDUCED -- zero under noop, generated as housed
                     residents finish their stay. ratio(0,0)=0, so housing nobody scores 0 on that
                     term too.

CALIBRATION (oracle/validate_surrogate.py -- run it after ANY change; it gates on tolerances)
  noop               score  +0.000 vs +0.000   exact
  greedy             score  +1.538 vs +1.575   (-0.038)   budget -268k vs -238k
  build-potential    score  +3.163 vs +3.200   (-0.037)   budget  +17k vs   +5k
  pareto (HELD-OUT)  score  +2.646 vs +2.600   (+0.047)   budget +243k vs +293k
The pareto case was NOT used to fit anything -- it is a policy the model had never seen, run in
Unity after the fact, and predicted to within 0.047. Its component agreement is reported but not
asserted (the analogue re-implements the real policy's guards rather than being that policy); its
worst residual is cost_food 0.139, because Unity's kitchens serve almost nothing while ~200 shelter
residents eat 200 packs per 4-round interval against 2 kitchens producing 40.

MECHANICS LEARNED FROM THAT HELD-OUT TEST (each one changed the model)
  * casework throughput is GLOBAL, not per site: Unity reads 0.519 at 1 site, 0.517 at 2, 0.288 at
    3 -- extra sites buy nothing, and the fall at 3 is that policy housing more people in shelters.
  * casework REQUESTS come mostly from shelter residents (weight 1.0) and only weakly from motel
    residents (0.30): request count tracks shelter population, not total housed.
  * a relocation that resolves SHORT is re-issued, and the replacement books its full demand again
    -- so a failed relocation is doubly expensive. Bounded at RELOC_MAX_REISSUES.
  * people are a finite pool: a re-issue asks for the SAME residents, so deliveries draw down one
    shared count or the model credits them twice.
  * shelter-destined relocations cost ~3x the fleet capacity of motel-destined ones
    (enableMultipleDeliveries splits the load across shelters).
  * no motel spill: a shelter-destined load that does not fit is simply not delivered.
Together these are why the surrogate's Pareto frontier over-valued shelter-heavy play: every one
of them makes routing to shelters more expensive than the model originally believed.

KNOWN DIVERGENCES (why the bound is indicative, not exact)
  * road blockage / flood events and their delivery-failure penalties (15 food / 10 other) are not
    modelled; they cost Haiku 9 penalty events across 32 episodes.
  * the one-round satisfaction collapse to 0 seen in 3-10% of real episodes is unexplained and
    therefore absent here, so the surrogate is OPTIMISTIC on tail outcomes.
  * casework request generation is fitted, not read from source.
Validate with oracle/validate_surrogate.py before trusting any number.
"""
from __future__ import annotations
import random, sys, os
from dataclasses import dataclass, field

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from reward_scoring import compute_score_components   # the REAL objective, imported

ROUNDS, ROUNDS_PER_DAY = 32, 4
FLEET = 3
VEH_CAPACITY = 10           # packs per load
KITCHEN_RATE, KITCHEN_CAP = 10, 20
BUILD_COST, BUILD_ROUNDS, NEED_WORKFORCE = 2000, 4, 4
HIRE_COST = 200
SHELTER_BEDS, CASEWORK_CAP = 100, 4    # CASEWORK_CAP = people processed per ROUND, GLOBAL
# The map ships exactly 15 AbandonedSite objects (counted in Scenes/MainScene.unity), one building
# each. Without this cap the search happily built 22 -- an illegal plan that inflated the bound.
MAX_SITES = 15
MOTEL_PER_DAY = 200
PAID_FOOD_COST, PAID_FOOD_PACKS = 1000, 10
PAID_RELOC_COST = 3000
FOOD_DAYS = (2, 3, 4, 5, 6, 7, 8)
FOOD_PER_DAY = 3
# ── ALL OF THE BELOW MEASURED ON THE CURRENT (Aug-14) BINARY ────────────────────────────────
# Sources: polsearch_35482/reverify_greedy and reverify_build-potential (n=32 pooled). Arrivals
# are agent-independent for COMMUNITY demand and were identical across both policies.
#
# Community food: EXACTLY 3 requests at segment 1 of days 2..8 -> 21/episode, in 32/32 episodes.
# Relocations: only 6 per episode, all in days 2..4, each moving 100 PEOPLE (601 lodgingResolved
#   / 6 = 100.2). The earlier model used 35 people x 17 relocations, fitted to the OLD binary.
RELOC_DIST = {   # round -> empirical distribution of how many relocations arrive
    5:  {0: 1, 1: 4, 2: 13, 3: 14},
    6:  {0: 6, 1: 12, 2: 10, 3: 4},
    9:  {0: 4, 1: 18, 2: 7, 3: 3},
    10: {0: 15, 1: 14, 2: 3},
    13: {0: 21, 1: 11},
    14: {0: 30, 1: 1, 2: 1},
    21: {0: 31, 1: 1},
}
PEOPLE_PER_RELOC = 100
# Income. Daily Budget is deterministic: +$5,000 at segment 1 of days 2..8 in 32/32 episodes.
# Storm Funding offers +$50,000 once per day for days 2..7 in ~1/3 of episodes (the task lingers
# two rounds, so counting both segments double-counts a single offer -- an error in the first pass).
DAILY_BUDGET = 5000
DAILY_BUDGET_ROUNDS = (5, 9, 13, 17, 21, 25, 29)
# Storm Funding offers TWO amounts -- $50,000 at 2 rounds' delay or $100,000 at 4 rounds' delay --
# and every measured policy takes the $100,000. An earlier pass counted only the FIRST positive
# choice per task, so it booked half the amount and (by deduping on the task's two-round lifetime)
# a third of the frequency, giving 137,000 income against a true 395k-636k. Reconciled against the
# budget identity income = finalBudget + totalSpend - 3,000:
#   greedy 4.12 x 100k + 35k = 447,000 (measured 449,500); build-potential 3.62 -> 397,000
#   (395,044); combined 5.38 -> 573,000 (571,519).
# Offer COUNT varies by policy (3.6-6.0/ep) for reasons not established; 4.3 is the mean.
STORM_FUNDING = 100000
STORM_ROUNDS = (5, 9, 13, 17, 21, 25)
STORM_PROB = 4.3 / 6.0
# Casework is AGENT-INDUCED: zero under noop, generated as housed residents finish their stay.
# Measured on build-potential: requests begin ~1 round after housing starts, cluster in rounds
# 6-15, and processing visibly DRAINS housed population (472 -> 173 over an episode).
CASEWORK_DELAY = 2   # rounds before a housed cohort's casework request appears
                     # 6 fits the observables -- score, casework rate and final budget -- best)
# Every housed resident eventually requests casework (fraction 1.0); throughput is what limits the
# term. CASEWORK_CAP is people processed per staffed site per answered round -- FITTED, because
# ClientStayTracker.RemoveClientsByQuantity takes its quantity from the task's choice and the task
# payloads are not recoverable from transcripts. Calibrated so a build-potential-shaped plan
# reproduces Unity: score +3.185 vs +3.196 (gap 0.011). Residuals at this setting:
# casework_processing_sat 0.587 vs 0.541 and cost_lodging 0.180 vs 0.122 -- they partially cancel,
# so the AGGREGATE matches better than the individual terms. Treat per-component surrogate numbers
# as indicative and the score as calibrated.
CASEWORK_FRACTION = 1.0
# Casework requests come predominantly from SHELTER residents; the motel contributes a much smaller
# base rate. ClientStayTracker.RegisterClientArrival is driven by shelter arrivals (its motel
# support was a later fix). Measured across Unity policies, request COUNT tracks shelter population
# and not total housed -- shelterPop 86 -> 38 tasks/ep, 153 -> 52.6, 280 -> 76.4, i.e.
# tasks ~ 21 + 0.198 x shelterPop, while total housed was ~475 in all three. Weighting every housed
# person equally made build-potential and the pareto strategy produce IDENTICAL casework in the
# surrogate (0.415 each) where Unity separates them 0.519 vs 0.281 -- which is precisely why the
# frontier could not see that shelter-heavy routing poisons its own casework ratio.
MOTEL_CASEWORK_WEIGHT = float(os.environ.get("MOTEL_CASEWORK_WEIGHT", "0.30"))
# Departure and casework CREDIT are separate events on separate clocks. ClientStayTracker has two
# removal paths: RemoveClientsByQuantity (called from the task handler, credits
# RecordCaseworkProcessed) and RemoveClientGroup ("leave for casework OR permanent housing"),
# which removes residents WITHOUT crediting anything. That is how Unity holds a high lodging bill
# (352,388 -- residents stay a while) together with casework_processing_sat of only 0.541.
# Tying the two to one clock made them unreachable together: any delay short enough to match the
# casework rate emptied the shelters and under-billed lodging by ~164k.
DEPART_DELAY = 14        # rounds housed before residents leave (uncredited)
# ── mechanics added after comparing motel fill-rate against Unity ──────────────────────────
# ResourceType { Population = 0, FoodPacks = 1 } and Vehicle.allowedCargoTypes = [0,1] with a
# single maxCargoCapacity = 10 -- so PEOPLE move in 10-person loads exactly like food packs, and
# DeliverySystem splits a request into ceil(qty/10) loads each needing its own vehicle. A
# 100-person relocation is therefore TEN vehicle-loads, not one. Modelling it as one filled the
# motel ~5x too fast (round 8: 500 people vs Unity's 100) and over-billed the $200/person/day
# meter by ~326k on greedy.
PEOPLE_PER_LOAD = 10
# Shelter-destined relocations cost more fleet capacity per person than motel-destined ones.
# Community_TransportRequest sets enableMultipleDeliveries: 1 on "Send to Shelters" (the load is
# split across several shelters) and 0 on "Send to Motel" (one destination). Measured consequence
# in Unity: the guarded pareto policy, which routes to shelters, drew lodgingResolved 1026 against
# build-potential's 607 while both moved the same ~600 people -- i.e. ~4 relocation tasks per
# episode delivered short and were re-issued. Its shelters all reached InUse and its shelter
# population still peaked at only 200, so the shortfall is delivery throughput, not capacity.
SHELTER_LOAD_COST = float(os.environ.get("SHELTER_LOAD_COST", "3.0"))
RELOC_MAX_REISSUES = int(os.environ.get("RELOC_MAX_REISSUES", "3"))
# A vehicle completes MULTIPLE trips per round. GlobalClock runs a 10 simulated-second round and
# site_distance_matrix.json (A* over the real road net) gives trip times of 2.0-7.4 s, median 4.4 --
# so ~10/4.4 = 2.3 trips per vehicle per round. Confirmed against Unity's motel fill rate under
# build-potential: 100 -> 300 people over r8-r12 is 50/round and 300 -> 600 over r12-r16 is
# 75/round, i.e. 1.7-2.5 loads per vehicle per round at 10 people a load. Modelling one trip per
# vehicle per round under-delivered badly (lodging 96k against Unity 352k, score -0.285).
TRIPS_PER_VEHICLE_PER_ROUND = 5.0
# BuildingResourceStorage population consumption: foodPerPersonPerNRounds = 1 every
# consumptionRoundInterval = 4, workersConsumeFoodToo = true. ENABLED on Shelter.prefab
# (enablePopulationBasedConsumption: 1) and DISABLED on MotelPrefab (: 0) -- so sheltered
# residents eat and motel residents do not, a standing cost of shelters the model had ignored.
FOOD_PER_PERSON_PER_INTERVAL = 1
CONSUMPTION_INTERVAL = 4
# WorkerTrainingSystem.trainingDurationDays = 1 -- hires are not instantly available.
HIRE_DELAY_ROUNDS = ROUNDS_PER_DAY
# (Income constants live above -- an earlier duplicate of this block silently redefined
#  STORM_FUNDING back to 50,000 AFTER the corrected 100,000, pinning modelled income at 250,125
#  against Unity's 395k-636k. Constants are declared once, near the top, on purpose.)


@dataclass
class Building:
    kind: str
    ready_at: int
    workforce: int = 0
    stock: int = 0          # kitchens only
    pop: int = 0            # shelters only
    def operational(self, rnd): return rnd >= self.ready_at and self.workforce >= NEED_WORKFORCE


@dataclass
class Task:
    kind: str               # 'food' | 'reloc'
    people: int             # lodging demand (0 for food)
    deadline: int
    answered: bool = False
    resolved: bool = False


@dataclass
class Metrics:
    foodResolved: int = 0; foodFulfilled: int = 0
    lodgingResolved: int = 0; lodgingFulfilled: int = 0
    caseworkRequested: int = 0; caseworkProcessed: int = 0
    foodSpend: float = 0.0; lodgingSpend: float = 0.0
    workerSpend: float = 0.0; caseworkSpend: float = 0.0
    cumWorkingWorkers: int = 0; cumTrainingWorkers: int = 0; cumIdleWorkers: int = 0
    totalWorkers: int = 0; daysCompleted: int = 1
    def as_dict(self):
        return {k: v for k, v in self.__dict__.items()}


class ArcSurrogate:
    """Seeded, deterministic-given-seed model of one 32-round episode."""

    def __init__(self, seed=0):
        # Decorrelate the seed. Mersenne Twister seeded with small SEQUENTIAL integers produces
        # correlated early draws, and every ensemble here is range(0, N): measured 6.049
        # relocations/episode on seeds 0..2999 against an expectation of exactly 6.000, which
        # inflated lodgingResolved by ~2% (613 vs Unity's 601). Randomised seeds gave 5.984.
        self.rng = random.Random(hash((seed, 0x5EED)) & 0xFFFFFFFF)
        self.rnd = 0
        self.budget = 3000.0
        self.community_pop = 1200
        # People are a finite pool. A re-issued relocation asks again for the SAME residents, so
        # deliveries must draw down one shared pool -- otherwise a re-request that succeeds credits
        # its 100 people a second time. Measured: Unity moves ~600 people out of the communities and
        # books lodgingFulfilled 605; without this cap the model booked ~853 against the same 600.
        self.relocatable = 0            # set from the schedule once it exists (see below)
        self.motel_pop = 0
        self.buildings: list[Building] = []
        self.free_workers = 10
        self.tasks: list[Task] = []
        self.m = Metrics(totalWorkers=10)
        self.pending_casework = []          # (due_round, people)
        self.open_casework = 0              # requested but not yet answered
        self.pending_depart = []            # (due_round, people) residents whose stay is ending
        self.inflight = []                  # (arrive_round, dest, people) population loads
        self.pending_hires = []             # (available_round, count)
        self._schedule = self._make_schedule()
        # The pool is exactly the demand the ORIGINAL relocations ask for, so a policy that never
        # fails can still reach 100% fulfilment, while a re-issued task competes for the same people
        # instead of minting new ones. A flat 600 was too small against this schedule's ~614 of
        # generated demand and cost even greedy 0.22 of sat_lodging.
        self.relocatable = sum(PEOPLE_PER_RELOC for ks in self._schedule.values()
                               for k in ks if k == "reloc")

    # ---- arrival process (measured over 30 noop episodes) ----------------------------------
    def _make_schedule(self):
        """Community demand only. Shelter food requests are induced later, from shelter state."""
        sched = {}
        for day in FOOD_DAYS:                      # deterministic: 3 at segment 1, days 2..8
            sched.setdefault((day - 1) * ROUNDS_PER_DAY + 1, []).extend(["food"] * FOOD_PER_DAY)
        for rnd, dist in RELOC_DIST.items():       # empirical per-round relocation counts
            total = sum(dist.values())
            roll = self.rng.randrange(total)
            acc = 0
            for k, v in sorted(dist.items()):
                acc += v
                if roll < acc:
                    sched.setdefault(rnd, []).extend(["reloc"] * k)
                    break
        return sched

    def _spawn(self):
        # Income advisories -- pure upside, no delivery, so a rational plan always takes them.
        if self.rnd in DAILY_BUDGET_ROUNDS:
            self.budget += DAILY_BUDGET
        if self.rnd in STORM_ROUNDS and self.rng.random() < STORM_PROB:
            self.budget += STORM_FUNDING
        if self.rnd == 6:
            # "Flood Alert - Rising Water" is taskTag 2 (Lodging) with demandQuantity 1, arriving
            # once per episode -- it is the +1 that makes Unity's lodgingResolved 601 and not 600.
            self.tasks.append(Task("reloc", 1, self.rnd + 4))
        for kind in self._schedule.get(self.rnd, []):
            people = PEOPLE_PER_RELOC if kind == "reloc" else 0
            self.tasks.append(Task(kind, people, self.rnd + 4))
        # Shelter food requests are INDUCED by having populated shelters (they appear only in runs
        # that build: greedy 21.0 food/ep, build-potential 22.4, combined 27.1). One per populated
        # shelter at segment 2, which reproduces that spread.
        if self.rnd % ROUNDS_PER_DAY == 2:
            for b in self.buildings:
                if b.kind == "Shelter" and b.pop > 0 and b.operational(self.rnd):
                    self.tasks.append(Task("food", 0, self.rnd + 4))

    # ---- the action is a per-round plan ----------------------------------------------------
    # {"build": None|'Kitchen'|'Shelter'|'CaseworkSite',
    #  "hire": int,
    #  "answer": [(task_index, option)] where option in
    #            food : 'kitchen10' | 'kitchen20' | 'paid'
    #            reloc: 'motel' | 'shelter' | 'paid_motel'}
    def legal_actions(self):
        acts = [None, "Kitchen", "Shelter", "CaseworkSite"]
        return acts

    def step(self, action):
        self._spawn()
        free_veh = int(FLEET * TRIPS_PER_VEHICLE_PER_ROUND)   # dispatchable LOADS this round

        if action.get("build") and len(self.buildings) < MAX_SITES:
            kind = action["build"]
            self.buildings.append(Building(kind, self.rnd + BUILD_ROUNDS))
            self.budget -= BUILD_COST
            # BuildingSystem.cs:191-195 attributes construction to a SPEND CATEGORY by type:
            # Kitchen -> Food, Shelter -> Lodging, CaseworkSite -> Casework. Booking only the
            # casework case (as this model first did) made kitchens and shelters FREE in the
            # reward, and the search exploited it by building 9 kitchens and 7 shelters.
            if kind == "Kitchen":
                self.m.foodSpend += BUILD_COST
            elif kind == "Shelter":
                self.m.lodgingSpend += BUILD_COST
            else:
                self.m.caseworkSpend += BUILD_COST
        hire = int(action.get("hire", 0))
        if hire:
            # WorkerTrainingSystem.trainingDurationDays = 1: paid now, usable next day.
            self.pending_hires.append((self.rnd + HIRE_DELAY_ROUNDS, hire))
            self.budget -= hire * HIRE_COST
            self.m.workerSpend += hire * HIRE_COST
            self.m.totalWorkers += hire
        arrived = [c for (d, c) in self.pending_hires if d <= self.rnd]
        if arrived:
            self.free_workers += sum(arrived)
            self.pending_hires = [(d, c) for (d, c) in self.pending_hires if d > self.rnd]

        # staff anything ready that still needs workers (always beneficial: enables production)
        for b in self.buildings:
            if self.rnd >= b.ready_at and b.workforce < NEED_WORKFORCE:
                take = min(NEED_WORKFORCE - b.workforce, self.free_workers)
                b.workforce += take; self.free_workers -= take

        # in-flight population loads land (one vehicle-load = PEOPLE_PER_LOAD people)
        landing = [t for t in self.inflight if t[0] <= self.rnd]
        self.inflight = [t for t in self.inflight if t[0] > self.rnd]
        for _, dest, ppl, task in landing:
            # PARTIAL delivery counts: RewardMetricsTracker books fulfilledAdd = min(delivered,
            # demand), and AddLateDelivery credits loads that arrive after the task resolved
            # (capped so fulfilled never exceeds resolved). Crediting only fully-shipped tasks --
            # as the first pass did -- dropped greedy 0.98 of score, because a 100-person
            # relocation needs 10 loads against 3 vehicles and a 4-round deadline.
            if dest == "motel":
                self.motel_pop += ppl
            else:
                left = ppl
                for b in self.buildings:
                    if left <= 0: break
                    if b.kind == "Shelter" and b.operational(self.rnd) and b.pop < SHELTER_BEDS:
                        d = min(SHELTER_BEDS - b.pop, left); b.pop += d; left -= d
                # NO motel spill. A shelter-destined load that does not fit simply is not
                # delivered -- Unity's "Send to Shelters" does not silently reroute. The spill this
                # model used to do meant shelter routing NEVER failed, so the re-request path never
                # fired and the frontier saw shelter-heavy play as costless.
                ppl -= left
            if ppl > 0:
                task.delivered = getattr(task, "delivered", 0) + ppl
                self._queue_casework(ppl, dest)

        # sheltered residents (and their workers) eat; the motel does not feed anyone
        # Lumpy consumption, matching BuildingResourceStorage: the whole interval's demand is
        # taken on one round every CONSUMPTION_INTERVAL. Spreading it evenly across rounds was
        # tried and fitted WORSE (held-out pareto -0.009 -> -0.084), so the lumpy form stays.
        if self.rnd % CONSUMPTION_INTERVAL == 0:
            for b in self.buildings:
                if b.kind != "Shelter" or not b.operational(self.rnd):
                    continue
                need = (b.pop + b.workforce) * FOOD_PER_PERSON_PER_INTERVAL
                have = min(need, b.stock)
                b.stock -= have
                if have < need:                 # unmet demand draws from any kitchen with stock
                    short = need - have
                    for k in self.buildings:
                        if short <= 0: break
                        if k.kind == "Kitchen" and k.stock > 0:
                            d = min(k.stock, short); k.stock -= d; short -= d

        # kitchens produce, capped by storage (ProduceResources skips when full)
        for b in self.buildings:
            if b.kind == "Kitchen" and b.operational(self.rnd):
                b.stock = min(KITCHEN_CAP, b.stock + KITCHEN_RATE)

        # answer tasks
        for idx, opt in action.get("answer", []):
            if idx >= len(self.tasks): continue
            t = self.tasks[idx]
            if t.answered or t.resolved: continue
            if t.kind == "food":
                if opt == "paid":
                    self.m.foodSpend += PAID_FOOD_COST
                    self.budget -= PAID_FOOD_COST
                    t.answered = True; self._resolve(t, delivered=1)
                else:
                    loads = 2 if opt == "kitchen20" else 1
                    need = loads * VEH_CAPACITY
                    # A delivery draws from ONE source building (DeliverySystem picks a single
                    # source), so the order needs `need` packs in a SINGLE kitchen -- pooling stock
                    # across kitchens made kitchen food far too available. Measured: Unity's pareto
                    # policy paid for ~24 of its 22.4 food tasks (foodSpend 28,031 including 4,000
                    # of kitchen construction), i.e. its kitchens served almost nothing, because
                    # ~200 shelter residents eat 200 packs per 4-round interval against 2 kitchens
                    # producing 40.
                    src = max((b for b in self.buildings
                               if b.kind == "Kitchen" and b.operational(self.rnd)),
                              key=lambda b: b.stock, default=None)
                    if loads <= free_veh and src is not None and src.stock >= need:
                        free_veh -= loads
                        src.stock -= need
                        t.answered = True; self._resolve(t, delivered=1)
            else:
                beds = sum(SHELTER_BEDS - b.pop for b in self.buildings
                           if b.kind == "Shelter" and b.operational(self.rnd))
                if opt == "paid_motel":
                    # immediate: uses NO vehicle, so it is never fleet-limited
                    self.budget -= PAID_RELOC_COST
                    self.m.lodgingSpend += PAID_RELOC_COST
                    self.motel_pop += t.people
                    t.answered = True; self._resolve(t, delivered=t.people)
                    self._queue_casework(t.people, 'motel')
                elif opt in ("shelter", "motel"):
                    # DeliverySystem splits into ceil(people / capacity) loads, each needing its
                    # own free vehicle. Loads that cannot be dispatched this round wait; the task
                    # is credited for what actually ships.
                    dest = opt if not (opt == "shelter" and beds <= 0) else "motel"
                    remaining = min(t.people - getattr(t, "shipped", 0), self.relocatable)
                    unit = SHELTER_LOAD_COST if dest == "shelter" else 1.0
                    loads = min(-(-remaining // PEOPLE_PER_LOAD), int(free_veh / unit))
                    if loads > 0:
                        free_veh -= int(round(loads * unit))
                        shipped = min(remaining, loads * PEOPLE_PER_LOAD)
                        self.inflight.append((self.rnd + 1, dest, shipped, t))
                        t.shipped = getattr(t, "shipped", 0) + shipped
                        self.relocatable = max(0, self.relocatable - shipped)

        # expiry: books demand into the denominator with zero numerator (ExpireTask semantics)
        for t in self.tasks:
            if not t.resolved and self.rnd >= t.deadline:
                self._resolve(t, delivered=getattr(t, "delivered", 0))

        # Casework REQUESTS surface as tasks. Processing happens only when the agent ANSWERS one
        # and a staffed CaseworkSite exists -- RewardMetricsTracker.RecordCaseworkProcessed is
        # called from ClientStayTracker.RemoveClientsByQuantity, which the task's choice handler
        # drives. It is NOT an automatic per-round drain. Modelling it as automatic made the
        # surrogate 0.319 optimistic on a build-potential-like plan (1.00 vs Unity's 0.531),
        # because Unity's greedy scorer never answers these Advisory tasks at all (no Budget or
        # Satisfaction impact -> value 0 -> never selected).
        due = [p for (d, p) in self.pending_casework if d <= self.rnd]
        if due:
            self.m.caseworkRequested += sum(due)
            self.pending_casework = [(d, p) for (d, p) in self.pending_casework if d > self.rnd]
            self.open_casework += sum(due)
            # Residents LEAVE when their stay is up, whether or not their casework was answered.
            # ClientStayTracker tracks overstay (overstayThreshold = 8 rounds) separately from
            # casework, and Unity's housed population visibly drains 472 -> 173 while build-
            # potential processes only 54% of casework. Coupling the two -- as this model first
            # did -- made the two observables unreachable together: any drain fast enough to hit
            # Unity's lodging spend (352,388) drove casework_processing_sat to ~1.0 against its
            # measured 0.541.
            # Departure requires somewhere to be processed: Unity's greedy builds NO casework
            # site and its lodging spend stays at 680,500 (residents never leave), while
            # build-potential builds one and drains to 352,388. So the stay ends only once an
            # operational casework site exists -- but the REWARD only credits the share actually
            # answered, which is how Unity reaches low casework (0.541) and low lodging together.
        # departures run on their OWN clock and are not credited to casework
        leaving = [p for (d, p) in self.pending_depart if d <= self.rnd]
        if leaving:
            self.pending_depart = [(d, p) for (d, p) in self.pending_depart if d > self.rnd]
            if any(b.kind == "CaseworkSite" and b.operational(self.rnd) for b in self.buildings):
                self._depart(sum(leaving))
            else:
                self.pending_depart.append((self.rnd + 1, sum(leaving)))   # nowhere to go yet
        if action.get("casework"):
            # Throughput is GLOBAL, not per site. Measured in Unity: 1 site -> casework 0.519,
            # 2 sites -> 0.517, 3 sites -> 0.288. Extra sites buy nothing; the drop at 3 is because
            # that policy housed more people in shelters (shelterPop 86 -> 280), and requests scale
            # with housed population (38 -> 76 tasks/ep) while processed stays put. Ratio check:
            # requests x2.01, score /1.80 -- i.e. processed is ~constant per episode.
            # Modelling it as cap-per-site made 3 sites look like ~1.0 and sent the frontier
            # strategy chasing casework capacity that does not exist.
            cap = CASEWORK_CAP if any(b.kind == "CaseworkSite" and b.operational(self.rnd)
                                      for b in self.buildings) else 0
            take = min(self.open_casework, cap)
            self.m.caseworkProcessed += take
            self.open_casework -= take
            # NOTE: no population drain here -- departure already happened when the stay ended.

        # worker utilisation + the motel's recurring charge, applied on day boundaries
        working = sum(b.workforce for b in self.buildings)
        self.m.cumWorkingWorkers += working
        self.m.cumIdleWorkers += self.free_workers
        self.rnd += 1
        if self.rnd % ROUNDS_PER_DAY == 0:
            self.m.daysCompleted = self.rnd // ROUNDS_PER_DAY
            charge = self.motel_pop * MOTEL_PER_DAY
            self.budget -= charge
            self.m.lodgingSpend += charge
        return self.done()

    def _depart(self, people):
        """Residents leaving housing, which stops their per-day charge."""
        left = people
        take = min(self.motel_pop, left); self.motel_pop -= take; left -= take
        for b in self.buildings:
            if left <= 0: break
            if b.kind == "Shelter" and b.pop > 0:
                d = min(b.pop, left); b.pop -= d; left -= d
        return people - left

    def _queue_casework(self, people, dest="shelter"):
        # Only a share of a cohort requests casework, and it arrives ~2 rounds after housing --
        # measured: requests begin the round after housing starts and cluster in rounds 6-15.
        w = CASEWORK_FRACTION * (MOTEL_CASEWORK_WEIGHT if dest == "motel" else 1.0)
        self.pending_casework.append((self.rnd + CASEWORK_DELAY, int(people * w)))
        self.pending_depart.append((self.rnd + DEPART_DELAY, people))

    def _resolve(self, t, delivered):
        t.resolved = True
        # A relocation that resolves SHORT is re-issued by the community, and the replacement books
        # its full demand into lodgingResolved again. Measured: the pareto strategy (which routes to
        # shelters and frequently overflows them) drew 10.59 relocation tasks/episode against
        # build-potential's 6.06 -- lodgingResolved 1026 vs 607 -- while BOTH moved the same ~600
        # people out of the communities (no rebound in community population). So the extra demand is
        # re-requests, not new arrivals. This makes a failed relocation doubly expensive and is why
        # shelter-heavy routing is self-defeating in Unity but looked free in this model.
        # Re-issue only while there are still people left to relocate. Without the pool check the
        # loop runs away: once the communities are empty nothing can be delivered, so every task
        # resolves short and spawns another, and lodgingResolved ran to 1477 against Unity's 1026.
        # Re-issue a shortfall, but bound the chain. Unbounded re-issue runs away once the
        # communities empty (lodgingResolved 1477 vs Unity's 1026); refusing to re-issue at all
        # under-counts it (the held-out pareto case then reads +0.211 instead of -0.009). Unity sits
        # between: ~10.3 relocation tasks/episode against 6.1 originals, i.e. roughly one re-issue
        # per failed task.
        chain = getattr(t, "reissues", 0)
        if (t.kind == "reloc" and delivered < t.people and self.rnd + 4 < ROUNDS
                and chain < RELOC_MAX_REISSUES):
            short = t.people - delivered
            if short >= PEOPLE_PER_LOAD:
                nt = Task("reloc", t.people, self.rnd + 4)
                nt.reissues = chain + 1
                self.tasks.append(nt)
        if t.kind == "food":
            self.m.foodResolved += 1; self.m.foodFulfilled += min(delivered, 1)
        else:
            self.m.lodgingResolved += t.people; self.m.lodgingFulfilled += min(delivered, t.people)

    def done(self):
        if self.rnd >= ROUNDS:
            # measured: foodResolved 21.0 (= every food task) and lodgingResolved 601.0 are
            # IDENTICAL across greedy/build-potential, i.e. all demand is booked regardless of
            # what the agent does. Anything still open at the horizon resolves unfulfilled.
            for t in self.tasks:
                if not t.resolved:
                    self._resolve(t, delivered=getattr(t, 'delivered', 0))
            return True
        return False

    def score(self):
        return compute_score_components(self.m.as_dict())["score"]

    def components(self):
        return compute_score_components(self.m.as_dict())
