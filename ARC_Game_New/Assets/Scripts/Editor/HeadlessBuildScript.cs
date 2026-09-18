using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System;

/// <summary>
/// Automated build script for creating headless Unity builds for rollout collection.
///
/// Usage from command line:
///   Windows build:
///     Unity.exe -quit -batchmode -projectPath "." -executeMethod HeadlessBuildScript.BuildWindows
///
///   Linux build (for clusters):
///     Unity.exe -quit -batchmode -projectPath "." -executeMethod HeadlessBuildScript.BuildLinux
/// </summary>
public class HeadlessBuildScript
{
    private static readonly string[] scenes = new string[]
    {
        "Assets/Scenes/MainScene.unity"
    };

    [MenuItem("Build/Headless Windows")]
    public static void BuildWindows()
    {
        string buildPath = "Build/Headless/Windows/ARC_Headless.exe";
        BuildHeadless(buildPath, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone);
    }

    [MenuItem("Build/Headless Linux")]
    public static void BuildLinux()
    {
        string buildPath = "Build/Headless/Linux/ARC_Headless.x86_64";
        BuildHeadless(buildPath, BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone);
    }

    [MenuItem("Build/Headless macOS")]
    public static void BuildMacOS()
    {
        string buildPath = "Build/Headless/macOS/ARC_Headless.app";
        BuildHeadless(buildPath, BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone);
    }

    // ── Render-capable headless build ──────────────────────────────────────────
    // The normal headless builds use the Dedicated Server subtarget, which STRIPS
    // all rendering modules — so Camera.Render()/ReadPixels cannot produce an image
    // there no matter the runtime flags. The render-capable build below is a normal
    // standalone Player build (graphics modules kept). Run it WITHOUT -nographics so
    // a GPU device exists for offscreen RenderTexture capture. It is otherwise
    // identical (same scene, same -gym-server entry point) and a bit larger/slower
    // to launch, so it's a separate target used only when frame capture is wanted.
    [MenuItem("Build/Headless macOS (Render)")]
    public static void BuildMacOSRender()
    {
        string buildPath = "Build/HeadlessRender/macOS/ARC_HeadlessRender.app";
        BuildHeadless(buildPath, BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, server: false);
    }

    [MenuItem("Build/Headless Windows (Render)")]
    public static void BuildWindowsRender()
    {
        string buildPath = "Build/HeadlessRender/Windows/ARC_HeadlessRender.exe";
        BuildHeadless(buildPath, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, server: false);
    }

    [MenuItem("Build/Headless Linux (Render)")]
    public static void BuildLinuxRender()
    {
        string buildPath = "Build/HeadlessRender/Linux/ARC_HeadlessRender.x86_64";
        BuildHeadless(buildPath, BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, server: false);
    }

    // server=true  -> Dedicated Server subtarget (no graphics modules; max-speed default).
    // server=false -> normal standalone Player (graphics kept); required for frame
    //                 capture. Launch the Player build WITHOUT -nographics.
    private static void BuildHeadless(string buildPath, BuildTarget target, BuildTargetGroup targetGroup, bool server = true)
    {
        Debug.Log($"[HeadlessBuild] Starting {(server ? "server" : "render-capable player")} build for {target}");
        Debug.Log($"[HeadlessBuild] Output path: {buildPath}");

        // Configure build options
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = target,
            targetGroup = targetGroup,
            options = BuildOptions.None,
        };

        // Server subtarget runs without graphics (the critical flag for the fast
        // headless default). Player subtarget keeps the render pipeline so the gym
        // can capture camera frames (must then be launched without -nographics).
        EditorUserBuildSettings.standaloneBuildSubtarget =
            server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player;

        Debug.Log(server
            ? "[HeadlessBuild] Enabled Server Build (headless, no graphics)"
            : "[HeadlessBuild] Player Build (graphics kept for frame capture)");

        // Perform the build
        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[HeadlessBuild] ✓ Build succeeded!");
            Debug.Log($"[HeadlessBuild] Size: {summary.totalSize / (1024 * 1024)} MB");
            Debug.Log($"[HeadlessBuild] Time: {summary.totalTime.TotalSeconds:F1}s");
            Debug.Log($"[HeadlessBuild] Output: {buildPath}");

            // Exit with success code for automation
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        else
        {
            Debug.LogError($"[HeadlessBuild] ✗ Build failed: {summary.result}");

            // Print errors
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        Debug.LogError($"  - {message.content}");
                    }
                }
            }

            // Exit with error code for automation
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
