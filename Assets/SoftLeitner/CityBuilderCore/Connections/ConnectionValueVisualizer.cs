using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// shows the numerical connection value of a point on the map thats under the mouse<br/>
    /// gets automatically activated by <see cref="DefaultOverlayManager"/> when a connectionview is shown
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection_value_visualizer.html")]
    public class ConnectionValueVisualizer : MonoBehaviour
    {
        [Tooltip("root that will be moved around with the mouse")]
        public RectTransform Root;
        [Tooltip("object that gets activated and deactivated when the visualizer is shown/hidden")]
        public GameObject Visual;
        [Tooltip("label for the value")]
        public TMPro.TMP_Text ValueText;

        private IConnectionManager _connectionManager;
        private IMouseInput _mouseInput;
        private IMap _map;

        private Vector2Int _activeMousePoint;
        private Connection _activeConnection;
        private Canvas _currentCanvas;

        private void Start()
        {
            _connectionManager = Dependencies.Get<IConnectionManager>();
            _mouseInput = Dependencies.Get<IMouseInput>();
            _map = Dependencies.Get<IMap>();
            _currentCanvas = GetComponentInParent<Canvas>();

            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_activeConnection)
                return;

            if (_currentCanvas)
                Root.anchoredPosition = _mouseInput.GetMouseScreenPosition() / _currentCanvas.scaleFactor;
            else
                Root.anchoredPosition = _mouseInput.GetMouseScreenPosition();

            if (!_mouseInput.TryGetMouseGridPosition(out Vector2Int currentMousePoint))
            {
                _activeMousePoint = new Vector2Int(int.MinValue, int.MinValue);
                Visual.SetActive(false);
                return;
            }

            if (currentMousePoint == _activeMousePoint)
                return;
            _activeMousePoint = currentMousePoint;

            if (!_connectionManager.HasPoint(_activeConnection, _activeMousePoint))
            {
                Visual.SetActive(false);
                return;
            }

            Visual.SetActive(true);
            if(ValueText)
                ValueText.text=_connectionManager.GetValue(_activeConnection, _activeMousePoint).ToString();
        }

        public void Activate(Connection connection)
        {
            _activeConnection = connection;
            _activeMousePoint = new Vector2Int(int.MaxValue, int.MaxValue);
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            _activeConnection = null;
            _activeMousePoint = new Vector2Int(int.MaxValue, int.MaxValue);
            gameObject.SetActive(false);
        }
    }
}
