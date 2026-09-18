using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A non-learning policy that PLAYS the game, so parity can be measured on episodes where
/// something actually happens.
///
/// WHY THIS EXISTS. The first parity result compared two builds across an episode in which the
/// driver only advanced the clock: nothing was ever built, deconstructed or confirmed, and the
/// facility list was empty for every round. Agreement on such an episode is close to vacuous —
/// it exercises the passive simulation and none of the paths where the two branches actually
/// differ. Ledger D1 needs a deconstruction to be reachable at all, and D5 needs a confirm.
/// A policy that does nothing cannot test either.
///
/// IT IS A PORT, NOT A NEW POLICY. The decision rule is `greedy_decision` from
/// benchmark_models.py, the repository's existing myopic reward-mirrored baseline, transcribed
/// term for term (see Value below). That matters twice over: the parity episodes are then
/// played by the same policy the benchmark reports numbers for, and nobody has to trust a
/// second hand-written heuristic. What is NOT ported is greedy's worker-assignment half, which
/// reaches the game through the gym's action enumerator; see the note on the gym below.
///
/// WHY IT IS NOT JUST DRIVEN BY THE GYM. `greedy_decision` acts through ARCGameGymEnv over TCP
/// to GymServerManager — and GymServerManager does not exist on main-bugfixes. Grafting it
/// there is not possible without editing GlobalClock (it needs `GymAdvanceRound` and
/// `gymInstantMode`, neither of which upstream has), which would mean modifying a mechanic file
/// on the reference build. Worse, `gymInstantMode` is a DIFFERENT clock path from the one a
/// human plays, so a gym-driven comparison would not be testing the game people actually play.
/// Hence the rule is ported into the driver and acts through the UI instead.
///
/// IT CLICKS THE PLAYER'S BUTTONS. `TaskDetailUI.ShowTaskDetail`, `OnChoiceSelected` and the
/// public `confirmButton` all exist identically on both branches, so the policy runs the same
/// confirm path a human runs — including whatever validation that path applies, which is
/// exactly what ledger D5 is about. (`SelectTaskChoiceHeadless`, the gym's entry point, exists
/// only on our side and is deliberately not used.)
///
/// IT MUST NEVER DRAW FROM UnityEngine.Random. Any tie-break uses a System.Random derived from
/// the episode seed. A policy that drew from the shared stream would shift every subsequent
/// flood, weather roll and task trigger — it would perturb the very thing the harness measures,
/// and the two builds would diverge because of the harness rather than because of the game.
/// </summary>
public static class ParityPolicy
{
    // Mirrors REWARD_WEIGHTS["w_food_cost"] in reward_scoring.py.
    const float W_FOOD_COST = 0.0002f;

    static System.Random tieBreak;

    /// <summary>Rounds in which the policy actually confirmed something — reported at the end so
    /// a run that silently did nothing is visibly distinguishable from a run that acted.</summary>
    public static int ConfirmedCount { get; private set; }
    public static int AttemptedCount { get; private set; }

    public static void Reset()
    {
        ConfirmedCount = 0;
        AttemptedCount = 0;
        tieBreak = EpisodeSeed.IsSeeded ? EpisodeSeed.NextSystemRandom() : new System.Random(0);
    }

    /// <summary>
    /// Act once for the current round: pick the highest-value choice on each active task and
    /// confirm it through the UI. Returns the number of confirms that went through.
    /// </summary>
    public static int Act()
    {
        var ts = TaskSystem.Instance;
        var ui = UnityEngine.Object.FindObjectOfType<TaskDetailUI>();
        if (ts == null || ui == null) return 0;

        int confirmed = 0;

        // Snapshot the list first. Confirming mutates activeTasks, and iterating the live list
        // while it changes underneath would make the policy's behaviour depend on list internals
        // rather than on the game — a difference that could show up on one build and not the
        // other for no reason worth reporting.
        var tasks = new List<GameTask>(ts.activeTasks);

        // Deterministic order. FindObjectsOfType / list order is not guaranteed to agree between
        // two builds, and the policy must make the same decisions in the same sequence on both.
        tasks.Sort((a, b) => a == null || b == null ? 0 : a.taskId.CompareTo(b.taskId));

        foreach (var task in tasks)
        {
            if (task == null || task.isExpired) continue;
            if (task.agentChoices == null || task.agentChoices.Count == 0) continue;

            AgentChoice best = BestChoice(task);
            if (best == null) continue;    // greedy skips a task whose best value is <= 0

            AttemptedCount++;
            try
            {
                ui.ShowTaskDetail(task);
                ui.OnChoiceSelected(best);
                if (ui.confirmButton != null && ui.confirmButton.interactable)
                {
                    ui.confirmButton.onClick.Invoke();
                    confirmed++;
                    ConfirmedCount++;
                }
                else if (ui.closeButton != null)
                {
                    // Confirm refused (validation, budget, workers). Close and move on: the
                    // REFUSAL is a legitimate result and belongs in the trace, not an error.
                    ui.closeButton.onClick.Invoke();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ParityPolicy] task {task.taskId} failed: {e.Message}");
            }
        }
        return confirmed;
    }

    /// <summary>
    /// `greedy_decision`'s choice rule, transcribed:
    ///
    ///     b = Budget impact, s = Satisfaction impact
    ///     if b > 0:  v = b/10000 + 0.01*s                      (a funding choice)
    ///     else:      cost = -b
    ///                acting = cost > 0 or s >= 10
    ///                v = (acting and demand ? 1 : 0) + 0.01*s - w_food_cost*cost
    ///     take argmax if its value > 0, else skip the task
    ///
    /// The `> 0` threshold is greedy's, not an accident: a task whose best option is worth
    /// nothing is left alone rather than actioned for the sake of acting.
    /// </summary>
    static AgentChoice BestChoice(GameTask task)
    {
        bool demand = task.taskType == TaskType.Demand || task.taskType == TaskType.Emergency;
        AgentChoice best = null;
        float bestV = 0f;

        foreach (var c in task.agentChoices)
        {
            if (c == null) continue;
            float b = 0f, s = 0f;
            if (c.choiceImpacts != null)
                foreach (var imp in c.choiceImpacts)
                {
                    if (imp == null) continue;
                    if (imp.impactType == ImpactType.Budget) b += imp.value;
                    else if (imp.impactType == ImpactType.Satisfaction) s += imp.value;
                }

            float v;
            if (b > 0f)
            {
                v = b / 10000f + 0.01f * s;
            }
            else
            {
                float cost = -b;
                bool acting = cost > 0f || s >= 10f;
                v = ((acting && demand) ? 1f : 0f) + 0.01f * s - W_FOOD_COST * cost;
            }

            if (v > bestV)
            {
                bestV = v;
                best = c;
            }
            else if (best != null && Mathf.Approximately(v, bestV) && tieBreak != null)
            {
                // Exact ties broken from the SEED's own generator, never the shared stream.
                if (tieBreak.Next(2) == 0) best = c;
            }
        }
        return best;
    }
}
