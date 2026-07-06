using UnityEngine;
using System;
using System.IO;

/// <summary>
/// Offscreen frame capture for the headless gym build.
///
/// Renders the map camera into a RenderTexture and encodes a PNG so a real game
/// image can be (a) embedded in LLM/VLM prompts and (b) collected into demo videos.
///
/// IMPORTANT — this only works when the player has a render pipeline available:
///   * The build must be a NORMAL standalone Player build, NOT a Dedicated Server
///     subtarget build (the Server build strips all rendering modules). See
///     HeadlessBuildScript.BuildMacOSRender.
///   * The process must be launched WITHOUT -nographics (which disables the GPU
///     device). -batchmode alone is fine and still renders offscreen on macOS.
///
/// Everything here is gated behind CaptureMode.Off (the default), so a normal
/// headless run pays nothing: Configure() is only called when the gym client opts
/// in via the "configure_render" request, and CaptureFrame() early-returns when Off.
///
/// Control modes:
///   Off         — never capture (default; preserves max headless speed)
///   PerStep     — capture once per gym step (every advance_time / round)
///   PerGameTime — capture only when the in-game day OR round changes
/// </summary>
public class GymCameraCapture : MonoBehaviour
{
    public enum CaptureMode { Off, PerStep, PerGameTime }

    public static GymCameraCapture Instance { get; private set; }

    [Header("Capture Settings")]
    public CaptureMode mode = CaptureMode.Off;
    public int width = 640;
    public int height = 360;
    public string outputDir = "render_frames";

    // The camera that frames the map. Resolved lazily from Camera.main (the
    // scene's "Main Camera" is tagged MainCamera) so we never hold a destroyed ref.
    private Camera captureCamera;

    // Reused across captures to avoid per-frame allocation churn.
    private RenderTexture renderTexture;
    private Texture2D readbackTexture;

    // Tracks the last (day, round) so PerGameTime only fires on a transition.
    private int lastDay = -1;
    private int lastSegment = -1;

    // Result of the most recent CaptureFrame(), surfaced in the gym response.
    public string LastFramePath { get; private set; }
    public string LastFrameBase64 { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Enable/configure capture at runtime (called from the gym "configure_render"
    /// request on the main thread). Safe to call repeatedly. Passing CaptureMode.Off
    /// disables capture and frees GPU resources.
    /// </summary>
    public void Configure(CaptureMode newMode, int newWidth, int newHeight, string newOutputDir)
    {
        mode = newMode;
        if (newWidth > 0) width = newWidth;
        if (newHeight > 0) height = newHeight;
        if (!string.IsNullOrEmpty(newOutputDir)) outputDir = newOutputDir;

        if (mode == CaptureMode.Off)
        {
            ReleaseResources();
            Debug.Log("[GymCapture] Capture disabled (Off)");
            return;
        }

        EnsureResources();
        Debug.Log($"[GymCapture] Configured mode={mode} {width}x{height} dir='{outputDir}'");
    }

    private void EnsureResources()
    {
        if (renderTexture == null || renderTexture.width != width || renderTexture.height != height)
        {
            ReleaseResources();
            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            renderTexture.Create();
            readbackTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        }

        try
        {
            Directory.CreateDirectory(GetAbsoluteOutputDir());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GymCapture] Could not create output dir: {e.Message}");
        }
    }

    private void ReleaseResources()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            UnityEngine.Object.Destroy(renderTexture);
            renderTexture = null;
        }
        if (readbackTexture != null)
        {
            UnityEngine.Object.Destroy(readbackTexture);
            readbackTexture = null;
        }
    }

    private string GetAbsoluteOutputDir()
    {
        if (Path.IsPathRooted(outputDir)) return outputDir;
        // Relative dirs resolve next to the working directory so the gym process,
        // which sets the cwd, finds the frames where it expects them.
        return Path.Combine(Directory.GetCurrentDirectory(), outputDir);
    }

    private Camera ResolveCamera()
    {
        // Use Unity's null overload (not ?.) — a destroyed Camera is a "fake null"
        // that passes ?. but throws on use (recurring gotcha in this codebase).
        if (captureCamera != null) return captureCamera;
        captureCamera = Camera.main;
        if (captureCamera == null)
        {
            // Fallback: first enabled camera in the scene.
            foreach (var c in Camera.allCameras)
            {
                if (c != null) { captureCamera = c; break; }
            }
        }
        return captureCamera;
    }

    /// <summary>
    /// Capture a frame if the current mode says we should, given the in-game
    /// day/segment. MUST be called on the main thread. Returns true if a frame was
    /// written (and sets LastFramePath/LastFrameBase64); false if skipped.
    ///
    /// includeBase64 controls whether the PNG is also base64-encoded into the
    /// response (cheaper to skip for large images and just use the on-disk path).
    /// </summary>
    public bool CaptureFrame(int day, int segment, bool includeBase64)
    {
        if (mode == CaptureMode.Off) return false;

        if (mode == CaptureMode.PerGameTime)
        {
            if (day == lastDay && segment == lastSegment) return false; // no transition
        }
        lastDay = day;
        lastSegment = segment;

        Camera cam = ResolveCamera();
        if (cam == null)
        {
            Debug.LogWarning("[GymCapture] No camera available to capture");
            return false;
        }

        EnsureResources();

        // Render the chosen camera into our offscreen target.
        RenderTexture prevTarget = cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = renderTexture;
            cam.Render();

            RenderTexture.active = renderTexture;
            readbackTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readbackTexture.Apply();
        }
        catch (Exception e)
        {
            Debug.LogError($"[GymCapture] Render/readback failed: {e.Message}");
            return false;
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
        }

        byte[] png;
        try
        {
            png = readbackTexture.EncodeToPNG();
        }
        catch (Exception e)
        {
            Debug.LogError($"[GymCapture] EncodeToPNG failed: {e.Message}");
            return false;
        }

        // Deterministic filename embedding day/round/segment so frames sort and
        // demo videos can be assembled in game-time order.
        string fileName = $"frame_d{day:D2}_r{segment + 1}_s{segment}.png";
        string fullPath = Path.Combine(GetAbsoluteOutputDir(), fileName);
        try
        {
            File.WriteAllBytes(fullPath, png);
            LastFramePath = fullPath;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GymCapture] Failed to write PNG: {e.Message}");
            LastFramePath = null;
            return false;
        }

        LastFrameBase64 = includeBase64 ? Convert.ToBase64String(png) : null;
        return true;
    }

    void OnDestroy()
    {
        ReleaseResources();
    }
}
