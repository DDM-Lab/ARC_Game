"""Tasks, deliveries and the six fulfilment counters.

This is the half of the score the economy does not own: foodResolved/foodFulfilled,
lodgingResolved/lodgingFulfilled, caseworkRequested/caseworkProcessed. Everything here
funnels through RewardMetricsTracker.RecordTaskResolution and its two late-delivery
variants, so those three methods ARE the specification.

THE ASYMMETRY THAT DRIVES EVERYTHING. Food and lodging are scored on different units, and
it is deliberate in the C#, not an accident to be smoothed over:

    LODGING  demandQuantity = max(deliveryQuantity) over the choices that deliver, so
             resolved and fulfilled are counted in PEOPLE. A relocation task credits 100
             resolved whether or not anyone moves.
    FOOD     demandQuantity stays 0 -- the comment in TaskSystem is explicit that food
             delivered quantity is not tracked -- so it falls back to the LEGACY per-task
             path: 1 resolved per task, 1 fulfilled if it completed.

A port that treats them alike gets lodging off by ~100x or food off by ~1/100th, and
because both feed clamped RATIOS the error hides inside a plausible-looking score.

RESOLUTION IS NOT DISAPPEARANCE. Measured on captures: three food tasks left the active
list in one round while foodResolved rose by 1, and in the next round no food task left
while foodResolved rose by 2. Food resolves when its DELIVERY lands, not when the choice is
made, which is why the delivery pipeline is part of this module rather than an optimisation
on top of it.
"""
from __future__ import annotations

FULFILMENT_COUNTERS = ("foodResolved", "foodFulfilled", "lodgingResolved",
                       "lodgingFulfilled", "caseworkRequested", "caseworkProcessed")

# DEFERRED DELIVERY LATENCY, BY TAG -- measured, not assumed, and the two genuinely differ.
# Sweeping a single shared latency against captured counters makes one tag exact and the
# other wrong, every time:
#
#   Lodging 1   lodgingFulfilled 600/600 exact on every trace
#   Food    4    from the distribution comparison, and it is KEPT even though the exact
#               replay wants 1-2. That contradiction is diagnostic, not a tuning problem:
#               no single latency satisfies both because the real constraint is not a
#               delay at all. Unity resolves exactly ONE food delivery per round even with
#               three orders outstanding and a kitchen holding enough for two -- its
#               delivery FLEET serialises them. A latency short enough to match the first
#               resolution then lets all three land at once, and a latency long enough to
#               spread them out delays the first. Modelling the fleet is the fix; picking
#               a number between them is not.
#
# There is no single value that fits both, which is the evidence that they are separate
# mechanics rather than one mechanic with a tuning constant. A relocation moves people by
# vehicle to a destination that already exists; a food request has to be filled from a
# kitchen's stock, and frequently is not filled before the task expires -- which is exactly
# why Unity's foodFulfilled sits so far below foodResolved.
from . import roads
from .roads import Fleet, path_length

DEFERRED_LATENCY = {"Lodging": 1, "Food": 4}

# THE DELIVERY FLEET. DeliverySystem ships `ervCount` vehicles (3), CreateDeliveryTask
# splits an order into trips of at most GetMaxVehicleCapacityForCargo (100), and trips wait
# in pendingTasks until a vehicle is free. That queue is why Unity resolves exactly ONE
# food delivery on the round three orders are outstanding against a kitchen holding enough
# for two -- the fleet serialises them. Without it no single latency can fit: short enough
# to match the first resolution lets all three land together, long enough to spread them
# delays the first.
VEHICLE_COUNT = 3
VEHICLE_CAPACITY = 100
DEFAULT_LATENCY = 2


