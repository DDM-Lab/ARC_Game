using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

public class ClockAnimationUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject      panel;
    public Image           clockImage;
    public TextMeshProUGUI messageText;

    [Header("Animation Frames")]
    [Tooltip("Drag all extracted GIF frames here in order.")]
    public Sprite[] frames;

    [Header("Skip Settings")]
    [Tooltip("Seconds per clock loop when skipping (3 loops play back-to-back).")]
    public float  skipLoopDuration = 0.4f;
    public string skipMessage      = "Skipping Simulation — No Deliveries In Progress";

    [Header("Day 1 Messages")]
    [TextArea(2, 4)] public string day1SetupMessage    = "Day 1 — Setup Phase\nFacilities opening will be ready by end of today (4 rounds).";
    [TextArea(2, 4)] public string day1CompleteMessage = "Facilities ready.";
    [TextArea(2, 4)] public string day1NoFacilitiesMessage = "Skipping setup time…";

    public static ClockAnimationUI Instance { get; private set; }

    private bool isPaused = false;

    public void Pause()  { isPaused = true; }
    public void Resume() { isPaused = false; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (panel != null) panel.SetActive(false);
    }

    // ── Public show / hide ───────────────────────────────────────────────────

    public void Show(string message = "")
    {
        if (panel != null)       panel.SetActive(true);
        if (messageText != null) messageText.text = message;

        GameLogPanel.Instance?.LogUIInteraction(
            $"Clock animation shown | message=\"{message}\"");
    }

    public void SetMessage(string message)
    {
        if (messageText != null) messageText.text = message;

        GameLogPanel.Instance?.LogUIInteraction(
            $"Clock animation message changed | message=\"{message}\"");
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);

        GameLogPanel.Instance?.LogUIInteraction("Clock animation hidden");
    }

    // ── Callback-based helpers (existing callers unchanged) ──────────────────

    // 3 fast loops then invoke onComplete — used when there are no deliveries
    public void PlaySkip(Action onComplete)
    {
        Show(skipMessage);
        StartCoroutine(AnimateLoops(3, skipLoopDuration, () => { Hide(); onComplete?.Invoke(); }));
    }

    // 3 loops spread over totalDuration real seconds — used alongside normal simulation
    public void PlaySynced(float totalDuration, Action onComplete = null)
    {
        Show();
        StartCoroutine(AnimateLoops(3, totalDuration / 3f, () => { Hide(); onComplete?.Invoke(); }));
    }

    // ── Yieldable version — panel visibility managed by caller ───────────────

    // Plays exactly 3 loops at skipLoopDuration each.
    // Does NOT show or hide the panel; the caller controls that.
    public IEnumerator PlayRoundLoops()
    {
        if (frames == null || frames.Length == 0)
        {
            yield return new WaitForSecondsRealtime(skipLoopDuration * 3);
            yield break;
        }

        float frameDuration = skipLoopDuration / frames.Length;

        for (int loop = 0; loop < 3; loop++)
            for (int i = 0; i < frames.Length; i++)
            {
                if (clockImage != null) clockImage.sprite = frames[i];
                yield return new WaitForSecondsRealtime(frameDuration);
                while (isPaused) yield return null;
            }
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    IEnumerator AnimateLoops(int loops, float loopDuration, Action onComplete)
    {
        if (frames == null || frames.Length == 0)
        {
            yield return new WaitForSecondsRealtime(loopDuration * loops);
            onComplete?.Invoke();
            yield break;
        }

        float frameDuration = loopDuration / frames.Length;

        for (int loop = 0; loop < loops; loop++)
            for (int i = 0; i < frames.Length; i++)
            {
                if (clockImage != null) clockImage.sprite = frames[i];
                yield return new WaitForSecondsRealtime(frameDuration);
                while (isPaused) yield return null;
            }

        onComplete?.Invoke();
    }
}
