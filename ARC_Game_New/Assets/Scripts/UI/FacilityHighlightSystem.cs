using System.Collections;
using UnityEngine;

public class FacilityHighlightSystem : MonoBehaviour
{
    public static FacilityHighlightSystem Instance { get; private set; }

    [Header("Highlight Settings")]
    public Color highlightColor = new Color(1f, 0.85f, 0.1f, 1f);
    public float highlightDuration = 2f;
    public float pulseFrequency = 3f;

    [Header("Route Highlight Colors")]
    public Color sourceHighlightColor = new Color(0.2f, 1f, 0.35f, 1f);
    public Color destHighlightColor   = new Color(0.35f, 0.65f, 1f, 1f);

    [Header("Camera Pan")]
    public float cameraPanDuration = 0.5f;

    [Header("Route Line")]
    public LineRenderer routeLine;

    public float TotalDuration => cameraPanDuration * 2f + highlightDuration;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (routeLine != null)
            routeLine.gameObject.SetActive(false);
    }

    // ── Single facility highlight ─────────────────────────────────────────────

    public void HighlightFacility(string facilityObjectName)
    {
        StartCoroutine(RunHighlight(facilityObjectName));
    }

    IEnumerator RunHighlight(string facilityObjectName)
    {
        MonoBehaviour facility = FindFacility(facilityObjectName);
        if (facility == null) yield break;

        UIToggleButton.Instance?.SetHidden(true);

        Vector3 originalCameraPos = Vector3.zero;
        bool movedCamera = false;
        if (Camera.main != null)
        {
            originalCameraPos = Camera.main.transform.position;
            Vector3 facilityPos = facility.transform.position;
            facilityPos.z = originalCameraPos.z;
            yield return StartCoroutine(PanCamera(originalCameraPos, facilityPos));
            movedCamera = true;
        }

        SpriteRenderer sr = facility.GetComponentInChildren<SpriteRenderer>();
        if (sr == null)
        {
            if (movedCamera) yield return StartCoroutine(PanCamera(Camera.main.transform.position, originalCameraPos));
            UIToggleButton.Instance?.SetHidden(false);
            yield break;
        }

        yield return StartCoroutine(PulseSprite(sr, highlightColor));

        if (movedCamera)
            yield return StartCoroutine(PanCamera(Camera.main.transform.position, originalCameraPos));

        UIToggleButton.Instance?.SetHidden(false);
    }

    // ── Delivery route highlight ──────────────────────────────────────────────

    public void HighlightRoute(MonoBehaviour source, MonoBehaviour dest)
    {
        StartCoroutine(RunRouteHighlight(source, dest));
    }

    public void HighlightMultiSourceRoute(System.Collections.Generic.List<MonoBehaviour> sources, MonoBehaviour dest, System.Action onComplete = null)
    {
        if (sources == null || sources.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }
        if (sources.Count == 1)
        {
            PreviewRouteAndCallback(sources[0], dest, onComplete);
            return;
        }

        StartCoroutine(RunMultiSourceRouteHighlight(sources, dest, onComplete));
    }

    IEnumerator RunMultiSourceRouteHighlight(System.Collections.Generic.List<MonoBehaviour> sources, MonoBehaviour dest, System.Action onComplete)
    {
        UIToggleButton.Instance?.SetHidden(true);

        Vector3 originalCameraPos = Vector3.zero;
        bool movedCamera = false;
        if (Camera.main != null && sources.Count > 0 && dest != null)
        {
            originalCameraPos = Camera.main.transform.position;
            Vector3 sum = dest.transform.position;
            foreach (var s in sources) sum += s.transform.position;
            Vector3 midpoint = sum / (sources.Count + 1);
            midpoint.z = originalCameraPos.z;
            yield return StartCoroutine(PanCamera(originalCameraPos, midpoint));
            movedCamera = true;
        }
        ShowMultiSourceRouteLine(sources, dest);

        var srs = new System.Collections.Generic.List<SpriteRenderer>();
        var originals = new System.Collections.Generic.List<Color>();
        foreach (var s in sources)
        {
            var sr = s.GetComponentInChildren<SpriteRenderer>();
            srs.Add(sr);
            originals.Add(sr != null ? sr.color : Color.white);
        }
        SpriteRenderer dstSR = dest != null ? dest.GetComponentInChildren<SpriteRenderer>() : null;
        Color dstOriginal = dstSR != null ? dstSR.color : Color.white;

        float elapsed = 0f;
        while (elapsed < highlightDuration)
        {
            float t = (Mathf.Sin(elapsed * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
            for (int i = 0; i < srs.Count; i++)
                if (srs[i] != null) srs[i].color = Color.Lerp(originals[i], sourceHighlightColor, t);
            if (dstSR != null) dstSR.color = Color.Lerp(dstOriginal, destHighlightColor, t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        for (int i = 0; i < srs.Count; i++)
            if (srs[i] != null) srs[i].color = originals[i];
        if (dstSR != null) dstSR.color = dstOriginal;

        HideRouteLine();

        if (movedCamera)
            yield return StartCoroutine(PanCamera(Camera.main.transform.position, originalCameraPos));

        UIToggleButton.Instance?.SetHidden(false);
        onComplete?.Invoke();
    }

    IEnumerator RunRouteHighlight(MonoBehaviour source, MonoBehaviour dest)
    {
        UIToggleButton.Instance?.SetHidden(true);

        Vector3 originalCameraPos = Vector3.zero;
        bool movedCamera = false;
        if (Camera.main != null)
        {
            originalCameraPos = Camera.main.transform.position;
            Vector3 midpoint = (source.transform.position + dest.transform.position) * 0.5f;
            midpoint.z = originalCameraPos.z;
            yield return StartCoroutine(PanCamera(originalCameraPos, midpoint));
            movedCamera = true;
        }

        // Draw route arrow
        ShowRouteLine(source.transform.position, dest.transform.position);

        // Pulse both buildings simultaneously with distinct colors
        SpriteRenderer srcSR = source.GetComponentInChildren<SpriteRenderer>();
        SpriteRenderer dstSR = dest.GetComponentInChildren<SpriteRenderer>();

        Color srcOriginal = srcSR != null ? srcSR.color : Color.white;
        Color dstOriginal = dstSR != null ? dstSR.color : Color.white;

        float elapsed = 0f;
        while (elapsed < highlightDuration)
        {
            float t = (Mathf.Sin(elapsed * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
            if (srcSR != null) srcSR.color = Color.Lerp(srcOriginal, sourceHighlightColor, t);
            if (dstSR != null) dstSR.color = Color.Lerp(dstOriginal, destHighlightColor,   t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (srcSR != null) srcSR.color = srcOriginal;
        if (dstSR != null) dstSR.color = dstOriginal;
        HideRouteLine();

        if (movedCamera)
            yield return StartCoroutine(PanCamera(Camera.main.transform.position, originalCameraPos));

        UIToggleButton.Instance?.SetHidden(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    IEnumerator PulseSprite(SpriteRenderer sr, Color target)
    {
        Color original = sr.color;
        float elapsed = 0f;
        while (elapsed < highlightDuration)
        {
            float t = (Mathf.Sin(elapsed * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
            sr.color = Color.Lerp(original, target, t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        sr.color = original;
    }

    void ShowRouteLine(Vector3 from, Vector3 to)
    {
        if (routeLine == null) return;
        routeLine.positionCount = 2;
        routeLine.SetPosition(0, new Vector3(from.x, from.y, 0f));
        routeLine.SetPosition(1, new Vector3(to.x,   to.y,   0f));
        routeLine.gameObject.SetActive(true);
    }

    void ShowMultiSourceRouteLine(System.Collections.Generic.List<MonoBehaviour> sources, MonoBehaviour dest)
    {
        if (routeLine == null || dest == null || sources == null || sources.Count == 0) return;

        var points = new System.Collections.Generic.List<Vector3>();
        Vector3 destPos = new Vector3(dest.transform.position.x, dest.transform.position.y, 0f);

        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i] == null) continue;
            Vector3 srcPos = new Vector3(sources[i].transform.position.x, sources[i].transform.position.y, 0f);
            points.Add(srcPos);
            points.Add(destPos);
        }

        if (points.Count == 0) return;

        routeLine.positionCount = points.Count;
        routeLine.SetPositions(points.ToArray());
        routeLine.gameObject.SetActive(true);
    }

    void HideRouteLine()
    {
        if (routeLine != null)
            routeLine.gameObject.SetActive(false);
    }

    IEnumerator PanCamera(Vector3 from, Vector3 to)
    {
        float elapsed = 0f;
        while (elapsed < cameraPanDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / cameraPanDuration));
            Camera.main.transform.position = Vector3.Lerp(from, to, t);
            yield return null;
        }
        Camera.main.transform.position = to;
    }

    MonoBehaviour FindFacility(string objectName)
    {
        foreach (Building b in FindObjectsOfType<Building>())
            if (b.name == objectName) return b;

        foreach (PrebuiltBuilding pb in FindObjectsOfType<PrebuiltBuilding>())
            if (pb.name == objectName) return pb;

        return null;
    }

    /// <summary>
    /// Used to notify when done with route highlight, since Highlight hides all UI.
    /// </summary>
    public void PreviewRouteAndCallback(MonoBehaviour source, MonoBehaviour dest, System.Action onComplete)
    {
        StartCoroutine(RunRoutePreviewSequence(source, dest, onComplete));
    }

    private IEnumerator RunRoutePreviewSequence(MonoBehaviour source, MonoBehaviour dest, System.Action onComplete)
    {
        yield return StartCoroutine(RunRouteHighlight(source, dest));
        onComplete?.Invoke();
    }


    /// <summary>
    /// Used to notify when done with facility highlight, since Highlight hides all UI.
    /// </summary>
    public void HighlightFacilityWithCallback(string facilityObjectName, System.Action onComplete)
    {
        StartCoroutine(RunFacilityHighlightSequence(facilityObjectName, onComplete));
    }

    private IEnumerator RunFacilityHighlightSequence(string facilityObjectName, System.Action onComplete)
    {
        yield return StartCoroutine(RunHighlight(facilityObjectName));
        onComplete?.Invoke();
    }
}