class Task:
    """One live task instance."""

    __slots__ = ("task_id", "tag", "demand", "delivered", "rounds_remaining",
                 "resolved", "chosen", "destination", "source", "fresh", "chosen_id")

    def __init__(self, task_id, tag, demand=0, rounds_remaining=1):
        self.task_id = task_id
        self.tag = tag                      # "Food" | "Lodging" | "None"
        self.demand = demand                # people, for Lodging; 0 for Food (legacy path)
        self.delivered = 0
        self.rounds_remaining = rounds_remaining
        self.resolved = False
        self.chosen = None
        self.destination = ""
        self.source = ""            # facility the people or goods come FROM
        self.fresh = True           # created this round; not aged until the next one
        self.chosen_id = None       # which choice was answered, for arrival-time sourcing

    def clone(self):
        t = Task.__new__(Task)
        for f in Task.__slots__:
            setattr(t, f, getattr(self, f))
        return t


def is_immediate(choice: dict) -> bool:
    """Does this choice deliver in the SAME round, or queue a delivery that lands later?

    Reads `immediateDelivery` when the capture carries it, and falls back to the choice
    text for older captures. The flag now ships in the game_state export precisely because
    the text heuristic was measurably wrong: inferring it from "(immediate)" / "Rapid
    Response" is right for food and wrong for lodging, which sent lodgingFulfilled from
    600/600 to 200/600 while fixing food. Two mechanics that look alike in prose are
    distinct in the data, so the port reads the flag rather than the prose.
    """
    if "immediateDelivery" in choice:
        return bool(choice["immediateDelivery"])
    text = (choice.get("choiceText") or "").lower()
    return "immediate" in text or "rapid response" in text


def demand_of(task_state: dict, tag: str) -> int:
    """TaskSystem's rule: Lodging demand is the largest delivering choice quantity.

    Scoped to Lodging on purpose -- Food deliberately keeps demandQuantity 0 so the tracker
    falls back to per-task counting."""
    if tag != "Lodging":
        return 0
    best = 0
    for c in task_state.get("choices") or []:
        q = c.get("deliveryQuantity") or 0
        if q > 0:
            best = max(best, q)
    return best


