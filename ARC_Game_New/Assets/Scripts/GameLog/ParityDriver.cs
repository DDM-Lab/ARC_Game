using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Advances an unattended episode so two builds can be compared round-for-round. Pairs with
/// <see cref="ParityProbe"/>: the driver makes the game progress, the probe records what
/// happened, and `parity/diff_traces.py` compares the two traces.
///
/// IT CLICKS THE PLAYER'S BUTTON, IT DOES NOT CALL THE ENGINE. `OnExecuteButtonClicked` is
/// private on both branches, but `GlobalClock.executeButton` is a public field on both, so
/// invoking `executeButton.onClick` runs exactly the path a human click runs — same handler,
/// same order, same side effects. That matters more than convenience: any private entry point
/// we added for testing would be a code path the real game never takes, and a parity result
/// measured through it would not be about the real game.
///
/// ADVANCE ON STATE, NOT ON A TIMER. It waits for the simulation to go idle and the button to
/// become interactable, then clicks. A timer-driven driver ("click every 3 seconds") would
/// couple the episode to frame rate and machine speed, and two builds would drift apart for
/// reasons that have nothing to do with game logic — manufacturing exactly the divergence the
/// harness exists to detect. The safety timeout below is a deadlock escape, not a pacing
/// mechanism; if it ever fires the run is void and says so.
///
/// COMPILES AND RUNS ON MAIN-BUGFIXES UNCHANGED. Touches only GlobalClock.Instance,
/// .executeButton, .IsSimulationRunning() and .currentDay/.currentTimeSegment — each verified
/// present and identical on both branches — and installs itself via
/// RuntimeInitializeOnLoadMethod, so no scene edit is needed.
///
/// Off unless asked for: `-parity-drive N` (or ARC_PARITY_DRIVE=N) advances N rounds then
/// quits. Without it nothing below Install() runs.
/// </summary>
public class ParityDriver : MonoBehaviour
{
    static ParityDriver instance;

    int roundsWanted;
    int roundsDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        int n = Wanted();
        if (n <= 0) return;
        var go = new GameObject("[ParityDriver]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ParityDriver>();
        instance.roundsWanted = n;
        Debug.Log($"[ParityDriver] armed for {n} round(s).");
    }

