using System.Collections;
using UnityEngine;

public class FacilityHighlightSystem : MonoBehaviour
{
    public static FacilityHighlightSystem Instance { get; private set; }

    [Header("Highlight Settings")]
    public Color highlightColor = new Color(1f, 0.85f, 0.1f, 1f);
    public float highlightDuration = 2f;
    public float pulseFrequency = 3f;

    [Header("Camera Pan")]
    public float cameraPanDuration = 0.5f;

    public float TotalDuration => cameraPanDuration * 2f + highlightDuration;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void HighlightFacility(string facilityObjectName)
    {
        StartCoroutine(RunHighlight(facilityObjectName));
    }

    IEnumerator RunHighlight(string facilityObjectName)
    {
        MonoBehaviour facility = FindFacility(facilityObjectName);
        if (facility == null) yield break;

        UIToggleButton.Instance?.SetHidden(true);

        // Pan to facility
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

        // Pulse highlight
        SpriteRenderer sr = facility.GetComponentInChildren<SpriteRenderer>();
        if (sr == null)
        {
            if (movedCamera) yield return StartCoroutine(PanCamera(Camera.main.transform.position, originalCameraPos));
            UIToggleButton.Instance?.SetHidden(false);
            yield break;
        }

        Color originalColor = sr.color;
        float elapsed = 0f;
        while (elapsed < highlightDuration)
        {
            float t = (Mathf.Sin(elapsed * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
            sr.color = Color.Lerp(originalColor, highlightColor, t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        sr.color = originalColor;

        // Pan back
        if (movedCamera)
            yield return StartCoroutine(PanCamera(Camera.main.transform.position, originalCameraPos));

        UIToggleButton.Instance?.SetHidden(false);
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
}
