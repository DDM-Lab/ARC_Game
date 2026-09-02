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
#   Food    4    foodResolved 19/19, 18/18, 19/19 -- 2 and 3 rounds are too short, and the
#               tasks answered in the last rounds then resolve inside the episode when
#               Unity's do not; 5 makes no further difference, so 4 is the boundary
#
# There is no single value that fits both, which is the evidence that they are separate
# mechanics rather than one mechanic with a tuning constant. A relocation moves people by
# vehicle to a destination that already exists; a food request has to be filled from a
# kitchen's stock, and frequently is not filled before the task expires -- which is exactly
# why Unity's foodFulfilled sits so far below foodResolved.
DEFERRED_LATENCY = {"Lodging": 1, "Food": 4}
DEFAULT_LATENCY = 2


class Task:
    """One live task instance."""

    __slots__ = ("task_id", "tag", "demand", "delivered", "rounds_remaining",
                 "resolved", "chosen", "destination")

    def __init__(self, task_id, tag, demand=0, rounds_remaining=1):
        self.task_id = task_id
        self.tag = tag                      # "Food" | "Lodging" | "None"
        self.demand = demand                # people, for Lodging; 0 for Food (legacy path)
        self.delivered = 0
        self.rounds_remaining = rounds_remaining
        self.resolved = False
        self.chosen = None
        self.destination = ""

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

    __slots__ = ("active", "deliveries", "next_id", "awaiting", "has_supplier")

    def __init__(self, has_supplier=None):
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

    def clone(self):
        b = TaskBoard.__new__(TaskBoard)
        b.active = {k: v.clone() for k, v in self.active.items()}
        b.deliveries = [list(d) for d in self.deliveries]
        b.awaiting = {k: v.clone() for k, v in self.awaiting.items()}
        b.next_id = self.next_id
        b.has_supplier = self.has_supplier
        return b

    # ── lifecycle ───────────────────────────────────────────────────────────────────
    def add(self, task: Task) -> Task:
        self.active[task.task_id] = task
        return task

    def answer(self, task_id, quantity=0, immediate=True, latency=None, destination="",
               counters=None):
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
        if quantity <= 0:
            # Nothing delivered: the task still resolves, unfulfilled, right away.
            if counters is not None:
                self.resolve(task, fulfilled=False, counters=counters)
            return
        if immediate:
            task.delivered += quantity
            if counters is not None:
                self.resolve(task, fulfilled=True, counters=counters)
            return
        if latency is None:
            latency = DEFERRED_LATENCY.get(task.tag, DEFAULT_LATENCY)
        self.awaiting[task_id] = task
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
        for entry in self.deliveries:
            entry[0] -= 1
        arriving = [d for d in self.deliveries if d[0] <= 0]
        self.deliveries = [d for d in self.deliveries if d[0] > 0]
        landed = []
        for _rounds, task_id, quantity in arriving:
            task = self.active.get(task_id) or self.awaiting.pop(task_id, None)
            if task is None:
                continue
            if task.resolved:
                self.late_delivery(task, quantity, counters)
            else:
                task.delivered += quantity
                if quantity > 0:
                    landed.append(task_id)
                # The delivery becoming due is what resolves an ANSWERED task -- fulfilled
                # if anything actually arrived, unfulfilled if the order could not be
                # sourced.
                if task_id not in self.active:
                    self.resolve(task, fulfilled=quantity > 0, counters=counters)
        return landed

    def tick(self, counters: dict) -> list:
        """One round: land deliveries, then age tasks and expire the exhausted ones.

        Deliveries land FIRST so a delivery arriving on the same round the task expires is
        credited as fulfilment rather than lost -- which is what the late-delivery path
        exists to handle when the ordering goes the other way."""
        landed = []
        for entry in self.deliveries:
            entry[0] -= 1
        arriving = [d for d in self.deliveries if d[0] <= 0]
        self.deliveries = [d for d in self.deliveries if d[0] > 0]
        for _rounds, task_id, quantity in arriving:
            task = self.active.get(task_id)
            if task is None:
                continue
            if task.resolved:
                self.late_delivery(task, quantity, counters)
            else:
                task.delivered += quantity
                landed.append(task_id)

        expired = []
        for task in list(self.active.values()):
            task.rounds_remaining -= 1
            if task.rounds_remaining <= 0:
                # An expired task resolves UNFULFILLED, but a lodging task still credits
                # whatever actually got delivered -- resolved counts demand either way.
                self.resolve(task, fulfilled=False, counters=counters)
                expired.append(task.task_id)
                del self.active[task.task_id]
        return expired

    def complete(self, task_id, counters: dict) -> None:
        """TaskSystem.CompleteTask -- resolution with fulfilled=True."""
        task = self.active.pop(task_id, None)
        if task is not None:
            self.resolve(task, fulfilled=True, counters=counters)