    /// <summary>`-parity-play` / ARC_PARITY_PLAY=1: let the greedy baseline act each round.
    /// Off by default, so the clock-only episode stays available as a control.</summary>
    static bool PolicyWanted()
    {
        try
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (a == "-parity-play" || a == "--parity-play") return true;
            string env = Environment.GetEnvironmentVariable("ARC_PARITY_PLAY");
            if (!string.IsNullOrEmpty(env) && env != "0") return true;
        }
        catch (Exception) { }
        return false;
    }

    static int Wanted()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if ((args[i] == "-parity-drive" || args[i] == "--parity-drive")
                    && int.TryParse(args[i + 1], out int s)) return s;
            string env = Environment.GetEnvironmentVariable("ARC_PARITY_DRIVE");
            if (!string.IsNullOrEmpty(env) && int.TryParse(env, out int e)) return e;
        }
        catch (Exception) { }
        return 0;
    }

    IEnumerator Start()
    {
        // Let the scene finish its Awake/Start chain before touching anything. The flood layout
        // and opening task roll happen in there; clicking mid-construction would compare two
        // builds at different points in their own startup.
        yield return null;
        yield return new WaitForSecondsRealtime(2f);

        int lastDay = -1, lastSegment = -1;
        float waited = 0f;
        bool play = PolicyWanted();
        if (play) { ParityPolicy.Reset(); Debug.Log("[ParityDriver] policy ON — the greedy baseline will play this episode."); }

        while (roundsDone < roundsWanted)
        {
            var clock = GlobalClock.Instance;
            if (clock == null) { Fail("GlobalClock never appeared"); yield break; }

            // COUNT ROUNDS THE WAY THE PROBE DOES: a round is a change of (day, segment), not a
            // click. Those are not the same number. On upstream a click can be spent confirming
            // a modal, and the end-of-day prompt costs two clicks for one round — so "-parity-drive
            // 16" used to mean sixteen clicks, which was sixteen rounds on one build and eleven on
            // the other. Counting observed transitions makes the flag mean the same thing on both,
            // and makes it mean the same thing as a line in the trace.
            if (clock.currentDay != lastDay || clock.currentTimeSegment != lastSegment)
            {
                if (lastDay >= 0) roundsDone++;
                lastDay = clock.currentDay;
                lastSegment = clock.currentTimeSegment;
                waited = 0f;
                if (roundsDone > 0)
                    Debug.Log($"[ParityDriver] round {roundsDone}/{roundsWanted} "
                            + $"(day {lastDay} segment {lastSegment})");
                if (roundsDone >= roundsWanted) break;
            }

            if (waited > 120f)
            {
                Fail($"stuck at day {clock.currentDay} segment {clock.currentTimeSegment} "
                   + $"after {roundsDone} round(s)");
                yield break;
            }

            // Anything blocking the way forward gets ONE action per tick, and a dismissal IS
            // that action. Confirming the end-of-day prompt is what advances the day; clicking
            // execute in the same tick fired a second action into a game that had just changed
            // state underneath it, because `onClick.Invoke()` ignores `interactable` and the
            // freshly-disabled button fired anyway. That alone made upstream's episode wander
            // through an extra segment and skip another — a divergence manufactured entirely by
            // the harness.
            if (!DismissBlockingModal()
                && !clock.IsSimulationRunning()
                && clock.executeButton != null
                && clock.executeButton.interactable)
            {
                // Act BEFORE advancing, which is where a player acts: the planning phase between
                // rounds. Acting after the click would apply this round's decisions to next
                // round's world.
                if (play) ParityPolicy.Act();
                clock.executeButton.onClick.Invoke();
            }

            yield return new WaitForSecondsRealtime(0.25f);
            waited += 0.25f;
        }

        if (play)
            Debug.Log($"[ParityDriver] policy acted: {ParityPolicy.ConfirmedCount} confirm(s) "
                    + $"from {ParityPolicy.AttemptedCount} attempt(s).");
        Debug.Log($"[ParityDriver] done — {roundsDone} round(s). Quitting.");
        // Give ParityProbe a moment to flush its final lines before the process ends.
        yield return new WaitForSecondsRealtime(1.5f);
        Application.Quit(0);
    }

    /// <summary>
    /// Clicks through the two modals that stand between one round and the next, if either is up.
    ///
    /// The day boundary is where an unattended episode used to stop dead. Executing the last
    /// segment of a day raises a ConfirmationPopup ("End today and go to the daily report?") and
    /// then a daily report whose Next Day button is the only way onward — both waiting on a human
    /// who is not there. Five rounds was not an episode; it was day one.
    ///
    /// AGAIN, IT CLICKS THE PLAYER'S BUTTONS. Same argument as the execute button: the confirm
    /// handler is what advances the day and shows the report, and calling past it would measure
    /// a path the real game never takes. `confirmButton` is private, so it is reached by
    /// reflection — the field name is identical on both branches, and every step here is guarded
    /// so a rename degrades into "the driver stalls and says so" rather than a crash.
    ///
    /// Both builds run this identical code against identical conditions, so it cannot be a
    /// source of divergence between them.
    /// </summary>
    /// <returns>True if a modal was clicked, meaning this tick's action is spent.</returns>
    bool DismissBlockingModal()
    {
        // 1. The confirmation popup, if visible.
        try
        {
            var popup = ConfirmationPopup.Instance;
            if (popup != null)
            {
                const BindingFlags priv = BindingFlags.Instance | BindingFlags.NonPublic;
                var t = typeof(ConfirmationPopup);
                var panel = t.GetField("popupPanel", priv)?.GetValue(popup) as GameObject;
                var confirm = t.GetField("confirmButton", priv)?.GetValue(popup) as UnityEngine.UI.Button;
                if (panel != null && panel.activeInHierarchy && confirm != null && confirm.interactable)
                {
                    Debug.Log("[ParityDriver] confirming popup");
                    confirm.onClick.Invoke();
                    return true;   // one modal per tick; the next one will be up on the next
                }
            }
        }
        catch (Exception e) { Debug.LogWarning($"[ParityDriver] popup dismiss failed: {e.Message}"); }

        // 2. The daily report, once it has finished fading in. Clicking mid-transition is
        //    ignored by the manager and would just burn polls.
        try
        {
            var mgr = DailyReportManager.Instance;
            if (mgr != null && mgr.IsWaitingForNextDay() && !mgr.IsTransitioning()
                && mgr.nextDayButton != null && mgr.nextDayButton.interactable)
            {
                Debug.Log("[ParityDriver] advancing past daily report");
                mgr.nextDayButton.onClick.Invoke();
                return true;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[ParityDriver] report dismiss failed: {e.Message}"); }
        return false;
    }

    void Fail(string why)
    {
        // Loud and non-zero: a partial trace compared as if complete is worse than no result,
        // because the diff would report "parity holds" for rounds that never ran.
        Debug.LogError($"[ParityDriver] ABORTED: {why}. Trace is INCOMPLETE — do not compare it.");
        Application.Quit(3);
    }
}
