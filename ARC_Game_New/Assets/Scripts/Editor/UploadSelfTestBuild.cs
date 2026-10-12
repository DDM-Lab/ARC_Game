using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the upload self-test: a WebGL development build of one temporary scene holding only
/// UploadSelfTest, which drives LogSender + CoraUpload.jslib through network faults. Uses the
/// project's own WebGL template and settings and changes neither; the temporary scene is deleted
/// afterwards.
///
///   Unity -batchmode -quit -nographics -projectPath . -buildTarget WebGL \
///         -executeMethod UploadSelfTestBuild.Build [-selfTestOutput Build/UploadSelfTest]
///
/// Then: python tests/upload_browser/run_webgl_selftest.py --build-dir Build/UploadSelfTest
/// </summary>
public static class UploadSelfTestBuild
{
    const string ScenePath = "Assets/UploadSelfTest_Temp.unity";

    [MenuItem("Build/Upload Self-Test (WebGL)")]
    public static void Build()
    {
        string output = ArgValue("-selfTestOutput") ?? "Build/UploadSelfTest";

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        new GameObject("UploadSelfTest").AddComponent<UploadSelfTest>();
        EditorSceneManager.SaveScene(scene, ScenePath);

        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.Development,
            });
        }
        finally
        {
            AssetDatabase.DeleteAsset(ScenePath);
        }

        Debug.Log($"[UploadSelfTestBuild] {report.summary.result}: {output} " +
                  $"({report.summary.totalErrors} errors, {report.summary.totalTime.TotalSeconds:F0} s)");
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Upload self-test build failed: " + report.summary.result);   // batchmode exit code 1
    }

    static string ArgValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
