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

    private static void BuildHeadless(string buildPath, BuildTarget target, BuildTargetGroup targetGroup)
    {
        Debug.Log($"[HeadlessBuild] Starting headless build for {target}");
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

        // Enable headless mode (server build)
        // This is the critical flag that makes Unity run without graphics
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;

        Debug.Log("[HeadlessBuild] Enabled Server Build (headless mode)");

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
