using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// simple minimap implementation<br/>
    /// automatically moves a camera to fit the entire map<br/>
    /// can be clicked and dragged on the set the main camera position
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_minimap.html")]
    public class Minimap : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        [Tooltip("the camera that will be moved/sized to fit the entire map")]
        public Camera Camera;
        [Tooltip("the image used to transform from a clicked point on the minimap to world coordinates")]
        public RawImage Image;

        private void Start()
        {
            var map = Dependencies.Get<IMap>();

            if (map.IsXY)
            {
                Camera.transform.position = new Vector3(map.WorldCenter.x, map.WorldCenter.y, Camera.transform.position.z);
            }
            else
            {
                Camera.transform.position = new Vector3(map.WorldCenter.x, Camera.transform.position.y, map.WorldCenter.z);
            }

            if (Camera.orthographic)
            {
                Camera.orthographicSize = Mathf.Max(map.Size.x / 2f * map.CellOffset.x, map.Size.y / 2f * map.CellOffset.y);
            }
            else
            {
                var distance = map.Size.x * map.CellOffset.x * 0.5f / Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

                if (map.IsXY)
                {
                    Camera.transform.position = new Vector3(Camera.transform.position.x, Camera.transform.position.y, -distance);
                }
                else
                {
                    Camera.transform.position = new Vector3(Camera.transform.position.x, distance, Camera.transform.position.z);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData) => setPosition();
        public void OnDrag(PointerEventData eventData) => setPosition();

        private void setPosition()
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Image.rectTransform, Input.mousePosition, GetComponentInParent<Canvas>().worldCamera, out Vector2 localPoint))
                return;

            Vector2 normalizedPoint = Rect.PointToNormalized(Image.rectTransform.rect, localPoint);
            Vector3 viewportPoint = new Vector3(normalizedPoint.x, normalizedPoint.y, Camera.transform.position.y);

            var mainCamera = Dependencies.Get<IMainCamera>();
            var worldPoint = Camera.ViewportToWorldPoint(viewportPoint);

            var map = Dependencies.Get<IMap>();

            if (map.IsXY)
                mainCamera.Position = new Vector3(worldPoint.x, worldPoint.y, 0f);
            else
                mainCamera.Position = new Vector3(worldPoint.x, 0f, worldPoint.z);
        }
    }
}