using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Puts "Save JSON Checkpoint" / "Load JSON Checkpoint" inside the Settings panel,
/// replacing the floating IMGUI
/// buttons CoraSaveLoad drew in the top-right corner.
///
/// NO SCENE EDIT. The buttons are CLONED AT RUNTIME from the panel's own Exit button rather
/// than authored into MainScene.unity. That is deliberate: the scenes are LFS-tracked and the
/// merge driver named in .gitattributes is not defined, so a scene edit conflicts whole-file
/// against any upstream scene change (we spent a merge on exactly that). Cloning also means
/// the buttons inherit the panel's real styling -- font, colours, transitions -- instead of
/// approximating it, so they match whatever the artists change later without being re-skinned.
///
/// WHY IT ROUTES THROUGH CloseSettings(). OpenSettings sets Time.timeScale = 0f. Save is
/// synchronous and survives that, but Load opens a file picker and then tears down and
/// reloads the scene -- which destroys the panel that was holding the timeScale, leaving the
/// game frozen at 0 with no way to unpause. CloseSettings already restores timeScale
/// correctly, including the case where the simulation was deliberately paused before Settings
/// was opened, so both buttons close the panel first and act second.
///
/// FALLBACK IS KEPT UNTIL INSTALL SUCCEEDS. CoraSaveLoad.showButtons is only turned off once
/// a clone is actually in the hierarchy. If the panel has no Exit button to clone (a scene
/// rework, a stripped prefab), the corner buttons stay and the feature degrades instead of
/// vanishing -- losing save/load entirely is a far worse outcome than a duplicated control.
/// </summary>
public class CoraSettingsButtons : MonoBehaviour
{
    const string SAVE_NAME = "CoraSaveButton";
    const string LOAD_NAME = "CoraLoadButton";
    const string FLAG_NAME = "CoraFlagButton";

    /// <summary>Capability that reveals the play-tester controls (Load .cora, Flag
    /// Interaction). Save stays available to everyone: writing a file is harmless and it is
    /// how an ordinary player sends us a session at all. Load and Flag are the ones that only
    /// make sense for someone running a test.</summary>
    const string PLAY_TESTER = "play_tester";

