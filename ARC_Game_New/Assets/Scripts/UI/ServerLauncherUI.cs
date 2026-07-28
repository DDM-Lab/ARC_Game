using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Pre-game launcher overlay. Lets the player enter a server URL + API key,
/// pull the config catalog from /configs, pick a config, then connect.
///
/// Builds its own Canvas + UI tree at runtime and self-instantiates via
/// <see cref="RuntimeInitializeOnLoadMethod"/>, so no scene wiring is needed —
/// dropping this file into the project is enough.
///
/// If you later want a styled prefab, replace AutoInstantiate with a scene
/// reference and reuse the field-binding logic in BindAndWire().
/// </summary>
public class ServerLauncherUI : MonoBehaviour
{
    // ── PlayerPrefs keys (shared with WebSocketManager) ─────────────
    const string PREFS_URL = "arc_server_url";
    const string PREFS_KEY = "arc_api_key";
    const string PREFS_CFG = "arc_config_name";

    const string DEFAULT_URL = "ws://localhost:9876/ws";
    const string DEFAULT_KEY = "dev-local-key";

    // ── UI references (built in BuildUI) ────────────────────────────
    GameObject root;
    GraphicRaycaster launcherRaycaster;
    TMP_InputField urlField;
    TMP_InputField keyField;
    Button configSelectorButton;
    TMP_Text configSelectorLabel;
    GameObject configListPanel;
    GraphicRaycaster configListRaycaster;
    bool configListOpen = false;
    int selectedConfigIndex = -1;
    Button connectButton;
    Button startButton;
    TMP_Text statusText;
    TMP_Text connectButtonLabel;
    TMP_Text startButtonLabel;

    // ── State ───────────────────────────────────────────────────────
    List<ConfigInfo> fetchedConfigs = new List<ConfigInfo>();
    bool fetching = false;
    bool connecting = false;

    // When Start Game is pressed in a scene that has no WebSocketManager yet
    // (e.g. the title screen of a build), we stash the chosen settings and
    // connect automatically once the scene that owns the manager loads.
    bool pendingConnect = false;
    string pendingUrl, pendingKey, pendingConfig;

    // Optional config name requested via ?config=... in the page URL (WebGL),
    // auto-selected once the catalog loads.
    string desiredConfig;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstantiate()
    {
        if (Application.isBatchMode) return;
        if (FindObjectOfType<ServerLauncherUI>() != null) return;
        var go = new GameObject("[ServerLauncher]");
        DontDestroyOnLoad(go);
        go.AddComponent<ServerLauncherUI>();
    }

    void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        LoadPrefs();
        SetStatus("Enter server URL + API key, then click Connect.", Color.gray);
        SetStartEnabled(false);

#if UNITY_WEBGL && !UNITY_EDITOR
        // By default Unity captures ALL keyboard input at the document level and
        // preventDefault()s it, so a DOM <input> (our key-entry overlay) never
        // shows typed/pasted characters. Turning this off makes Unity grab the
        // keyboard only while the game canvas is focused — the overlay works, and
        // gameplay/keyboard resume the moment the user clicks back on the canvas.
        //
        // WebGLInput lives in UnityEngine.WebGLModule, which Unity does NOT add to
        // the player Assembly-CSharp reference set in this project (every other
        // UnityEngine.*Module is referenced, but not WebGLModule) — a direct
        // reference gives CS0103 at player-compile time. Set the property
        // reflectively so there is no compile-time dependency on that assembly; the
        // module still ships in the WebGL runtime, so it resolves at run time.
        var webglInput = System.Type.GetType("UnityEngine.WebGLInput, UnityEngine.WebGLModule");
        webglInput?.GetProperty("captureAllKeyboardInput")?.SetValue(null, false);
        ClipboardInit();   // enable Cmd/Ctrl+V paste of the API key in the browser
#endif

