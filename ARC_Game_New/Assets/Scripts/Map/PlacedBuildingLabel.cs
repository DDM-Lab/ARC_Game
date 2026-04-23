using TMPro;
using UnityEngine;

public class PlacedBuildingLabel : MonoBehaviour
{
    public Transform target;
    public Vector2   uiOffset = new Vector2(0f, 40f);

    RectTransform _rt;
    Canvas        _canvas;
    Camera        _cam;

    void Awake()
    {
        _rt            = GetComponent<RectTransform>();
        _rt.anchorMin  = new Vector2(0.5f, 0.5f);
        _rt.anchorMax  = new Vector2(0.5f, 0.5f);
        _rt.pivot      = new Vector2(0.5f, 0.5f);
        _rt.localScale = Vector3.one;
    }

    void Start()
    {
        _cam    = Camera.main;
        _canvas = GetComponentInParent<Canvas>();
    }

    void LateUpdate()
    {
        if (target == null || _cam == null || _canvas == null) return;

        Vector3 worldPos    = target.position;
        Vector3 viewportPos = _cam.WorldToViewportPoint(worldPos);

        bool visible = viewportPos.z > 0 &&
                       viewportPos.x >= 0 && viewportPos.x <= 1 &&
                       viewportPos.y >= 0 && viewportPos.y <= 1;

        if (!visible) { gameObject.SetActive(false); return; }
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        RectTransform canvasRect = _canvas.GetComponent<RectTransform>();
        Vector2 canvasSize = canvasRect.sizeDelta;
        Vector2 screenPos = new Vector2(
            viewportPos.x * canvasSize.x - canvasSize.x * 0.5f,
            viewportPos.y * canvasSize.y - canvasSize.y * 0.5f);

        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            screenPos = _cam.WorldToScreenPoint(worldPos);

        _rt.anchoredPosition = screenPos + uiOffset;
    }

    public void SetText(string text)
    {
        var tmp = GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.text = text;
    }
}
