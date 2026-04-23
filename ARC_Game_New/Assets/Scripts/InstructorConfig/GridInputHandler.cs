using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach this to the same GameObject as the RawImage (GridImage).
/// Forwards all pointer events to MapEditorCanvas so the canvas script
/// can stay on the parent panel without needing an EventTrigger component.
///
/// Requires the GameObject to have a Graphic component (RawImage qualifies)
/// so the EventSystem can detect hits.
/// </summary>
[RequireComponent(typeof(UnityEngine.UI.RawImage))]
public class GridInputHandler : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IDragHandler,
    IPointerMoveHandler,
    IPointerExitHandler
{
    [Tooltip("The MapEditorCanvas script living on the MapEditorPanel")]
    public MapEditorCanvas editorCanvas;

    public void OnPointerDown (PointerEventData e) => editorCanvas.OnGridPointerDown(e);
    public void OnPointerUp   (PointerEventData e) => editorCanvas.OnGridPointerUp(e);
    public void OnDrag        (PointerEventData e) => editorCanvas.OnGridDrag(e);
    public void OnPointerMove (PointerEventData e) => editorCanvas.OnGridPointerMove(e);
    public void OnPointerExit (PointerEventData e) => editorCanvas.OnGridPointerExit(e);
}
