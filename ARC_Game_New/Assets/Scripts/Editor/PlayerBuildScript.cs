using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.Linq;

/// <summary>
/// Build the PLAYABLE client (graphical, full scene flow) for distribution.
///
/// Distinct from HeadlessBuildScript, which makes a headless *server* build for
/// rollout collection. This produces a normal player that connects to a remote
/// agent_router via the in-game launcher (server URL + API key entered at runtime,
/// so no config files ship with the client).
///
/// Command line (editor must NOT already be open on this project):
///   Unity -quit -batchmode -nographics -projectPath "." \
///         -executeMethod PlayerBuildScript.BuildMac   -logFile build.log
///   ...BuildWindows | BuildLinux | BuildWebGL | BuildAll
///
/// Output: Build/Client/&lt;platform&gt;/
/// </summary>
public class PlayerBuildScript
{
    // Use the scenes configured in Build Settings (enabled only), in order.
    // Falls back to an explicit list if Build Settings is somehow empty.
    static string[] Scenes()
    {
        var fromSettings = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        if (fromSettings.Length > 0) return fromSettings;
        return new[]
        {
            "Assets/Scenes/TitleScene.unity",
            "Assets/Scenes/InfoScene.unity",
            "Assets/Scenes/InstructorConfigScene.unity",
            "Assets/Scenes/TutorialScene.unity",
            "Assets/Scenes/MainScene.unity",
        };
    }

    [MenuItem("Build/Client/macOS")]
    public static void BuildMac()
    {
        BuildStandalone("Build/Client/macOS/ARC_Game.app",
                        BuildTarget.StandaloneOSX);
    }

    [MenuItem("Build/Client/Windows")]
    public static void BuildWindows()
    {
        BuildStandalone("Build/Client/Windows/ARC_Game.exe",
                        BuildTarget.StandaloneWindows64);
    }

    [MenuItem("Build/Client/Linux")]
    public static void BuildLinux()
    {
        BuildStandalone("Build/Client/Linux/ARC_Game.x86_64",
                        BuildTarget.StandaloneLinux64);
    }

    [MenuItem("Build/Client/WebGL")]
    public static void BuildWebGL()
    {
        // Full-window, high-DPI template (Assets/WebGLTemplates/ARC) so the game
        // fills the browser and renders crisply on Retina instead of stretching a
        // fixed 900x640 canvas. "PROJECT:" namespaces a template under Assets/.
        PlayerSettings.WebGL.template = "PROJECT:ARC";
        Build("Build/Client/WebGL", BuildTarget.WebGL,
              BuildTargetGroup.WebGL, isStandalone: false);
    }

    [MenuItem("Build/Client/ALL (installed targets)")]
    public static void BuildAll()
    {
        BuildMac();
        BuildWindows();
        BuildLinux();
        BuildWebGL();
    }

    static void BuildStandalone(string path, BuildTarget target)
    {
        // Make sure we produce a normal PLAYER, not a headless Server build
        // (HeadlessBuildScript flips this to Server; reset it here).
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
        Build(path, target, BuildTargetGroup.Standalone, isStandalone: true);
    }

    static void Build(string path, BuildTarget target,
                      BuildTargetGroup group, bool isStandalone)
    {
        Debug.Log($"[PlayerBuild] Building {target} -> {path}");

        var options = new BuildPlayerOptions
        {
            scenes = Scenes(),
            locationPathName = path,
            target = target,
            targetGroup = group,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[PlayerBuild] ✓ {target} succeeded — "
                      + $"{summary.totalSize / (1024 * 1024)} MB in "
                      + $"{summary.totalTime.TotalSeconds:F1}s -> {path}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[PlayerBuild] ✗ {target} failed: {summary.result}");
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        Debug.LogError($"  - {m.content}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