        // If the page URL carried an API key (e.g. a personalized link
        // ?key=ck_...&config=...), use it and auto-load the catalog so the
        // player never has to type into the text box.
        string urlKey = UrlParam("key");
        if (!string.IsNullOrEmpty(urlKey))
        {
            keyField.text = urlKey;
            SetStatus("Loading from your link…", new Color(0.75f, 0.78f, 0.82f));
            StartCoroutine(FetchConfigs());
        }
    }

    // The Day-1 tutorial (TutorialMessageUI.DisableGameInteractions) disables the
    // GraphicRaycaster on every canvas except its own when it pauses the game on
    // scene load. That kills clicks on this launcher even though it renders on top.
    // As a modal pre-game gate, re-assert our own raycaster while we're visible so
    // the launcher stays interactive regardless of what the game does underneath.
    // (uGUI input is processed on unscaled time, so this works at timeScale == 0.)
    void Update()
    {
        if (root != null && root.activeSelf &&
            launcherRaycaster != null && !launcherRaycaster.enabled)
        {
            launcherRaycaster.enabled = true;
        }
        // The dropdown list rides its own canvas/raycaster; keep it live too.
        if (configListOpen && configListRaycaster != null && !configListRaycaster.enabled)
        {
            configListRaycaster.enabled = true;
        }
        // Deferred connect: the WebSocketManager lives in the game scene, so if
        // Start Game was pressed earlier (on the title screen), connect as soon
        // as that scene loads and the manager appears.
        if (pendingConnect && WebSocketManager.Instance != null)
        {
            pendingConnect = false;
            ApplyAndConnect(WebSocketManager.Instance);
        }
    }

    // ── PlayerPrefs ────────────────────────────────────────────────

    void LoadPrefs()
    {
        if (Application.platform == RuntimePlatform.WebGLPlayer)
            urlField.text = DefaultServerUrl();   // always same-origin for hosted builds
        else
            urlField.text = PlayerPrefs.GetString(PREFS_URL, DEFAULT_URL);

        // In the browser, don't pre-fill a dev key — make users enter their own.
        string defaultKey = (Application.platform == RuntimePlatform.WebGLPlayer) ? "" : DEFAULT_KEY;
        keyField.text = PlayerPrefs.GetString(PREFS_KEY, defaultKey);

        // A config requested via ?config=... is auto-selected after the fetch.
        desiredConfig = UrlParam("config");
    }

    void SavePrefs(string configName)
    {
        PlayerPrefs.SetString(PREFS_URL, Sanitize(urlField.text));
        PlayerPrefs.SetString(PREFS_KEY, Sanitize(keyField.text));
        PlayerPrefs.SetString(PREFS_CFG, configName);
        PlayerPrefs.Save();
    }

    // ── Clipboard paste ────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
    // PromptApiKey now shows an HTML overlay and delivers the result
    // asynchronously via OnClipboardPasted — it returns void.
    [DllImport("__Internal")] static extern void PromptApiKey();
    [DllImport("__Internal")] static extern void ClipboardInit();
#endif

    void PasteApiKey()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // Opens an HTML <input> overlay directly in the browser DOM.
        // Result arrives via OnClipboardPasted once the user confirms.
        PromptApiKey();
#else
        string clip = GUIUtility.systemCopyBuffer;
        if (!string.IsNullOrEmpty(clip)) ApplyPasted(clip);
        else SetStatus("Clipboard is empty.", new Color(0.95f, 0.55f, 0.35f));
