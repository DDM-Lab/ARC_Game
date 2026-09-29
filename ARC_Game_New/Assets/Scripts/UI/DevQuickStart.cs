using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Developer quick start: <c>?quickstart=1</c> on the page URL (WebGL) skips the title, info and
/// tutorial screens and the day-1 tutorial, and goes straight into MainScene.
///
/// Combine with the launcher's existing parameters to also connect to the router with no clicks:
///   ?quickstart=1&amp;launcher=1&amp;key=dev-local-key&amp;config=playtest_interagent_qwen&amp;uid=alice
///
/// For testing only. It is IGNORED when config.json sets testMode (participant deployments),
/// so editing the link cannot skip the consent/instruction screens. <c>uid</c> names the
/// player in the game log (default "dev").
/// </summary>
public static class DevQuickStart
{
    const string MAIN_SCENE = "MainScene";
    static bool? requested;

    /// <summary>The page URL asked for a quick start (regardless of testMode).</summary>
    static bool Requested
    {
        get
        {
            if (requested == null)
            {
                string v = ServerLauncherUI.UrlParam("quickstart");
                requested = !string.IsNullOrEmpty(v) && v != "0"
                            && !v.Equals("false", System.StringComparison.OrdinalIgnoreCase);
            }
            return requested.Value;
        }
    }

    /// <summary>True when a quick start is in effect: requested by the URL and not locked out by
    /// testMode. Read by the launcher and the tutorial, both of which run after config.json has
    /// loaded (the jump below waits for it), so the testMode check is reliable there.</summary>
    public static bool Active => Requested && !RuntimeConfig.TestMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void JumpToGame()
    {
        if (Application.isBatchMode || !Requested) return;
        // Decide only once config.json is in: testMode must be able to veto the jump.
        RuntimeConfig.WhenLoaded(() =>
        {
            if (RuntimeConfig.TestMode)
            {
                Debug.Log("[DevQuickStart] ?quickstart ignored (testMode)");
                return;
            }
            if (SceneManager.GetActiveScene().name == MAIN_SCENE) return;
            // PlayerSession lives in the skipped TutorialScene; start the session here so the
            // game log and uploads stay attributed.
            PlayerSession.StartDevSession(ServerLauncherUI.UrlParam("uid"));
            Debug.Log("[DevQuickStart] ?quickstart=1 — skipping to " + MAIN_SCENE);
            SceneManager.LoadScene(MAIN_SCENE);
        });
    }
}
