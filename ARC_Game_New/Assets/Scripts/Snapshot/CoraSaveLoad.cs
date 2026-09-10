using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Player-facing save/load of `.cora` files.
///
/// ENTRY POINT WITHOUT A SCENE EDIT. This installs itself through
/// RuntimeInitializeOnLoadMethod rather than being dropped on a GameObject in MainScene.
/// MainScene.unity is LFS-tracked, so every scene edit takes one side of a merge whole-file
/// with no 3-way -- adding a button to it while an upstream scene change is unmerged
/// guarantees a conflict. Buttons can be wired to <see cref="SaveToFile"/> and
/// <see cref="LoadFromFile"/> once the scene is settled; the hotkeys work in the meantime
/// and cost nothing.
///
/// LOAD IS A SCENE REBUILD, NOT AN OVERWRITE. Restoring onto a live scene leaves stale
/// objects the snapshot never mentions -- buildings that were demolished, tasks that were
/// answered, a vehicle mid-coroutine -- so the scene is torn down and reloaded first and the
/// snapshot is written over the fresh singletons. This mirrors GymServerManager's
/// LoadStateRoutine deliberately; the two are duplicated rather than shared because the gym
/// reset is calibrated against the surrogate's lockstep corpus and is not worth disturbing
/// for this. Unify them once the upstream merge is in.
/// </summary>
public class CoraSaveLoad : MonoBehaviour
{
    public static CoraSaveLoad Instance { get; private set; }

    /// <summary>Ctrl/Cmd + S saves, Ctrl/Cmd + O loads. Off while a text field has focus.</summary>
    public bool hotkeysEnabled = true;

    /// <summary>Set from the load flow so the snapshot can be applied after the reload.</summary>
    static GameSnapshot pendingRestore;
    static string pendingNote;