#endif
    }

    // Bonus path: called from ClipboardPaste.jslib's paste listener via
    // SendMessage when a browser does deliver the event — must stay public.
    public void OnClipboardPasted(string text)
    {
        if (root == null || !root.activeSelf) return;        // ignore in-game pastes
        if (!string.IsNullOrEmpty(text)) ApplyPasted(text);
    }

    void ApplyPasted(string text)
    {
        if (keyField == null) return;
        keyField.text = Sanitize(text);
        SetStatus("Pasted from clipboard.", new Color(0.55f, 0.85f, 0.6f));
    }

    // Strip whitespace AND control characters (e.g. a stray  backspace that
    // rides in on a paste). Header values legally can't contain control chars, so
    // UnityWebRequest.SetRequestHeader throws on them — which, thrown inside the
    // fetch coroutine, aborts it silently and leaves the UI stuck on "Fetching
    // configs…". Sanitizing here (and before every header/connect use) prevents both.
    static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            if (!char.IsControl(c)) sb.Append(c);
        return sb.ToString().Trim();
    }

    // ── UI Construction ────────────────────────────────────────────

    void BuildUI()
    {
        // ── Canvas ──────────────────────────────────────────────────
        root = new GameObject("LauncherCanvas");
        root.transform.SetParent(transform);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000; // above all gameplay UI
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        launcherRaycaster = root.AddComponent<GraphicRaycaster>();

        // ── Backdrop (swallows clicks) ──────────────────────────────
        var backdrop = MakePanel(root.transform, new Color(0f, 0f, 0f, 0.72f));
        StretchToParent(backdrop);

        // ── Card ────────────────────────────────────────────────────
        var card = MakePanel(backdrop.transform, new Color(0.13f, 0.14f, 0.17f, 0.98f));
        var cardRT = card.GetComponent<RectTransform>();
        cardRT.anchorMin = cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.sizeDelta = new Vector2(640, 560);
        cardRT.anchoredPosition = Vector2.zero;
        var cardLayout = card.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(32, 32, 24, 24);
        cardLayout.spacing = 12;
        cardLayout.childForceExpandHeight = false;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childAlignment = TextAnchor.UpperCenter;

        // ── Title ───────────────────────────────────────────────────
        MakeText(card.transform, "Connect to ARC Server", 26, FontStyles.Bold,
                 TextAlignmentOptions.Center, new Color(0.93f, 0.94f, 0.96f), 40);

        AddSpacer(card.transform, 4);

        // ── Server URL ──────────────────────────────────────────────
        // On a hosted WebGL deployment the router is same-origin (served via
        // the reverse proxy), so we derive the URL from the page and hide this
        // field — players only enter their API key.
        var urlLabel = MakeLabel(card.transform, "Server URL");
        urlField = MakeInput(card.transform, DEFAULT_URL, password: false);
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            urlLabel.gameObject.SetActive(false);
            urlField.gameObject.SetActive(false);
        }

        // ── API Key ────────────────────────────────────────────────
        // Visible (not masked): it's a shared access token, not a password, and
        // showing it prevents "looks filled but is empty" placeholder confusion
        // and stops the browser from trying to autofill a saved password here.
        MakeLabel(card.transform, "API Key");
        keyField = MakeInput(card.transform, "Enter your API key", password: false);
        // Paste button: WebGL can't Cmd/Ctrl+V into input fields (Unity's paste
        // reads its own buffer, not the browser clipboard), so offer an explicit
        // clipboard read. Works on native too (reads the OS copy buffer).
        var pasteButton = MakeButton(card.transform, "Paste / enter API key",
                                     out _, height: 30, bg: new Color(0.28f, 0.30f, 0.36f));
        pasteButton.onClick.AddListener(PasteApiKey);

        // ── Connect Button ─────────────────────────────────────────
        AddSpacer(card.transform, 4);
        connectButton = MakeButton(card.transform, "Connect & Load Configs",
                                    out connectButtonLabel, height: 44,
                                    bg: new Color(0.20f, 0.45f, 0.85f));
        connectButton.onClick.AddListener(() => StartCoroutine(FetchConfigs()));

        // ── Status ─────────────────────────────────────────────────
        statusText = MakeText(card.transform, "", 14, FontStyles.Normal,
                              TextAlignmentOptions.Center,
                              new Color(0.75f, 0.78f, 0.82f), 36);
        statusText.enableWordWrapping = true;

        // ── Config dropdown (custom, built from Buttons) ────────────
        // A TMP_Dropdown built at runtime proved unreliable (popup list
        // wouldn't render), so this is a hand-rolled dropdown: a selector
        // button that toggles a list panel of one button per config. The
        // list lives on its own Canvas (overrideSorting) so it draws above
        // the rest of the launcher and doesn't disturb the card's layout.
        MakeLabel(card.transform, "Config");
        configSelectorButton = MakeButton(card.transform, "(connect to load configs)  ▼",
                                          out configSelectorLabel, height: 40,
                                          bg: new Color(0.08f, 0.09f, 0.11f));
        configSelectorButton.onClick.AddListener(ToggleConfigList);
        configSelectorButton.interactable = false;
        BuildConfigListPanel();

        AddSpacer(card.transform, 8);

        // ── Start Button ───────────────────────────────────────────
        startButton = MakeButton(card.transform, "Start Game",
                                  out startButtonLabel, height: 50,
                                  bg: new Color(0.20f, 0.70f, 0.40f));
        startButton.onClick.AddListener(() => StartCoroutine(StartGame()));
    }

    // ── UI primitive builders ───────────────────────────────────────

    static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
        DontDestroyOnLoad(es);
    }

    static GameObject MakePanel(Transform parent, Color color)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    static void StretchToParent(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void AddSpacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }

    static TMP_Text MakeText(Transform parent, string content, float size,
                             FontStyles style, TextAlignmentOptions align,
                             Color color, float preferredHeight)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        go.GetComponent<LayoutElement>().preferredHeight = preferredHeight;
        return t;
    }

    static TMP_Text MakeLabel(Transform parent, string content)
    {
        return MakeText(parent, content, 14, FontStyles.Bold,
                        TextAlignmentOptions.Left,
                        new Color(0.70f, 0.74f, 0.80f), 20);
    }

    static TMP_InputField MakeInput(Transform parent, string defaultText, bool password)
    {
        var go = new GameObject("InputField",
                                typeof(RectTransform), typeof(Image),
                                typeof(LayoutElement), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.11f, 1f);
        go.GetComponent<LayoutElement>().preferredHeight = 40;

        var input = go.GetComponent<TMP_InputField>();

        // Text Area child (required by TMP_InputField)
        var ta = new GameObject("TextArea",
                                typeof(RectTransform), typeof(RectMask2D));
        ta.transform.SetParent(go.transform, false);
        var taRT = ta.GetComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(10, 4);
        taRT.offsetMax = new Vector2(-10, -4);

        // Actual visible text
        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(ta.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.fontSize = 16;
        text.color = new Color(0.92f, 0.93f, 0.95f);
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Truncate;
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = textRT.offsetMax = Vector2.zero;

        // Placeholder
        var phGO = new GameObject("Placeholder", typeof(RectTransform));
        phGO.transform.SetParent(ta.transform, false);
        var ph = phGO.AddComponent<TextMeshProUGUI>();
        ph.fontSize = 16;
        ph.color = new Color(0.45f, 0.48f, 0.52f);
        ph.fontStyle = FontStyles.Italic;
        ph.text = defaultText;
        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = phRT.offsetMax = Vector2.zero;

        input.textViewport = taRT;
        input.textComponent = text;
        input.placeholder = ph;
        input.contentType = password
            ? TMP_InputField.ContentType.Password
            : TMP_InputField.ContentType.Standard;
        input.text = "";
        return input;
    }

    static Button MakeButton(Transform parent, string content,
                             out TMP_Text labelOut, float height, Color bg)
    {
        var go = new GameObject("Button",
                                typeof(RectTransform), typeof(Image),
                                typeof(LayoutElement), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = bg;
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        // Hover/press color tint
        var colors = btn.colors;
        colors.normalColor = bg;
        colors.highlightedColor = Lighten(bg, 0.12f);
        colors.pressedColor = Lighten(bg, -0.10f);
        colors.disabledColor = new Color(bg.r, bg.g, bg.b, 0.35f);
        btn.colors = colors;

        labelOut = MakeText(go.transform, content, 17, FontStyles.Bold,
                            TextAlignmentOptions.Center,
                            new Color(1f, 1f, 1f), height);
        // Stretch label to fill button.
        var lblRT = labelOut.rectTransform;
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = lblRT.offsetMax = Vector2.zero;
        // Remove the LayoutElement we got from MakeText so it doesn't force
        // its own preferred height inside the button.
        var le = labelOut.GetComponent<LayoutElement>();
        if (le != null) Destroy(le);
        return btn;
    }

    static Color Lighten(Color c, float delta)
    {
        return new Color(
            Mathf.Clamp01(c.r + delta),
            Mathf.Clamp01(c.g + delta),
            Mathf.Clamp01(c.b + delta),
            c.a);
    }

    // ── Status / Enable helpers ────────────────────────────────────

    void SetStatus(string msg, Color color)
    {
        if (statusText != null)
        {
            statusText.text = msg;
            statusText.color = color;
        }
        Debug.Log($"[Launcher] {msg}");
    }

    void SetStartEnabled(bool enabled)
    {
        if (startButton != null) startButton.interactable = enabled;
    }

    void SetConnectEnabled(bool enabled)
    {
        if (connectButton != null) connectButton.interactable = enabled;
        if (connectButtonLabel != null)
            connectButtonLabel.text = enabled ? "Connect & Load Configs" : "Fetching…";
    }

    void BuildConfigListPanel()
    {
        configListPanel = new GameObject("ConfigList",
            typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster),
            typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        configListPanel.transform.SetParent(configSelectorButton.transform, false);

        var rt = configListPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 1f);     // hang downward from selector's bottom
        rt.anchoredPosition = new Vector2(0, -2);

        var canvas = configListPanel.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 6000;           // above the launcher canvas (5000)
        configListRaycaster = configListPanel.GetComponent<GraphicRaycaster>();

        configListPanel.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 1f);

        var vlg = configListPanel.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 2;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        configListPanel.GetComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        configListPanel.SetActive(false);
    }

    void PopulateConfigList()
    {
        if (configListPanel == null) return;
        for (int i = configListPanel.transform.childCount - 1; i >= 0; i--)
            Destroy(configListPanel.transform.GetChild(i).gameObject);

        for (int i = 0; i < fetchedConfigs.Count; i++)
        {
            var c = fetchedConfigs[i];
            int n = (c.agents != null) ? c.agents.Length : 0;
            int idx = i; // capture for the closure
            var b = MakeButton(configListPanel.transform, $"{DisplayName(c)}  ({n} agents)",
                               out _, height: 34, bg: new Color(0.16f, 0.17f, 0.21f));
            b.onClick.AddListener(() => SelectConfig(idx));
        }
    }

    void ToggleConfigList()
    {
        if (fetchedConfigs.Count == 0) return;
        configListOpen = !configListOpen;
        configListPanel.SetActive(configListOpen);
    }

    void SelectConfig(int index)
    {
        if (index < 0 || index >= fetchedConfigs.Count) return;
        selectedConfigIndex = index;
        RefreshConfigLabel();
        configListOpen = false;
        configListPanel.SetActive(false);
        SetStartEnabled(true);
    }

    void RefreshConfigLabel()
    {
        if (configSelectorLabel == null) return;
        if (selectedConfigIndex < 0 || selectedConfigIndex >= fetchedConfigs.Count)
        {
            configSelectorLabel.text = "(connect to load configs)  ▼";
            return;
        }
        var c = fetchedConfigs[selectedConfigIndex];
        int n = (c.agents != null) ? c.agents.Length : 0;
        configSelectorLabel.text = $"{DisplayName(c)}  ({n} agents)  ▼";
    }

    static string DisplayName(ConfigInfo c)
    {
        return !string.IsNullOrEmpty(c.title) ? c.title : c.name;
    }

    // ── Fetch /configs ─────────────────────────────────────────────

    IEnumerator FetchConfigs()
    {
        if (fetching) yield break;
        fetching = true;
        SetConnectEnabled(false);
        SetStartEnabled(false);
        SetStatus("Fetching configs…", new Color(0.75f, 0.78f, 0.82f));

        string baseUrl = WsToHttp(urlField.text.Trim());
        if (string.IsNullOrEmpty(baseUrl))
        {
            SetStatus("Couldn't parse server URL.", Color.red);
            fetching = false;
            SetConnectEnabled(true);
            yield break;
        }

        var req = UnityWebRequest.Get(baseUrl + "/configs");
        req.SetRequestHeader("Authorization", "Bearer " + Sanitize(keyField.text));
        req.timeout = 10;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string detail;
            if (req.responseCode == 401) detail = "401 — bad API key.";
            else if (req.responseCode == 0) detail = "could not reach server.";
            else detail = $"{req.responseCode} — {req.error}";
            SetStatus($"Failed: {detail}", new Color(0.95f, 0.45f, 0.45f));
            fetching = false;
            SetConnectEnabled(true);
            yield break;
        }

        ConfigsResponse parsed = null;
        try { parsed = JsonUtility.FromJson<ConfigsResponse>(req.downloadHandler.text); }
        catch (Exception e)
        {
            SetStatus("Bad JSON from server: " + e.Message, Color.red);
            fetching = false;
            SetConnectEnabled(true);
            yield break;
        }

        fetchedConfigs.Clear();
        if (parsed != null && parsed.configs != null) fetchedConfigs.AddRange(parsed.configs);

        // Collapse any open list, then rebuild it from the fresh results.
        configListOpen = false;
        if (configListPanel != null) configListPanel.SetActive(false);
        PopulateConfigList();

        if (fetchedConfigs.Count == 0)
        {
            SetStatus("Server returned no configs.",
                      new Color(0.95f, 0.55f, 0.35f));
            selectedConfigIndex = -1;
            RefreshConfigLabel();
            if (configSelectorButton != null) configSelectorButton.interactable = false;
        }
        else
        {
            // Selection priority: ?config= from the link, else last-used, else first.
            string preferred = !string.IsNullOrEmpty(desiredConfig)
                ? desiredConfig : PlayerPrefs.GetString(PREFS_CFG, "");
            selectedConfigIndex = 0;
            for (int i = 0; i < fetchedConfigs.Count; i++)
                if (fetchedConfigs[i].name == preferred) { selectedConfigIndex = i; break; }
            RefreshConfigLabel();
            if (configSelectorButton != null) configSelectorButton.interactable = true;

            SetStatus($"Loaded {fetchedConfigs.Count} configs. Click the box to choose.",
                      new Color(0.55f, 0.85f, 0.6f));
            SetStartEnabled(true);
        }

        fetching = false;
        SetConnectEnabled(true);
    }

    // ── Start Game ─────────────────────────────────────────────────

    IEnumerator StartGame()
    {
        if (connecting) yield break;
        if (selectedConfigIndex < 0 || selectedConfigIndex >= fetchedConfigs.Count)
        {
            SetStatus("Pick a config first.", new Color(0.95f, 0.55f, 0.35f));
            yield break;
        }

        connecting = true;
        SetStartEnabled(false);
        SetConnectEnabled(false);
        if (startButtonLabel != null) startButtonLabel.text = "Connecting…";

        var chosen = fetchedConfigs[selectedConfigIndex];
        SavePrefs(chosen.name);

        // Stash the chosen settings so we can apply them to the
        // WebSocketManager whether it exists now or appears in a later scene.
        pendingUrl = Sanitize(urlField.text);
        pendingKey = Sanitize(keyField.text);
        pendingConfig = chosen.name;

        var wsm = WebSocketManager.Instance;
        if (wsm != null)
        {
            // The manager is in this scene (e.g. playing MainScene in the
            // Editor) — connect right away.
            ApplyAndConnect(wsm);
            if (root != null) root.SetActive(false);
        }
        else
        {
            // No manager yet (e.g. the title screen of a build). This launcher
            // object survives scene loads (DontDestroyOnLoad); Update() will
            // connect once the scene that owns the WebSocketManager loads.
            pendingConnect = true;
            SetStatus("Settings saved — the game will connect when it loads.",
                      new Color(0.55f, 0.85f, 0.6f));
            if (root != null) root.SetActive(false);
        }
    }

    void ApplyAndConnect(WebSocketManager wsm)
    {
        wsm.serverUrl = pendingUrl;
        wsm.apiKey = pendingKey;
        wsm.configName = pendingConfig;
        wsm.enableWebSocket = true;
        wsm.ConnectToServer();
    }

    // ── Helpers ────────────────────────────────────────────────────

    // Hosted WebGL: derive the router's same-origin wss:// URL from the page
    // address so the build needs no baked-in server address. Falls back to the
    // localhost default in the Editor / native players.
    static string DefaultServerUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string page = Application.absoluteURL;
        if (!string.IsNullOrEmpty(page))
        {
            try
            {
                var uri = new System.Uri(page);
                string scheme = uri.Scheme == "https" ? "wss" : "ws";
                return $"{scheme}://{uri.Authority}/ws";
            }
            catch { }
        }
