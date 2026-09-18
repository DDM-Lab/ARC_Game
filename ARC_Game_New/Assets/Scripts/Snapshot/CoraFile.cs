using System;
using UnityEngine;

/// <summary>
/// The `.cora` save file: one played position, written to disk and loadable anywhere.
///
/// WHY A FILE AND NOT A REPLAY.
/// A position could in principle be described by (seed, actions) and replayed. That is not
/// reliable in THIS game and the reason is measured, not theoretical: a round is 34 moving
/// frames plus 1-14 PAUSED planning frames, and the pause length depends on how long the
/// player or agent took to answer. DeliverySystem.AssignPendingTasks is gated on Time.time,
/// which advances through the pause, so the frame a vehicle is dispatched on -- and
/// therefore which round its cargo lands in -- shifts between two runs of the same inputs.
/// Replaying a session reaches a SIMILAR position, not the same one. A snapshot describes
/// where the pieces are, so it does not care how they got there.
///
/// WHAT IS IN THE ENVELOPE, AND WHY IT IS NOT JUST THE SNAPSHOT.
/// A snapshot is only meaningful against the build and the parameter sheet it was taken
/// under. Loading a file recorded under a different sheet silently loads into different
/// mechanics -- different capacities, different flood rates, a different day count -- and
/// presents as a bug in the game rather than a mismatched file. So the envelope carries the
/// build GUID and the exact parameter JSON, and the loader REFUSES on format mismatch and
/// WARNS loudly on build or parameter mismatch.
///
/// ONE FORMAT, THREE WINGS. `snapshot` is the same payload the gym server's save_state RPC
/// returns and load_state accepts. A .cora a player sends in can therefore be replayed
/// through the headless gym and diffed against the surrogate without conversion.
/// </summary>
[Serializable]
public class CoraFile
{
    public const string FORMAT = "cora";
    public const int FORMAT_VERSION = 1;
    public const string EXTENSION = "cora";

    public string format = FORMAT;
    public int formatVersion = FORMAT_VERSION;

    /// <summary>Application.buildGUID of the build that wrote the file. Empty in the Editor.</summary>
    public string buildGUID = "";
    /// <summary>GameDataManager's "parameters in effect" JSON, verbatim.</summary>
    public string parametersInEffect = "";
    public string createdUtc = "";
    public string appVersion = "";
    public string platform = "";

    /// <summary>Free text, so a sender can say what to look at.</summary>
    public string note = "";

    /// <summary>Human-readable position, for picking a file without loading it.</summary>
    public string label = "";

    /// <summary>The GameSnapshot, as JSON. Kept as a string so this envelope round-trips
    /// through JsonUtility without needing GameSnapshot to be a serialisable field of it.</summary>
    public string snapshot = "";

    public enum Compatibility { Ok, ParametersDiffer, BuildDiffers, WrongFormat, Unreadable }

    public struct Check
    {
        public Compatibility level;
        public string message;
        public bool CanLoad => level == Compatibility.Ok
                            || level == Compatibility.ParametersDiffer
                            || level == Compatibility.BuildDiffers;
    }
}

/// <summary>
/// Reads and writes <see cref="CoraFile"/> against the live game.
///
/// THREADING: Capture/Restore touch Unity objects and JsonUtility, so every entry point here
/// must run on the main thread. That is a native-crash class in this project, not merely
/// unsafe -- see GymServerManager's action queue.
/// </summary>
public static class CoraFileIO
{
    /// <summary>Capture the current position into an envelope. Main thread only.</summary>
    public static CoraFile Capture(string note = "")
    {
        var snap = GameSnapshotManager.Capture();
        var file = new CoraFile
        {
            buildGUID = Application.buildGUID,
            parametersInEffect = GameDataManager.ParametersInEffectJson,
            createdUtc = DateTime.UtcNow.ToString("o"),
            appVersion = Application.version,
            platform = Application.platform.ToString(),
            note = note ?? "",
            label = Describe(snap),
            snapshot = JsonUtility.ToJson(snap),
        };
        return file;
    }