    FileIOBridge bridge;
    bool busy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Instance != null) return;
        var go = new GameObject("[CoraSaveLoad]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<CoraSaveLoad>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    IEnumerator Start()
    {
        // A snapshot queued before the reload is applied here, once the reloaded scene's
        // Awake/Start chain has re-established Day 1 and re-applied the config. Two frames
        // is what the gym's reset waits for the same reason.
        if (pendingRestore != null)
        {
            yield return null;
            yield return null;
            var snap = pendingRestore;
            pendingRestore = null;
            ApplyRestore(snap);
        }
    }

    void Update()
    {
        if (!hotkeysEnabled || busy) return;
        bool mod = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        if (!mod) return;
        if (Input.GetKeyDown(KeyCode.S)) SaveToFile();
        else if (Input.GetKeyDown(KeyCode.O)) LoadFromFile();
    }

    // ── save ──────────────────────────────────────────────────────────────────────────

    /// <summary>Capture the current position and hand the player a `.cora` download.</summary>
    public void SaveToFile(string note = "")
    {
        if (busy) return;
        try
        {
            CoraFile file = CoraFileIO.Capture(note);
            GameSnapshot snap = CoraFileIO.ReadSnapshot(file);
            string json = CoraFileIO.ToJson(file);
            string name = CoraFileIO.SuggestFilename(snap);
            Bridge().DownloadText(name, json, "Save CORA game state", CoraFile.EXTENSION);
            Report($"Saved {name} — {file.label}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CoraSaveLoad] save failed: {e}");
            Report("Save failed — see the log.");
        }
    }

    // ── load ──────────────────────────────────────────────────────────────────────────

    /// <summary>Ask the player for a `.cora` file and load it.</summary>
    public void LoadFromFile()
    {
        if (busy) return;
        FileIOBridge b = Bridge();
        b.OnFileImported -= OnPicked;
        b.OnFileImported += OnPicked;
        b.OpenImportPicker("Open CORA game state", CoraFile.EXTENSION, "." + CoraFile.EXTENSION);
    }

    void OnPicked(string text)
    {
        Bridge().OnFileImported -= OnPicked;
        LoadFromJson(text);
    }

    /// <summary>Load a `.cora` payload that is already in hand. Returns false if refused.</summary>
    public bool LoadFromJson(string json)
    {
        CoraFile file = null;
        try { file = CoraFileIO.FromJson(json); }
        catch (System.Exception e) { Debug.LogError($"[CoraSaveLoad] unparsable .cora: {e}"); }

        CoraFile.Check check = CoraFileIO.Inspect(file);
        if (!check.CanLoad)
        {
            Debug.LogError($"[CoraSaveLoad] refused: {check.message}");
            Report(check.message);
            return false;
        }
        if (check.level != CoraFile.Compatibility.Ok)
        {
            // Loud, but not fatal: the file still describes a real position, and refusing
            // it would block the exact case this feature exists for -- someone on a
            // different build sending in a situation to look at.
            Debug.LogWarning($"[CoraSaveLoad] {check.message}");
            Report(check.message);
        }

        GameSnapshot snap = CoraFileIO.ReadSnapshot(file);
        if (snap == null)
        {
            Report("That .cora carries no readable snapshot.");
            return false;
        }

        pendingRestore = snap;
        pendingNote = string.IsNullOrEmpty(file.note) ? file.label : file.note;
        StartCoroutine(RebuildThenRestore());
        return true;
    }

    IEnumerator RebuildThenRestore()
    {
        busy = true;
        Scene active = SceneManager.GetActiveScene();
        int buildIndex = active.buildIndex;

        // Tear down the DontDestroyOnLoad game-state roots, keeping only infra -- this
        // object included, since it carries the queued snapshot across the reload.
        var keep = new HashSet<GameObject> { gameObject };
        KeepIfPresent<WebSocketManager>(keep);
        KeepIfPresent<GameConfigLoader>(keep);
        KeepIfPresent<GymServerManager>(keep);
        RewardMetricsTracker.Instance?.ResetForNewEpisode();
        WebSocketManager.Instance?.ClearSceneRefs();

        var probe = new GameObject("[CoraLoadProbe]");
        DontDestroyOnLoad(probe);
        keep.Add(probe);
        foreach (GameObject root in probe.scene.GetRootGameObjects())
            if (!keep.Contains(root)) Destroy(root);
        Destroy(probe);

        // Let end-of-frame destruction run so the reloaded scene's fresh singletons see
        // Instance == null and claim the slot instead of self-destructing.
        yield return null;

        AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
        while (op != null && !op.isDone) yield return null;

        // Start() applies `pendingRestore` after the reloaded scene settles. Restoring here
        // instead would write over singletons that have not finished their Awake chain.
        busy = false;
    }

    void ApplyRestore(GameSnapshot snap)
    {
        try
        {
            ActionExecutor.Instance?.ReresolveSceneRefs();
            GameSnapshotManager.Restore(snap);
            Debug.Log($"[CoraSaveLoad] loaded — Day {snap.clock.currentDay}, "
                    + $"round {snap.clock.currentTimeSegment}, budget {snap.economy.currentBudget}");
            Report(string.IsNullOrEmpty(pendingNote) ? "Loaded .cora" : "Loaded: " + pendingNote);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CoraSaveLoad] restore failed: {e}");
            Report("Load failed after the scene rebuild — see the log.");
        }
        pendingNote = null;
    }

    // ── plumbing ──────────────────────────────────────────────────────────────────────

    static void KeepIfPresent<T>(HashSet<GameObject> keep) where T : Component
    {
        var obj = FindObjectOfType<T>();
        if (obj != null) keep.Add(obj.gameObject);
    }

    FileIOBridge Bridge()
    {
        if (bridge != null) return bridge;
        bridge = FileIOBridge.Instance;
        if (bridge == null) bridge = FindObjectOfType<FileIOBridge>();
        if (bridge == null)
        {
            // WebGL SendMessage addresses the bridge BY GAMEOBJECT NAME, so the name here
            // must be the one the jslib callback is given -- it is passed gameObject.name.
            var go = new GameObject("FileIOBridge");
            DontDestroyOnLoad(go);
            bridge = go.AddComponent<FileIOBridge>();
        }
        return bridge;
    }

    static void Report(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        Debug.Log("[CoraSaveLoad] " + message);
        ToastManager.ShowToast(message, ToastType.Info, true);
    }
}
