using System;
using System.Collections;
using System.Collections.Generic;
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
    TMP_InputField urlField;
    TMP_InputField keyField;
    TMP_Dropdown configDropdown;
    Button connectButton;
    Button startButton;
    TMP_Text statusText;
    TMP_Text connectButtonLabel;
    TMP_Text startButtonLabel;

    // ── State ───────────────────────────────────────────────────────
    List<ConfigInfo> fetchedConfigs = new List<ConfigInfo>();
    bool fetching = false;
    bool connecting = false;

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
    }

    // ── PlayerPrefs ────────────────────────────────────────────────

    void LoadPrefs()
    {
        urlField.text = PlayerPrefs.GetString(PREFS_URL, DEFAULT_URL);
        keyField.text = PlayerPrefs.GetString(PREFS_KEY, DEFAULT_KEY);
    }

    void SavePrefs(string configName)
    {
        PlayerPrefs.SetString(PREFS_URL, urlField.text.Trim());
        PlayerPrefs.SetString(PREFS_KEY, keyField.text.Trim());
        PlayerPrefs.SetString(PREFS_CFG, configName);
        PlayerPrefs.Save();
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
        root.AddComponent<GraphicRaycaster>();

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
        MakeLabel(card.transform, "Server URL");
        urlField = MakeInput(card.transform, DEFAULT_URL, password: false);

        // ── API Key ────────────────────────────────────────────────
        MakeLabel(card.transform, "API Key");
        keyField = MakeInput(card.transform, DEFAULT_KEY, password: true);

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

        // ── Config dropdown ────────────────────────────────────────
        MakeLabel(card.transform, "Config");
        configDropdown = MakeDropdown(card.transform);
        configDropdown.onValueChanged.AddListener(_ =>
            SetStartEnabled(configDropdown.value >= 0 && fetchedConfigs.Count > 0));

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

    static void MakeLabel(Transform parent, string content)
    {
        MakeText(parent, content, 14, FontStyles.Bold,
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

    static TMP_Dropdown MakeDropdown(Transform parent)
    {
        var go = new GameObject("Dropdown",
                                typeof(RectTransform), typeof(Image),
                                typeof(LayoutElement), typeof(TMP_Dropdown));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.11f, 1f);
        go.GetComponent<LayoutElement>().preferredHeight = 40;
        var dd = go.GetComponent<TMP_Dropdown>();

        // Label (currently-selected option text)
        var label = MakeText(go.transform, "(none yet)", 16, FontStyles.Normal,
                             TextAlignmentOptions.MidlineLeft,
                             new Color(0.92f, 0.93f, 0.95f), 40);
        var lblRT = label.rectTransform;
        lblRT.anchorMin = new Vector2(0, 0);
        lblRT.anchorMax = new Vector2(1, 1);
        lblRT.offsetMin = new Vector2(10, 6);
        lblRT.offsetMax = new Vector2(-30, -6);

        // Arrow
        var arrow = new GameObject("Arrow",
                                    typeof(RectTransform), typeof(Image));
        arrow.transform.SetParent(go.transform, false);
        arrow.GetComponent<Image>().color = new Color(0.6f, 0.63f, 0.7f);
        var aRT = arrow.GetComponent<RectTransform>();
        aRT.anchorMin = new Vector2(1, 0.5f);
        aRT.anchorMax = new Vector2(1, 0.5f);
        aRT.pivot = new Vector2(1, 0.5f);
        aRT.sizeDelta = new Vector2(12, 12);
        aRT.anchoredPosition = new Vector2(-12, 0);

        // Template (TMP_Dropdown needs this to spawn the option list)
        var template = new GameObject("Template",
                                       typeof(RectTransform), typeof(Image),
                                       typeof(ScrollRect), typeof(CanvasGroup));
        template.transform.SetParent(go.transform, false);
        template.SetActive(false);
        template.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 1f);
        var tRT = template.GetComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0, 0);
        tRT.anchorMax = new Vector2(1, 0);
        tRT.pivot = new Vector2(0.5f, 1f);
        tRT.anchoredPosition = new Vector2(0, 2);
        tRT.sizeDelta = new Vector2(0, 180);

        // Viewport
        var viewport = new GameObject("Viewport",
                                       typeof(RectTransform), typeof(Mask),
                                       typeof(Image));
        viewport.transform.SetParent(template.transform, false);
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;
        var vRT = viewport.GetComponent<RectTransform>();
        vRT.anchorMin = Vector2.zero;
        vRT.anchorMax = Vector2.one;
        vRT.offsetMin = Vector2.zero;
        vRT.offsetMax = Vector2.zero;

        // Content
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var cRT = content.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(0, 1);
        cRT.anchorMax = new Vector2(1, 1);
        cRT.pivot = new Vector2(0.5f, 1);
        cRT.sizeDelta = new Vector2(0, 36);

        // Item
        var item = new GameObject("Item",
                                   typeof(RectTransform), typeof(Toggle),
                                   typeof(LayoutElement));
        item.transform.SetParent(content.transform, false);
        item.GetComponent<LayoutElement>().preferredHeight = 36;
        var itemRT = item.GetComponent<RectTransform>();
        itemRT.anchorMin = new Vector2(0, 0.5f);
        itemRT.anchorMax = new Vector2(1, 0.5f);
        itemRT.sizeDelta = new Vector2(0, 36);

        // Item background (highlighted color)
        var itemBg = new GameObject("Item Background",
                                     typeof(RectTransform), typeof(Image));
        itemBg.transform.SetParent(item.transform, false);
        itemBg.GetComponent<Image>().color = new Color(0.20f, 0.45f, 0.85f, 0.45f);
        var bgRT = itemBg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        // Item label
        var itemLabel = new GameObject("Item Label", typeof(RectTransform));
        itemLabel.transform.SetParent(item.transform, false);
        var itemLabelText = itemLabel.AddComponent<TextMeshProUGUI>();
        itemLabelText.text = "";
        itemLabelText.fontSize = 16;
        itemLabelText.color = new Color(0.92f, 0.93f, 0.95f);
        itemLabelText.alignment = TextAlignmentOptions.MidlineLeft;
        var ilRT = itemLabel.GetComponent<RectTransform>();
        ilRT.anchorMin = Vector2.zero;
        ilRT.anchorMax = Vector2.one;
        ilRT.offsetMin = new Vector2(12, 0);
        ilRT.offsetMax = new Vector2(-12, 0);

        var itemToggle = item.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemBg.GetComponent<Image>();
        itemToggle.graphic = itemBg.GetComponent<Image>();
        itemToggle.isOn = false;

        // Wire dropdown
        var scroll = template.GetComponent<ScrollRect>();
        scroll.viewport = vRT;
        scroll.content = cRT;
        scroll.horizontal = false;
        scroll.vertical = true;

        dd.template = tRT;
        dd.captionText = label;
        dd.itemText = itemLabelText;
        dd.ClearOptions();
        dd.options = new List<TMP_Dropdown.OptionData>();

        return dd;
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
        req.SetRequestHeader("Authorization", "Bearer " + keyField.text.Trim());
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

        if (fetchedConfigs.Count == 0)
        {
            SetStatus("Server returned no configs.",
                      new Color(0.95f, 0.55f, 0.35f));
            configDropdown.ClearOptions();
        }
        else
        {
            var opts = new List<TMP_Dropdown.OptionData>();
            for (int i = 0; i < fetchedConfigs.Count; i++)
            {
                var c = fetchedConfigs[i];
                int n = (c.agents != null) ? c.agents.Length : 0;
                opts.Add(new TMP_Dropdown.OptionData($"{c.name}  ({n} agents)"));
            }
            configDropdown.ClearOptions();
            configDropdown.AddOptions(opts);

            // Pre-select last-used config if it's still there.
            string lastCfg = PlayerPrefs.GetString(PREFS_CFG, "");
            int found = 0;
            for (int i = 0; i < fetchedConfigs.Count; i++)
                if (fetchedConfigs[i].name == lastCfg) { found = i; break; }
            configDropdown.value = found;
            configDropdown.RefreshShownValue();

            SetStatus($"Loaded {fetchedConfigs.Count} configs.",
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
        if (configDropdown.value < 0 || configDropdown.value >= fetchedConfigs.Count)
        {
            SetStatus("Pick a config first.", new Color(0.95f, 0.55f, 0.35f));
            yield break;
        }

        connecting = true;
        SetStartEnabled(false);
        SetConnectEnabled(false);
        if (startButtonLabel != null) startButtonLabel.text = "Connecting…";

        var chosen = fetchedConfigs[configDropdown.value];
        SavePrefs(chosen.name);

        var wsm = WebSocketManager.Instance;
        if (wsm == null)
        {
            SetStatus("WebSocketManager not present in scene.", Color.red);
            connecting = false;
            SetStartEnabled(true);
            SetConnectEnabled(true);
            if (startButtonLabel != null) startButtonLabel.text = "Start Game";
            yield break;
        }

        wsm.serverUrl = urlField.text.Trim();
        wsm.apiKey = keyField.text.Trim();
        wsm.configName = chosen.name;
        wsm.enableWebSocket = true;
        wsm.ConnectToServer();

        // Hide the launcher once we've handed off; WebSocketManager handles
        // hello/ack/game_start from here.
        if (root != null) root.SetActive(false);
    }

    // ── Helpers ────────────────────────────────────────────────────

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
    public class ConfigInfo { public string name; public string path; public AgentInfo[] agents; }
    [Serializable]
    public class AgentInfo { public string name; public string role; public string actor_type; }
}
