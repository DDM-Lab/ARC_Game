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

    /// <summary>
    /// Richer mark for mechanic ports: emits the RNG state PLUS the inputs the mechanic
    /// reads. A port test built on a separate snapshot has to guess which weather and
    /// which tile set the mechanic actually saw at that instant, and gets it wrong when
    /// anything mutates between the snapshot and the mechanic. Emitting the inputs beside
    /// the state makes the test self-contained.
    /// </summary>

    /// <summary>
    /// Which gym step (advance_time call) is currently executing. Unity's clock advances
    /// BEFORE a round simulates, so a d2r2-tagged event may belong to either step 5 or step
    /// 6 -- and every remaining replay discrepancy turns on that attribution. Stamping the
    /// step makes it arithmetic instead of inference.
    /// </summary>
    public static int GymStep = 0;

    public static void MarkContext(string label, string contextJson)
    {
        if (!Enabled) return;
        var st = UnityEngine.Random.state;
        int day = GlobalClock.Instance != null ? GlobalClock.Instance.currentDay : -1;
        int seg = GlobalClock.Instance != null ? GlobalClock.Instance.currentTimeSegment : -1;
        // frameCount rides along because the port converts a vehicle's path length into ROUNDS,
        // and that conversion needs the frames-per-round budget. I have been asserting that
        // budget is fixed; this measures it. Placed inside the existing d{day}r{seg} token so
        // every parser that matches (d\d+r\d+) keeps working.
        Debug.Log($"[RNGCTX] s{GymStep}d{day}r{seg}f{Time.frameCount} {label} {JsonUtility.ToJson(st)} {contextJson}");
    }

    public static void Mark(string label)
    {
        if (!Enabled) return;
        // Reading Random.state does NOT advance the stream, so marking is side-effect free.
        var st = UnityEngine.Random.state;
        int day = GlobalClock.Instance != null ? GlobalClock.Instance.currentDay : -1;
        int seg = GlobalClock.Instance != null ? GlobalClock.Instance.currentTimeSegment : -1;
        Debug.Log($"[RNGMARK] s{GymStep}d{day}r{seg}f{Time.frameCount} {label} {JsonUtility.ToJson(st)}");
    }
}
