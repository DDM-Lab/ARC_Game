using UnityEngine;

/// <summary>
/// Localises RNG-stream divergence between a replayed game and a restored one.
///
/// The trajectory-equivalence harness proved the two streams match at restore and one
/// round later, then differ — and that the RESTORED game draws FEWER randoms. Knowing
/// that is not enough to fix it: the draws happen across several systems inside one
/// round. Marking the stream at segment boundaries turns "somewhere in the round" into
/// "between these two marks", which is a one-run answer instead of one rebuild per guess.
///
/// Off unless ARC_SNAPSHOT_DEBUG=1, so it costs nothing in normal runs.
/// </summary>
public static class SnapshotDebug
{
    static readonly bool Enabled =
        System.Environment.GetEnvironmentVariable("ARC_SNAPSHOT_DEBUG") == "1";

    public static void Mark(string label)
    {
        if (!Enabled) return;
        // Reading Random.state does NOT advance the stream, so marking is side-effect free.
        var st = UnityEngine.Random.state;
        int day = GlobalClock.Instance != null ? GlobalClock.Instance.currentDay : -1;
        int seg = GlobalClock.Instance != null ? GlobalClock.Instance.currentTimeSegment : -1;
        Debug.Log($"[RNGMARK] d{day}r{seg} {label} {JsonUtility.ToJson(st)}");
    }
}