    bool baseInstalled;       // Save .cora is in the panel
    bool testerInstalled;     // Load .cora + Flag Interaction are in the panel
    float elapsed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Application.isBatchMode) return;          // headless/gym drives save_state over TCP
        var go = new GameObject("[CoraSettingsButtons]");
        DontDestroyOnLoad(go);
        go.AddComponent<CoraSettingsButtons>();
    }

    void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    /// <summary>Re-arm on every scene load. Two reasons, both of which bit us:
    ///
    /// 1. THE GAME DOES NOT BOOT INTO MainScene. Build order is TitleScene, InfoScene,
    ///    InstructorConfigScene, TutorialScene, MainScene -- and SettingsPanel only exists in
    ///    the last one. RuntimeInitializeOnLoadMethod(AfterSceneLoad) fires after TITLE loads,
    ///    so the original 60-second search expired somewhere in the tutorial and the component
    ///    switched itself off before the panel ever existed. The corner IMGUI buttons then
    ///    stayed up for the entire session, which looked like the feature had been reverted.
    ///
    /// 2. LOADING A .cora REBUILDS THE SCENE, destroying the cloned buttons with it. Without
    ///    re-arming, save/load would work exactly once per session.</summary>
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        baseInstalled = false;
        testerInstalled = false;
        nextProbe = 0f;
        enabled = true;
    }

    float nextProbe;
    float nextComplaint;

    void Update()
    {
        if (baseInstalled && testerInstalled) return;   // stay enabled: a scene load re-arms us
        // Cheap poll rather than a per-frame one. NO give-up: the panel may legitimately be
        // several scenes away, and the capability may arrive long after the handshake starts.
        if (Time.unscaledTime < nextProbe) return;
        nextProbe = Time.unscaledTime + 0.5f;
        TryInstall();
    }

    void TryInstall()
    {
        SettingsPanel panel = SettingsPanel.Instance;
        if (panel == null || panel.settingsPanel == null || panel.exitButton == null)
        {
            // Throttled so a session spent in the title/tutorial scenes explains itself in the
            // console instead of looking like the component is dead.
            if (Time.unscaledTime > nextComplaint)
            {
                nextComplaint = Time.unscaledTime + 10f;
                Debug.Log($"[CoraSettingsButtons] waiting for SettingsPanel "
                        + $"(scene='{SceneManager.GetActiveScene().name}', "
                        + $"instance={(SettingsPanel.Instance != null)})");
            }
            return;
        }

        Transform parent = panel.exitButton.transform.parent;
        if (parent == null) return;

        bool autoLayout = parent.GetComponent<LayoutGroup>() != null;
        // Borrow a font from the panel's own label so the new buttons match it; TMP's
        // default asset may not even be assigned in a WebGL build, which renders nothing.
        TMP_FontAsset font = panel.sfxVolumeText != null ? panel.sfxVolumeText.font : null;

        // ── base: Save, for everyone, no capability required ──────────────────────────
        if (!baseInstalled)
        {
            if (parent.Find(SAVE_NAME) == null)
            {
                Button save = Clone(panel.exitButton, parent, SAVE_NAME, "Save JSON Checkpoint",
                                    autoLayout ? 0 : 1, font);
                if (save == null) return;
                save.onClick.AddListener(() =>
                {
                    Debug.Log("[CoraSettingsButtons] Save JSON Checkpoint clicked");
                    panel.CloseSettings();
                    CoraSaveLoad.Instance?.SaveToFile();
                });
            }
            baseInstalled = true;
            // Safe to drop the corner fallback the moment a real button exists.
            if (CoraSaveLoad.Instance != null) CoraSaveLoad.Instance.showButtons = false;
            Debug.Log("[CoraSettingsButtons] Save JSON Checkpoint installed in Settings; "
                    + "corner buttons disabled.");
        }

        // ── play-tester: Load + Flag, once the handshake says so ──────────────────────
        if (!testerInstalled && WebSocketManager.HasCapability(PLAY_TESTER))
        {
            if (parent.Find(LOAD_NAME) == null)
            {
                Button load = Clone(panel.exitButton, parent, LOAD_NAME, "Load JSON Checkpoint",
                                    autoLayout ? 0 : 2, font);
                if (load != null)
                    load.onClick.AddListener(() =>
                    {
                        // Traced because the WebGL file picker is opened from jslib
                        // (input.click()) and a browser can refuse it silently when the call
                        // does not originate in a user gesture. If this line appears in the
                        // console but no picker opens, the refusal is browser-side, not ours.
                        Debug.Log("[CoraSettingsButtons] Load JSON Checkpoint clicked -> opening picker");
                        panel.CloseSettings();
                        CoraSaveLoad.Instance?.LoadFromFile();
                    });
            }
            if (parent.Find(FLAG_NAME) == null)
            {
                Button flag = Clone(panel.exitButton, parent, FLAG_NAME, "Flag Interaction",
                                    autoLayout ? 0 : 3, font);
                if (flag != null)
                    flag.onClick.AddListener(() =>
                    {
                        // Flag FIRST, then close: CloseSettings resumes the clock, and the
                        // flag should record the moment the tester was looking at.
                        InteractionFlag f = InteractionFlags.Flag(CurrentOfficer());
                        panel.CloseSettings();
                        Debug.Log($"[CoraSettingsButtons] flagged {f.id}");
                    });
            }
            testerInstalled = true;
            Debug.Log("[CoraSettingsButtons] play_tester controls installed (Load, Flag).");
        }
    }

    /// <summary>Officer whose conversation is open, for the flag record. Best-effort: a flag
    /// with no officer is still useful (day/round still locate it), so this never throws.</summary>
    static string CurrentOfficer()
    {
        try
        {
            var ui = FindObjectOfType<AgentConversationUI>();
            return ui != null ? ui.CurrentOfficerName() : "";
        }
        catch (System.Exception) { return ""; }
    }

    /// <summary>Clone the Exit button so the new control inherits the panel's exact styling,
    /// then strip the inherited listeners (the clone would otherwise also close the panel
    /// through Exit's own handler, firing CloseSettings twice).</summary>
    /// <summary>Clone the Exit button for its Button component (transitions, colours, the
    /// navigation setup the panel already uses), then make it a TEXT button: ExitButton is a
    /// 55x55 icon whose Image sprite is the X glyph and which has no child text at all, so a
    /// bare clone renders as another X. Sprite is cleared (the Image stays enabled -- it is
    /// the raycast target, disabling it makes the button unclickable), the rect is widened,
    /// and a label is added using a font taken from the panel's own text so it matches.</summary>
    static Button Clone(Button template, Transform parent, string name, string label,
                        int slot, TMP_FontAsset font)
    {
        GameObject go = Instantiate(template.gameObject, parent);
        go.name = name;
        go.SetActive(true);
        Button b = go.GetComponent<Button>();
        if (b == null) { Destroy(go); return null; }
        b.onClick.RemoveAllListeners();

        var rt = go.GetComponent<RectTransform>();
        if (rt != null && slot > 0)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(270f, 44f);   // fits "Load JSON Checkpoint"
            // Stacked upward from the dialog's lower area, in the same centred coordinate
            // space ExitButton uses (it sits at y=274, so the dialog is ~550 tall).
            rt.anchoredPosition = new Vector2(0f, -150f - 52f * (slot - 1));
        }

        // Drop the X sprite but keep the graphic: Image is the raycast target.
        var img = go.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = null;
            img.color = new Color(0.16f, 0.18f, 0.22f, 0.95f);
        }

        // ExitButton has no text child, so make one.
        var tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp == null)
        {
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 12f; tmp.fontSizeMax = 22f;
            tmp.color = Color.white;
            tmp.raycastTarget = false;      // clicks belong to the Button's own Image
            if (font != null) tmp.font = font;   // match the panel instead of TMP's default
        }
        tmp.text = label;
        return b;
    }
}
