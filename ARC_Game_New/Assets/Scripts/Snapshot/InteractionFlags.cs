using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records play-tester "that interaction went badly" markers, so a bad exchange with the AI
/// can be found again afterwards.
///
/// TWO SINKS, ONE JOIN KEY. Every flag is written in two places: into the `.cora` file the
/// tester saves (<see cref="CoraFile.flags"/>), and into the session transcript on the router
/// (as a client_event). Both carry the SAME <c>id</c>. That id is the point — day, round and
/// wall-clock are not unique if a tester flags twice inside one round, which is precisely the
/// situation where they are most likely to flag at all. With the id you can take any flagged
/// .cora file, find the matching transcript entries, and pull the officer exchange around it.
///
/// MARKER ONLY, BY DESIGN. A flag does NOT capture the world at that instant. Snapshot-per-flag
/// would let you load the exact moment, but each snapshot is the size of the entire save and a
/// tester may flag a dozen times in a session. The tradeoff accepted here is that replaying a
/// flagged moment means loading the .cora and seeking to that day/round, and that the officer
/// dialogue at that moment comes from the transcript rather than being re-generated (it could
/// not be re-generated faithfully anyway: the officers run at temperature 0.3, so a replay
/// produces the same game state but a different conversation).
///
/// The list lives for the session and is attached to every save, so a tester who flags three
/// times and then saves gets all three in the file — they do not have to save immediately
/// after each one.
/// </summary>
public static class InteractionFlags
{
    static readonly List<InteractionFlag> flags = new List<InteractionFlag>();

    /// <summary>Flags recorded so far this session, oldest first.</summary>
    public static IReadOnlyList<InteractionFlag> All => flags;
    public static int Count => flags.Count;

    /// <summary>Snapshot of the list for embedding in a .cora envelope.</summary>
    public static InteractionFlag[] Collected() => flags.ToArray();

    /// <summary>
    /// Record a flag at the current moment and mirror it to the router transcript.
    /// Returns the flag so a caller can show its id to the tester.
    /// </summary>
    public static InteractionFlag Flag(string officer = "", string note = "")
    {
        var f = new InteractionFlag
        {
            // Short, unique, and readable in a log line. Not a GUID: a tester may end up
            // reading this id aloud or pasting it into a bug report.
            id = "flag-" + DateTime.UtcNow.ToString("HHmmss") + "-" +
                 UnityEngine.Random.Range(0x1000, 0xFFFF).ToString("x4"),
            utc = DateTime.UtcNow.ToString("o"),
            day = CurrentDay(),
            round = CurrentRound(),
            officer = officer ?? "",
            note = note ?? "",
        };
        flags.Add(f);

        // Sink 1 — the game log, so it also rides along in the uploaded log payload even if
        // the tester never saves a .cora file.
        GameLogPanel.Instance?.LogPlayerAction(
            $"Interaction flagged: {f.id} (day {f.day}, round {f.round}"
            + (string.IsNullOrEmpty(f.officer) ? "" : $", officer {f.officer}") + ")");

        // Sink 2 — the router transcript, under the same id. Reuses the existing
        // client_event channel rather than adding a message type: the router already
        // logs these into the session jsonl, which is exactly where this belongs.
        var ws = WebSocketManager.Instance;
        if (ws != null)
            ws.SendClientEvent("play_tester", "flag_interaction",
                               $"{f.id}|day={f.day}|round={f.round}|officer={f.officer}|note={f.note}",
                               0);
        else
            Debug.LogWarning($"[InteractionFlags] {f.id} recorded locally; no router connection "
                           + "to mirror it into the transcript.");

        Debug.Log($"[InteractionFlags] {f.id} recorded (total {flags.Count})");
        return f;
    }

    static int CurrentDay()
    {
        try { return GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 0; }
        catch (Exception) { return 0; }
    }

    static int CurrentRound()
    {
        try { return GlobalClock.Instance != null ? GlobalClock.Instance.currentTimeSegment : 0; }
        catch (Exception) { return 0; }
    }
}
