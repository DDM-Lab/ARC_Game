using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// World-space overlay for delivery route visualization.
/// Draws a coloured line along the road path, directional chevrons every N units,
/// and SpriteRenderer ring markers at the source (green) and destination (red) buildings.
/// Attach once to a persistent scene GameObject; reference from VehicleInfoPanel.
/// </summary>
public class DeliveryRouteVisualizer : MonoBehaviour
{
    [Header("Route Line")]
    public Color routeColor = new Color(1f, 0.82f, 0.1f, 0.95f);
    public float lineWidth  = 0.18f;

    [Header("Endpoint Markers")]
    public Color sourceMarkerColor = new Color(0.15f, 1f, 0.4f, 1f);
    public Color destMarkerColor   = new Color(1f, 0.3f, 0.3f, 1f);
    public float markerSize        = 1.2f;   // world-unit diameter of the ring

    [Header("Direction Chevrons")]
    public float chevronSpacing = 2.5f;
    public float chevronSize    = 0.32f;
    public float chevronWidth   = 0.1f;

    [Header("Rendering")]
    public string sortingLayerName = "Default";
    public int    sortingOrder     = 25;

    public static DeliveryRouteVisualizer Instance { get; private set; }

    private LineRenderer       routeLine;
    private SpriteRenderer     sourceMarker;
    private SpriteRenderer     destMarker;
    private List<LineRenderer> chevrons = new List<LineRenderer>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        Sprite ringSprite = CreateRingSprite(128, 14);

        routeLine    = MakeLine("RouteLine", lineWidth, routeColor, loop: false);
        sourceMarker = MakeMarker("SourceMarker", ringSprite, sourceMarkerColor);
        destMarker   = MakeMarker("DestMarker",   ringSprite, destMarkerColor);

        routeLine.gameObject.SetActive(false);
        sourceMarker.gameObject.SetActive(false);
        destMarker.gameObject.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────────

    public void ShowRoute(List<Vector3> path, Vector3 sourceWorldPos, Vector3 destWorldPos)
    {
        HideRoute();
        if (path == null || path.Count < 2) return;

        const float z = -2f;

        // Route line
        routeLine.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
            routeLine.SetPosition(i, XY(path[i], z));
        routeLine.gameObject.SetActive(true);

        // Endpoint markers
        PlaceMarker(sourceMarker, sourceWorldPos, z - 0.1f);
        PlaceMarker(destMarker,   destWorldPos,   z - 0.1f);

        // Directional chevrons
        DrawChevrons(path, z - 0.05f);
    }

    public void HideRoute()
    {
        routeLine.positionCount = 0;
        routeLine.gameObject.SetActive(false);
        sourceMarker.gameObject.SetActive(false);
        destMarker.gameObject.SetActive(false);
        foreach (var c in chevrons)
            if (c != null) c.gameObject.SetActive(false);
    }

    // ── Chevron placement ─────────────────────────────────────────────

    void DrawChevrons(List<Vector3> path, float z)
    {
        float acc = 0f;
        int idx   = 0;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a      = path[i];
            Vector3 b      = path[i + 1];
            float   segLen = Vector3.Distance(a, b);
            if (segLen < 0.001f) continue;

            float used = 0f;
            while (true)
            {
                float need = chevronSpacing - acc;
                if (need > segLen - used) { acc += segLen - used; break; }

                used += need;
                acc   = 0f;

                Vector3 pos = Vector3.Lerp(a, b, used / segLen);
                PlaceChevron(idx++, XY(pos, z), (b - a).normalized);
            }
        }
    }

    void PlaceChevron(int index, Vector3 center, Vector3 forward)
    {
        while (chevrons.Count <= index)
            chevrons.Add(MakeLine($"Chevron_{chevrons.Count}", chevronWidth, routeColor, loop: false));

        LineRenderer c = chevrons[index];
        forward.z = 0f;
        if (forward.sqrMagnitude < 0.001f) { c.gameObject.SetActive(false); return; }
        forward.Normalize();

        Vector3 perp  = new Vector3(-forward.y, forward.x, 0f);
        Vector3 tip   = center + forward * chevronSize;
        Vector3 left  = center - forward * (chevronSize * 0.5f) + perp * (chevronSize * 0.6f);
        Vector3 right = center - forward * (chevronSize * 0.5f) - perp * (chevronSize * 0.6f);

        c.positionCount = 3;
        c.SetPosition(0, left);
        c.SetPosition(1, tip);
        c.SetPosition(2, right);
        c.gameObject.SetActive(true);
    }

    // ── Factory helpers ───────────────────────────────────────────────

    LineRenderer MakeLine(string goName, float width, Color color, bool loop)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);
        var lr              = go.AddComponent<LineRenderer>();
        lr.material         = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
        lr.startColor       = color;
        lr.endColor         = color;
        lr.startWidth       = width;
        lr.endWidth         = width;
        lr.positionCount    = 0;
        lr.useWorldSpace    = true;
        lr.loop             = loop;
        lr.sortingLayerName = sortingLayerName;
        lr.sortingOrder     = sortingOrder;
        lr.numCapVertices   = 3;
        return lr;
    }

    SpriteRenderer MakeMarker(string goName, Sprite sprite, Color color)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);
        var sr              = go.AddComponent<SpriteRenderer>();
        sr.sprite           = sprite;
        sr.color            = color;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder     = sortingOrder + 2;
        return sr;
    }

    void PlaceMarker(SpriteRenderer marker, Vector3 worldPos, float z)
    {
        marker.transform.position   = XY(worldPos, z);
        marker.transform.localScale = Vector3.one * markerSize;
        marker.gameObject.SetActive(true);
    }

    // ── Ring sprite ───────────────────────────────────────────────────

    // Creates a white ring (hollow circle) texture. thickness is in pixels.
    static Sprite CreateRingSprite(int diameter, int thickness)
    {
        var tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        float cx  = diameter * 0.5f;
        float outerR2 = (cx - 0.5f) * (cx - 0.5f);
        float innerR2 = (cx - thickness - 0.5f) * (cx - thickness - 0.5f);

        var pixels = new Color32[diameter * diameter];
        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dx = x - cx + 0.5f;
                float dy = y - cx + 0.5f;
                float d2 = dx * dx + dy * dy;
                pixels[y * diameter + x] = (d2 <= outerR2 && d2 >= innerR2)
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(0, 0, 0, 0);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, diameter, diameter),
                             new Vector2(0.5f, 0.5f), diameter);
    }

    static Vector3 XY(Vector3 v, float z) => new Vector3(v.x, v.y, z);
}
