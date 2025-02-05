using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// shows main camera view area on minimap
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_camera_area.html")]
    [RequireComponent(typeof(LineRenderer))]
    public class CameraArea : MonoBehaviour
    {
        private LineRenderer _lineRenderer;
        private Vector3[] _positions;
        private IMap _map;
        private IMainCamera _mainCamera;

        private void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            _positions = new Vector3[4];
        }

        private void Start()
        {
            _map = Dependencies.Get<IMap>();
            _mainCamera = Dependencies.Get<IMainCamera>();
        }

        private void Update()
        {
            Ray topLeft = _mainCamera.Camera.ViewportPointToRay(new Vector3(0, 0, 0));
            Ray topRight = _mainCamera.Camera.ViewportPointToRay(new Vector3(1, 0, 0));
            Ray botRight = _mainCamera.Camera.ViewportPointToRay(new Vector3(1, 1, 0));
            Ray botLeft = _mainCamera.Camera.ViewportPointToRay(new Vector3(0, 1, 0));

            Plane plane;

            if (_map.IsXY)
                plane = new Plane(Vector3.forward, 0);
            else
                plane = new Plane(Vector3.up, 0);

            plane.Raycast(topLeft, out float topLeftEnter);
            plane.Raycast(topRight, out float topRightEnter);
            plane.Raycast(botRight, out float botRightEnter);
            plane.Raycast(botLeft, out float botLeftEnter);

            _positions[0] = topLeft.GetPoint(topLeftEnter);
            _positions[1] = topRight.GetPoint(topRightEnter);
            _positions[2] = botRight.GetPoint(botRightEnter < 0 ? 1000 : botRightEnter);
            _positions[3] = botLeft.GetPoint(Mathf.Abs(botLeftEnter < 0 ? 1000 : botLeftEnter));

            _lineRenderer.SetPositions(_positions);
        }
    }
}