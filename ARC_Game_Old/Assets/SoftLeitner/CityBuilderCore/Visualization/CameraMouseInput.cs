using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// provides access to the position of the players pointer position using the <see cref="IMouseInput"/> interface<br/>
    /// usually this is done by the <see cref="CameraController"/>, this behaviour can be used instead when the camera is static
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_camera_mouse_input.html")]
    [RequireComponent(typeof(Camera))]
    public class CameraMouseInput : MonoBehaviour, IMouseInput
    {
        [Tooltip("optional, used when calculating where the mouse is on the grid")]
        public Collider MouseCollider;
        [Tooltip("offset between touch position and cursor positon, can be used so that the finger does not cover the thing it interacts with")]
        public Vector2 TouchOffset = Vector3.zero;

        private Camera _camera;
        private IMap _map;

        protected virtual void Awake()
        {
            _camera = GetComponent<Camera>();

            Dependencies.Register<IMouseInput>(this);
        }

        protected virtual void Start()
        {
            _map = Dependencies.Get<IMap>();
        }

        public Ray GetRay(bool applyOffset = false) => _camera.ScreenPointToRay(GetMouseScreenPosition(applyOffset));
        public Vector3 GetMousePosition(bool applyOffset = false)
        {
            var ray = GetRay(applyOffset);
            if (ray.IsInvalid())
                return Vector3.positiveInfinity;

            if (MouseCollider)
            {
                if (MouseCollider.Raycast(ray, out RaycastHit _hit, float.MaxValue))
                {
                    return _hit.point;
                }
            }

            var plane = new Plane(_map.IsXY ? Vector3.forward : Vector3.up, Vector3.zero);

            if (plane.Raycast(ray, out float distance))
            {
                return ray.GetPoint(distance);
            }
            else
            {
                if (_map.IsXY)
                    ray.direction = new Vector3(-ray.direction.x, -ray.direction.y, ray.direction.z);
                else
                    ray.direction = new Vector3(-ray.direction.x, ray.direction.y, -ray.direction.z);

                return ray.GetPoint(distance);
            }
        }
        public Vector2 GetMouseScreenPosition(bool applyOffset = false)
        {
            Vector2 position;

            if (Input.touchCount == 0)
                position = Input.mousePosition;
            else
                position = Input.GetTouch(0).position;

            if (applyOffset)
                return position + TouchOffset;
            else
                return position;
        }

        public Vector2Int GetMouseGridPosition(bool applyOffset = false)
        {
            var mousePosition = GetMousePosition(applyOffset);
            if (mousePosition.IsInvalid())
                return Vector2Int.zero;
            return _map.ClampPoint(Dependencies.Get<IGridPositions>().GetGridPoint(mousePosition));
        }
    }
}