    /// <summary>"Day 4, round 2 - budget $58,800, satisfaction 100" -- what the file selector shows.</summary>
    static string Describe(GameSnapshot s)
    {
        if (s == null) return "";
        return $"Day {s.clock.currentDay}, round {s.clock.currentTimeSegment}"
             + $" - budget ${s.economy.currentBudget:N0}, satisfaction {s.economy.currentSatisfaction:0}";
    }

    /// <summary>A filename that sorts chronologically and says what it holds.</summary>
    public static string SuggestFilename(GameSnapshot s = null)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (s == null) return $"cora-{stamp}.{CoraFile.EXTENSION}";
        return $"cora-d{s.clock.currentDay}r{s.clock.currentTimeSegment}-{stamp}.{CoraFile.EXTENSION}";
    }

    public static string ToJson(CoraFile file) => JsonUtility.ToJson(file, true);

    public static CoraFile FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        return JsonUtility.FromJson<CoraFile>(json);
    }

    /// <summary>
    /// Decide whether a file can be loaded here, and what to warn about. Never throws --
    /// an unreadable file is a result, not an exception, because this runs off a file the
    /// user picked and a stack trace is not an answer for them.
    /// </summary>
    public static CoraFile.Check Inspect(CoraFile file)
    {
        if (file == null)
            return new CoraFile.Check { level = CoraFile.Compatibility.Unreadable,
                                        message = "Not a .cora file (could not be parsed)." };
        if (file.format != CoraFile.FORMAT)
            return new CoraFile.Check { level = CoraFile.Compatibility.WrongFormat,
                                        message = $"Not a .cora file (format=\"{file.format}\")." };
        if (file.formatVersion != CoraFile.FORMAT_VERSION)
            return new CoraFile.Check { level = CoraFile.Compatibility.WrongFormat,
                                        message = $"This file is .cora v{file.formatVersion}; this build reads v{CoraFile.FORMAT_VERSION}." };
        if (string.IsNullOrEmpty(file.snapshot))
            return new CoraFile.Check { level = CoraFile.Compatibility.Unreadable,
                                        message = "The file carries no snapshot." };

        // Parameters first: a sheet mismatch changes the MECHANICS, so it is the more
        // misleading of the two and is reported in preference to a build difference.
        string here = GameDataManager.ParametersInEffectJson ?? "";
        if (!string.IsNullOrEmpty(file.parametersInEffect) && !string.IsNullOrEmpty(here)
            && file.parametersInEffect != here)
            return new CoraFile.Check { level = CoraFile.Compatibility.ParametersDiffer,
                                        message = "This file was recorded under a DIFFERENT parameter sheet. "
                                                + "It will load, but capacities, flood rates and the day count "
                                                + "may not match what the sender saw." };

        if (!string.IsNullOrEmpty(file.buildGUID) && !string.IsNullOrEmpty(Application.buildGUID)
            && file.buildGUID != Application.buildGUID)
            return new CoraFile.Check { level = CoraFile.Compatibility.BuildDiffers,
                                        message = "This file was recorded on a different build. It will load, "
                                                + "but game logic may have changed since." };

        return new CoraFile.Check { level = CoraFile.Compatibility.Ok, message = "" };
    }

    /// <summary>
    /// Parse the envelope's snapshot. The caller restores it -- in the GUI through
    /// CoraSaveLoad, in the headless wing through the gym's load_state, which resets the
    /// scene first. Restoring onto a live scene leaves stale objects the snapshot never
    /// mentions, so neither path calls GameSnapshotManager.Restore directly.
    /// </summary>
    public static GameSnapshot ReadSnapshot(CoraFile file)
    {
        if (file == null || string.IsNullOrEmpty(file.snapshot)) return null;
        return GameSnapshotManager.FromJson(file.snapshot);
    }
}