#endif
        return DEFAULT_URL;
    }

    // Read a query-string parameter from the hosting page URL (WebGL only).
    // Lets a personalized link supply the API key / config, e.g.
    //   https://arc.example.edu/?key=ck_alice...&config=openai_multi_agent_config_local
    static string UrlParam(string name)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string url = Application.absoluteURL;
        if (string.IsNullOrEmpty(url)) return null;
        int q = url.IndexOf('?');
        if (q < 0) return null;
        string query = url.Substring(q + 1);
        int hash = query.IndexOf('#');
        if (hash >= 0) query = query.Substring(0, hash);
        foreach (string pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string k = System.Uri.UnescapeDataString(pair.Substring(0, eq));
            if (k == name)
                return System.Uri.UnescapeDataString(pair.Substring(eq + 1));
        }
#endif
        return null;
    }

    static string WsToHttp(string wsUrl)
    {
        if (string.IsNullOrEmpty(wsUrl)) return null;
        string url = wsUrl.Trim();
        if (url.StartsWith("wss://")) url = "https://" + url.Substring(6);
        else if (url.StartsWith("ws://")) url = "http://" + url.Substring(5);
        else if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "http://" + url;
        int doubleSlash = url.IndexOf("//");
        if (doubleSlash >= 0)
        {
            int pathSlash = url.IndexOf('/', doubleSlash + 2);
            if (pathSlash > 0) url = url.Substring(0, pathSlash);
        }
        return url;
    }

    // ── JSON wrappers (match agent_router /configs response) ───────
    [Serializable]
    class ConfigsResponse { public ConfigInfo[] configs; }
    [Serializable]
    public class ConfigInfo { public string name; public string title; public string path; public AgentInfo[] agents; }
    [Serializable]
    public class AgentInfo { public string name; public string role; public string actor_type; }
}
