using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Captures EVERY human mouse click: screen + normalized coords, plus the UGUI element hit
/// (or, when no UI element was hit, the world object under the cursor). Each click is written
/// to the game log as a "click" Data record, which the upload carries to the lab in every
/// build, LLM or not; when connected it is also sent to the router as a raw gui_event. This is
/// the every-click stream behind GUI-agent training and the "unproductive click" confusion
/// signal. (It used to record only when connected to the router, so the plain human build lost
/// every click.)
///
/// It also owns <see cref="LastClickSeq"/>, a monotonic per-click id. Because this
/// component's Update() runs before UGUI dispatches the click to a button handler
/// in the same frame, any semantic event that handler emits (via
/// GameLogPanel.LogUIInteraction / WebSocketManager.SendClientEvent /
/// SendChoiceMade / SendDirectorMessage) carries the matching click_seq, so the
/// raw click and its meaning join offline.
///
/// Self-instantiates via RuntimeInitializeOnLoadMethod (mirrors ServerLauncherUI),
/// so no scene wiring is needed. Skipped in batchmode → the headless/gym build
/// never creates it. Purely passive: it never consumes input, so it cannot
/// interfere with gameplay or UI.
/// </summary>
public class GuiInteractionRecorder : MonoBehaviour
{
    public static long LastClickSeq { get; private set; } = -1;
    private long nextClickSeq = 0;

    // Reused per click to avoid GC churn.
    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstantiate()
    {
        if (Application.isBatchMode) return;                 // headless/gym: no human mouse
        if (FindObjectOfType<GuiInteractionRecorder>() != null) return;
        var go = new GameObject("[GuiInteractionRecorder]");
        DontDestroyOnLoad(go);
        go.AddComponent<GuiInteractionRecorder>();
    }

    void Update()
    {
        for (int button = 0; button <= 1; button++)          // 0 = left, 1 = right
        {
            if (!Input.GetMouseButtonDown(button)) continue;
            // Bump the id FIRST so a same-frame UGUI handler reads the right seq.
            LastClickSeq = nextClickSeq++;
            CaptureClick(button, Input.mousePosition);
        }
    }

    void CaptureClick(int button, Vector3 screenPos)
    {
        float sw = Screen.width, sh = Screen.height;
        float nx = sw > 0 ? screenPos.x / sw : 0f;
        float ny = sh > 0 ? screenPos.y / sh : 0f;

        string hitName = null, hitType = null, hitPath = null;
        string canvasName = null;
        float clx = 0f, cly = 0f;

        EventSystem es = EventSystem.current;
        if (es != null)
        {
            var ped = new PointerEventData(es) { position = screenPos };
            raycastResults.Clear();
            es.RaycastAll(ped, raycastResults);
            if (raycastResults.Count > 0)
            {
                GameObject rawHit = raycastResults[0].gameObject;

                // Prefer the nearest interactable control (Button/Toggle/etc.) over
                // a child graphic that happened to be the raycast target.
                Selectable sel = rawHit.GetComponentInParent<Selectable>();
                GameObject target = sel != null ? sel.gameObject : rawHit;

                hitName = target.name;
                hitPath = HierarchyPath(target.transform);
                hitType = ComponentTypeName(target);

                Canvas canvas = target.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    canvasName = canvas.name;
                    RectTransform crt = canvas.transform as RectTransform;
                    Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                        ? null : canvas.worldCamera;
                    Vector2 local;
                    if (crt != null &&
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            crt, screenPos, cam, out local))
                    {
                        clx = local.x; cly = local.y;
                    }
                }
            }
        }

        // Map objects (sites, buildings, vehicles) are not UI, so a click on one hits no UGUI
        // element. Name the world object under the cursor instead, so the click is not blank.
        string worldName = null, worldPath = null;
        if (hitName == null) WorldHit(screenPos, out worldName, out worldPath);

        GameLogPanel.Instance?.LogData("click", GameLogPanel.Json(
            "button", button,
            "x", screenPos.x, "y", screenPos.y, "screen_w", sw, "screen_h", sh,
            "nx", nx, "ny", ny,
            "canvas", canvasName, "canvas_x", clx, "canvas_y", cly,
            "ui_name", hitName, "ui_type", hitType, "ui_path", hitPath,
            "world_name", worldName, "world_path", worldPath));

        var ws = WebSocketManager.Instance;
        if (ws != null && ws.isConnected)
            ws.SendGuiEvent(
                LastClickSeq, button,
                screenPos.x, screenPos.y, sw, sh, nx, ny,
                canvasName, clx, cly,
                hitName, hitType, hitPath);
    }

    static void WorldHit(Vector3 screenPos, out string name, out string path)
    {
        name = path = null;
        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(screenPos);
        RaycastHit2D h2 = Physics2D.GetRayIntersection(ray);
        Transform t = h2.collider != null ? h2.collider.transform : null;
        if (t == null && Physics.Raycast(ray, out RaycastHit h3)) t = h3.collider.transform;
        if (t == null) return;
        name = t.name;
        path = HierarchyPath(t);
    }

    static string ComponentTypeName(GameObject go)
    {
        var sel = go.GetComponent<Selectable>();
        if (sel != null) return sel.GetType().FullName;
        var gfx = go.GetComponent<Graphic>();
        if (gfx != null) return gfx.GetType().FullName;
        return "UnityEngine.GameObject";
    }

    static string HierarchyPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent)
            sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
