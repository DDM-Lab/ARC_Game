using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Developer quick start: <c>?quickstart=1</c> on the page URL (WebGL) skips the title, info and
/// tutorial screens and the day-1 tutorial, and goes straight into MainScene.
///
/// Combine with the launcher's existing parameters to also connect to the router with no clicks:
///   ?quickstart=1&amp;launcher=1&amp;key=dev-local-key&amp;config=playtest_interagent_qwen&amp;uid=alice
///
/// For testing only. A study link never carries quickstart, so participants always see the
/// normal flow. <c>uid</c> names the player in the game log (default "dev").
/// </summary>
public static class DevQuickStart
{
    const string MAIN_SCENE = "MainScene";
    static bool? active;

    /// <summary>True when the page URL asked for a quick start. Computed lazily so callers that
    /// run before this class's own initializer (the launcher, the tutorial) still see it.</summary>
    public static bool Active
    {
        get
        {
            if (active == null)
            {
                string v = ServerLauncherUI.UrlParam("quickstart");
                active = !string.IsNullOrEmpty(v) && v != "0"
                         && !v.Equals("false", System.StringComparison.OrdinalIgnoreCase);
            }
            return active.Value;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void JumpToGame()
    {
        if (Application.isBatchMode || !Active) return;
        if (SceneManager.GetActiveScene().name == MAIN_SCENE) return;
        // PlayerSession lives in the skipped TutorialScene; start the session here so the game
        // log and uploads stay attributed.
        PlayerSession.StartDevSession(ServerLauncherUI.UrlParam("uid"));
        Debug.Log("[DevQuickStart] ?quickstart=1 — skipping to " + MAIN_SCENE);
        SceneManager.LoadScene(MAIN_SCENE);
    }
}