class TaskBoard:
    """Active tasks plus in-flight deliveries."""

    __slots__ = ("active", "deliveries", "next_id", "awaiting", "has_supplier",
                 "_sources", "queue", "busy", "fleet", "cell_for", "repair_for",
                 "retry_if_unsourced", "pending", "pending_seq", "flooded")

    def __init__(self, has_supplier=None, cell_for=None):
        self.active = {}                    # task_id -> Task
        self.deliveries = []                # [rounds_remaining, task_id, quantity]
        self.awaiting = {}                  # answered, off the board, not yet resolved
        self.next_id = 1
        # Can a DEFERRED delivery actually be sourced? A "Request N meals from Kitchens"
        # choice needs an operational kitchen holding stock; with none, the delivery never
        # arrives and the task resolves unfulfilled. Measured, and it is the whole
        # explanation for food's low fulfilment: across three 32-round captures
        # foodFulfilled was 7, 5 and 7 -- exactly the number of IMMEDIATE (external,
        # Rapid-Response) food choices, and never once a kitchen order. Not one kitchen was
        # operational in any of those episodes.
        self.has_supplier = has_supplier or (lambda tag: False)
        self._sources = {}          # answered task -> facility its people leave from
        self.queue = []             # trips waiting for a vehicle: [task_id, quantity]
        self.busy = 0               # vehicles currently out
        # THE REAL TRAVEL MODEL. DEFERRED_LATENCY below is a fitted constant and behaves
        # like one -- every value that matched one metric broke another, because a delivery
        # takes as long as the drive takes. Given a cell_for resolver the fleet replaces the
        # constant with the measured drive: two legs of flood-aware A* on the road grid,
        # from wherever the assigned vehicle last parked.
        self.fleet = Fleet()
        self.cell_for = cell_for
        # task_id -> vehicle index, for the repair task a flood-damaged vehicle spawns.
        # Without it a vehicle damaged once is out for the rest of the episode, the fleet
        # drains to nothing, and every later delivery silently reverts to the fitted
        # constant -- which is exactly what the port did before this: 87 of 168 fallbacks
        # happened with all three vehicles damaged.
        self.repair_for = {}
        # Injected: how much of `quantity` the source can actually supply right now.
        # None disables the check, which is what the pure-task suites want.
        self.retry_if_unsourced = None
        # Orders waiting for a vehicle, exactly DeliverySystem.pendingTasks. Entries are
        # [seq, (task_id, quantity, destination), src_cell, dst_cell, quantity], sorted by
        # creation order -- both handlers use priority 3, so priority never breaks a tie.
        self.pending = []
        self.pending_seq = 0
        self.flooded = frozenset()

    def clone(self):
        b = TaskBoard.__new__(TaskBoard)
        b.active = {k: v.clone() for k, v in self.active.items()}
        b.deliveries = [list(d) for d in self.deliveries]
        b.awaiting = {k: v.clone() for k, v in self.awaiting.items()}
        b.next_id = self.next_id
        b.has_supplier = self.has_supplier
        b._sources = dict(self._sources)
        b.queue = [list(q) for q in self.queue]
        b.busy = self.busy
        b.fleet = self.fleet.clone()
        b.cell_for = self.cell_for
        b.repair_for = dict(self.repair_for)
        b.retry_if_unsourced = self.retry_if_unsourced
        b.pending = [list(x) for x in self.pending]
        b.pending_seq = self.pending_seq
        b.flooded = self.flooded
        return b


    def travel_rounds(self, source_name, dest_name, flooded=frozenset(),
                      quantity=0, task_id=None, segment=None):
        """Rounds for a delivery from `source_name` to `dest_name`, or None.

        None means "no opinion" -- the caller falls back to DEFERRED_LATENCY -- EXCEPT when
        the flood has cut the route, which returns False, because a cut route is not a slow
        delivery but one that never arrives at all (Unity bails to StopVehicleDueToFlood).
        """
        if self.cell_for is None:
            return None
        src = self.cell_for(source_name)
        dst = self.cell_for(dest_name)
        if src is None or dst is None:
            return None
        # ROUTE FIRST, VEHICLE SECOND. CreateDeliveryTask calls GetDeliveryTimeEstimate and
        # bails with "Cannot create delivery task - no route available" BEFORE any vehicle is
        # involved, so an unroutable order simply never becomes a delivery -- nothing is
        # dispatched and nothing is damaged. The port was assigning a vehicle first and
        # damaging it on the failed path, which is StopVehicleDueToFlood's behaviour for a
        # vehicle already EN ROUTE, not for an order that was never created.
        #
        # It wrecked the whole fleet. On staff_5701 round 5 the port issued three food
        # orders from the kitchen, two of them across a corridor the flood had cut at
        # (-5, 0); each "blocked" one damaged a vehicle, so all three were out and the two
        # relocations that follow fell back to the fitted constant. Unity created one food
        # delivery, refused the other two silently, and kept its fleet.
        if path_length(src, dst, flooded) is None:
            return False
        v = self.fleet.best_vehicle(src, quantity)
        wait = 0
        if v is None:
            # Nothing free RIGHT NOW is not the same as no opinion. pendingTasks holds the
            # trip until a vehicle lands, so the cost is that wait plus the drive.
            v, wait = self.fleet.soonest_free()
            if v is None:
                return None      # every vehicle damaged; caller falls back
        if not self.fleet.dispatch(v, task_id, src, dst, flooded):
            # StopVehicleDueToFlood spawns a repair task through
            # FloodTaskGenerator.CreateVehicleRepairTask: Emergency, roundsRemaining 2, two
            # choices -- repair now for $1200, or delay for -5 satisfaction. The vehicle
            # stays out of service until choice 1 is answered.
            self.open_repair_task(v)
            return False         # route cut: order dropped, vehicle damaged
        # The clock already includes everything queued ahead of this trip.
        seconds = self.fleet.busy_seconds[v]
        # Occupancy is REAL: the vehicle stays out for the whole drive and is not available
        # for the next order. Zeroing it here (as the first cut of this did) made
        # best_vehicle always return vehicle 0 and silently removed the fleet limit.
        # Round UP: a trip needing any part of a round has not landed by the end of it.
        # Rounds are SECONDS over seconds. A round simulates ROUND_SECONDS of game time, so
        # a trip needing less than that lands inside the round it was ordered in, and one
        # needing twice that takes two more rounds. No frame budget and no per-segment
        # table: every round simulates the same duration.
        from math import floor
        return int(floor(seconds / roads.ROUND_SECONDS))


    REPAIR_COST = 1200          # AgentChoice(1, "Repair immediately ($1200)")
    REPAIR_DELAY_SATISFACTION = -5   # AgentChoice(2, "Delay repair ...")
    REPAIR_ROUNDS = 2           # repairTask.roundsRemaining

    def open_repair_task(self, vehicle):
        """CreateVehicleRepairTask, including its de-duplication.

        The C# refuses to create a second repair task for a vehicle that already has one,
        matching on the vehicle name in the description; the port matches on the index.
        """
        if vehicle in self.repair_for.values():
            return None
        task = Task(self.next_id, "Repair", 0, self.REPAIR_ROUNDS)
        self.next_id += 1
        self.add(task)
        self.repair_for[task.task_id] = vehicle
        return task

    def answer_repair(self, task_id, choice_id, counters=None):
        """ApplyChoiceImpacts' repair branch, which the HEADLESS path also reaches.

        Repair lives in TaskDetailUI, which reads like a GUI-only path, but
        SelectTaskChoiceHeadless routes the gym through the same CompleteTaskAction ->
        ApplyChoiceImpacts, so the headless server really does repair. Only choiceId 1
        repairs; choice 2 leaves the vehicle damaged and costs satisfaction.
        """
        vehicle = self.repair_for.pop(task_id, None)
        if vehicle is None:
            return False
        self.active.pop(task_id, None)
        if choice_id == 1:
            self.fleet.repair(vehicle)
            return True
        return False

    # ── lifecycle ───────────────────────────────────────────────────────────────────
    def add(self, task: Task) -> Task:
        self.active[task.task_id] = task
        return task

    def answer(self, task_id, quantity=0, immediate=True, latency=None, destination="",
               counters=None, latency_measured=False, destination_facility=None):
        """Answer a task: it leaves the board NOW and resolves when its delivery LANDS.

        THE TWO ARE NOT THE SAME ROUND, and that is the whole point. Measured on captures:
        three food tasks left the active list in one round while foodResolved rose by 0,
        and the next round none left while it rose by 3. Answering removes the task from
        the player's view; RecordTaskResolution fires later, from the delivery handler.

        An immediate delivery collapses the two into one round, which is why lodging looked
        like it resolved on disappearance and food did not. Same rule, different latency.

        The consequence is real, not bookkeeping: tasks answered near the end of an episode
        never resolve at all, so their demand is never credited. Over one 32-round capture
        that is 21 food tasks answered and 19 resolved."""
        task = self.active.pop(task_id, None)
        if task is None:
            return
        task.chosen = quantity
        task.destination = destination
        self._sources = getattr(self, "_sources", {})
        if task.source:
            self._sources[task_id] = task.source
        if quantity <= 0:
            # Nothing could be sourced -- but the ORDER was still placed, and it fails on
            # the round it was due rather than the instant it was made. Resolving inline
            # credited foodResolved in the same round the choice was answered and put the
            # port a round ahead of Unity on every capture (unity 0, port 3 at round 4).
            latency = DEFERRED_LATENCY.get(task.tag, DEFAULT_LATENCY)
            self.awaiting[task_id] = task
            self.deliveries.append([latency, task_id, 0, True])
            return
        if immediate:
            # The delivery itself is a teleport -- the goods or people arrive at once, and
            # the caller applies that side effect immediately. RESOLUTION still happens on
            # the next tick, because RecordTaskResolution fires from the delivery-completion
            # path rather than from the click. Measured: Unity's foodResolved is still 0 on
            # the round its first food task is answered and only moves the round after, so
            # resolving inline put the port a full round ahead on every capture.
            task.delivered += quantity
            self.awaiting[task_id] = task
            self.deliveries.append([1, task_id, 0, True])   # 0: already delivered, resolve only
            return
        # THE ORDER GOES INTO THE QUEUE, NOT ONTO A CLOCK. DeliverySystem creates the task
        # into pendingTasks and ProcessPendingTasks assigns it during the simulated round,
        # so a delivery has no latency of its own -- it has a place in a line and a drive.
        # Costing it at answer time is what made the port land five orders in the round
        # Unity landed two.
        src = self.cell_for(getattr(task, "source", "") or "") if self.cell_for else None
        dst = self.cell_for(destination_facility or "") if self.cell_for else None
        if src is not None and dst is not None and quantity > 0:
            self.awaiting[task_id] = task
            self.pending.append([self.pending_seq, (task_id, quantity, destination),
                                 src, dst, quantity])
            self.pending_seq += 1
            return
        if latency is None:
            latency = DEFERRED_LATENCY.get(task.tag, DEFAULT_LATENCY)
        self.awaiting[task_id] = task
        # Split into vehicle-sized trips and queue them; dispatch happens in tick() as
        # vehicles free up, exactly as pendingTasks drains in DeliverySystem.
        #
        # ONLY when the caller had no measured travel time. This queue was built BEFORE the
        # road graph was ported, as a stand-in for the serialisation the fleet imposes, and
        # its entries carry the fresh flag -- so a trip routed through it waits a round for
        # dispatch ON TOP of its latency. Once travel_rounds supplies a real drive that
        # double-counts: three food orders answered at round 5 with measured latencies of
        # 1, 2 and 1 all landed a round late, which is precisely the round-5 foodResolved
        # divergence (unity 1, port 0) that has stood since this suite was written.
        # Fleet occupancy and queue wait are now modelled inside travel_rounds itself.
        remaining = quantity if self.has_supplier(task.tag) else 0
        if quantity > 0 and not latency_measured:
            trips = max(1, -(-quantity // VEHICLE_CAPACITY))
            per = quantity // trips
            for i in range(trips):
                q = per if i < trips - 1 else quantity - per * (trips - 1)
                self.queue.append([task_id, q if remaining else 0, latency])
            return
        # A delivery that cannot be sourced still takes the same time to FAIL as a real one
        # takes to arrive -- the request goes out, nothing comes back, and the task resolves
        # unfulfilled on the round the delivery was due. Resolving it instantly instead
        # over-counts resolved by whatever is still in flight when the episode ends: 21
        # against Unity's 19, on a 32-round capture where the last three were answered in
        # the final rounds.
        self.deliveries.append([latency, task_id,
                                quantity if self.has_supplier(task.tag) else 0])

    def choose(self, task_id, quantity=0, immediate=True, latency=None, destination=""):
        """Answer a task's choice.

        `immediate` options deliver in the same round; deferred ones enter the delivery
        queue and land `latency` rounds later. That distinction is the whole reason a
        cheap deferred option can score worse than an expensive immediate one -- the
        deferred delivery can arrive after the task has already resolved unfulfilled, at
        which point it is credited by the LATE path with its own capping rules."""
        task = self.active.get(task_id)
        if task is None:
            return
        task.chosen = quantity
        task.destination = destination
        self._sources = getattr(self, "_sources", {})
        if task.source:
            self._sources[task_id] = task.source
        if quantity <= 0:
            return
        if immediate:
            task.delivered += quantity
        else:
            if latency is None:
                latency = DEFERRED_LATENCY.get(task.tag, DEFAULT_LATENCY)
            self.deliveries.append([latency, task_id, quantity])

    def resolve(self, task: Task, fulfilled: bool, counters: dict) -> None:
        """RewardMetricsTracker.RecordTaskResolution, transcribed.

        Only Food and Lodging tasks touch the counters at all; advisories and worker
        notices resolve silently."""
        if task.resolved or task.tag not in ("Food", "Lodging"):
            task.resolved = True
            return
        demand = task.demand
        delivered = max(0, min(task.delivered, max(demand, task.delivered)))
        resolved_add = demand if demand > 0 else 1
        fulfilled_add = (min(delivered, demand) if demand > 0
                         else (1 if fulfilled else 0))
        if task.tag == "Food":
            counters["foodResolved"] += resolved_add
            counters["foodFulfilled"] += fulfilled_add
        else:
            counters["lodgingResolved"] += resolved_add
            counters["lodgingFulfilled"] += fulfilled_add
        task.resolved = True

    def late_delivery(self, task: Task, quantity: int, counters: dict) -> None:
        """AddLateDelivery / AddLateFoodTask -- a delivery that lands after resolution.

        The two are NOT the same operation and the C# comment is emphatic about it: lodging
        credits the PACK/PEOPLE count, food credits exactly +1 because food is on the
        per-task metric and crediting its quantity would corrupt the rate. Both cap
        fulfilled at resolved."""
        if quantity <= 0:
            return
        if task.tag == "Food":
            counters["foodFulfilled"] = min(counters["foodResolved"],
                                            counters["foodFulfilled"] + 1)
        elif task.tag == "Lodging":
            counters["lodgingFulfilled"] = min(counters["lodgingResolved"],
                                               counters["lodgingFulfilled"] + quantity)

    def tick_deliveries_only(self, counters: dict) -> list:
        """Land due deliveries without ageing tasks.

        Used where task expiry is driven externally (the equivalence test replays Unity's
        own resolution events) so that delivery latency is still modelled while timing is
        not double-counted."""
        # Dispatch queued trips to any free vehicle before ageing, so a trip that waited a
        # round starts the moment one lands.
        # ProcessPendingTasks for one round: orders leave the queue as vehicles free up,
        # and whatever finishes inside the round's seconds lands now.
        landed_now = []
        def _load(payload, qty):
            """LoadCargo at the moment the vehicle reaches the source."""
            if self.retry_if_unsourced is None:
                return qty
            task = self.active.get(payload[0]) or self.awaiting.get(payload[0])
            return qty if task is None else self.retry_if_unsourced(task, qty)

        arrived, self.pending = self.fleet.run_round(self.pending, self.flooded, _load)
        for task_id, quantity, destination in arrived:
            task = self.active.get(task_id) or self.awaiting.pop(task_id, None)
            if task is None:
                continue
            task.delivered += quantity
            if quantity > 0:
                landed_now.append((task_id, quantity, destination))
            if task_id not in self.active and not task.resolved:
                self.resolve(task, fulfilled=task.delivered > 0, counters=counters)
        while self.queue and self.busy < VEHICLE_COUNT:
            task_id, qty, lat = self.queue.pop(0)
            self.deliveries.append([lat, task_id, qty, True])
            self.busy += 1

        # A delivery queued during THIS round is not aged by it, exactly as a task created
        # this round is not. step_round ticks the queue in the same round the choice was
        # made, so without this a latency of 1 is consumed instantly and the task resolves
        # inline -- which is what put lodgingResolved at 100 in round 4 where Unity had 0.
        for entry in self.deliveries:
            if len(entry) > 3 and entry[3]:
                entry[3] = False
                continue
            entry[0] -= 1
        arriving = [d for d in self.deliveries if d[0] <= 0]
        self.deliveries = [d for d in self.deliveries if d[0] > 0]
        self.busy = max(0, self.busy - len(arriving))     # vehicles return
        landed = list(landed_now)
        for _rounds, task_id, quantity, *_ in arriving:
            task = self.active.get(task_id) or self.awaiting.pop(task_id, None)
            if task is None:
                continue
            # LoadCargo ABORTS when the source building holds none of the cargo: it nulls
            # currentTask, sets the vehicle Idle and returns, so RunDelivery never reaches
            # the destination and OnVehicleDeliveryCompleted never fires. No completion
            # means no RecordTaskResolution -- the task is NOT resolved-unfulfilled, it
            # simply has not happened yet, and it goes again once the source restocks.
            #
            # This is the whole round-5 divergence. Unity's kitchen holds 200 and each
            # order is 100, so exactly ONE of three orders loads: foodResolved 1 at round 5
            # and 3 at round 6, with the kitchen dropping 200 -> 100 and restocking at the
            # day reset. The port resolved all three at once because it treated an
            # unsourceable delivery as a failed one. The comment above answer()'s
            # cannot-be-sourced branch says the opposite; it was written from inference
            # before LoadCargo was read, and it is wrong.
            if self.retry_if_unsourced is not None and quantity > 0:
                available = self.retry_if_unsourced(task, quantity)
                if available <= 0:
                    # It goes again next round -- but the TASK still ages, and when its
                    # rounds run out it resolves UNFULFILLED like any other expiry. That
                    # bound is what makes the counts come out: three 100-pack orders
                    # against a 200-pack kitchen give two fulfilled deliveries and one
                    # expiry, which is Unity's foodResolved 3 / foodFulfilled 2. Retrying
                    # without the bound left the third order in flight forever and the port
                    # under-resolved by up to 5 over an episode.
                    task.rounds_remaining -= 1
                    if task.rounds_remaining > 0:
                        self.awaiting[task_id] = task
                        self.deliveries.append([1, task_id, quantity])
                        continue
                    self.resolve(task, fulfilled=task.delivered > 0, counters=counters)
                    continue
            if task.resolved:
                self.late_delivery(task, quantity, counters)
            else:
                task.delivered += quantity
                if quantity > 0:
                    landed.append((task_id, quantity, task.destination))
                # The delivery becoming due is what resolves an ANSWERED task -- fulfilled
                # if anything actually arrived, unfulfilled if the order could not be
                # sourced.
                if task_id not in self.active:
                    # An immediate delivery was already credited to task.delivered when it
                    # was answered, so fulfilment is judged on the TASK, not this entry.
                    self.resolve(task, fulfilled=task.delivered > 0, counters=counters)
        return landed

    def tick(self, counters: dict) -> list:
        """One round: land due deliveries, then age tasks and expire the exhausted ones.

        Returns the landings as (task_id, quantity, destination) so the caller can turn
        them into client arrivals. Delegates the landing half to tick_deliveries_only
        rather than repeating it -- an earlier version had two copies of that logic and
        they drifted: this one looked up only `active`, so a delivery for an ANSWERED task
        (which lives in `awaiting`) was silently dropped, and it returned `expired` while
        building an unused `landed`. Deliveries never reached the round loop, so the
        surrogate could not generate its own client arrivals and looked as though the
        pipeline simply did nothing.

        Deliveries land BEFORE ageing so one arriving on the round its task expires counts
        as fulfilment rather than being lost to the late-delivery path."""
        landed = self.tick_deliveries_only(counters)

        for task in list(self.active.values()):
            if task.fresh:
                # A task generated during THIS round is not aged by it. Unity decrements in
                # OnTimeSegmentAdvanced, which fires on the NEXT segment advance, so a task
                # with roundsRemaining = 1 survives the round it was born in. Ageing it
                # immediately expired every such task on creation and put foodResolved a
                # full round ahead of Unity on all four captures.
                task.fresh = False
                continue
            task.rounds_remaining -= 1
            if task.rounds_remaining <= 0:
                # An expired task resolves UNFULFILLED, but a lodging task still credits
                # whatever actually got delivered -- resolved counts demand either way.
                self.resolve(task, fulfilled=False, counters=counters)
                del self.active[task.task_id]
        return landed

    def complete(self, task_id, counters: dict) -> None:
        """TaskSystem.CompleteTask -- resolution with fulfilled=True."""
        task = self.active.pop(task_id, None)
        if task is not None:
            self.resolve(task, fulfilled=True, counters=counters)
