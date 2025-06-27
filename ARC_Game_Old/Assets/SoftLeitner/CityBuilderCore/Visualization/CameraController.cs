using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// camera controller that works with various setups and provides mouse inpurt and main camera interfaces
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_camera_controller.html")]
    public class CameraController : MonoBehaviour, IMouseInput, IMainCamera
    {
        [Header("General")]
        [Tooltip("camera controlled by this controller, will be treated as the main camera")]
        public Camera Camera;
        [Tooltip("transform that is moved and rotated by this controler, counts as the camera position")]
        public Transform Pivot;
        [Header("Speed")]
        [Tooltip("how fast the controler translates")]
        public float Speed = 5;
        [Tooltip("how fast the controler rotates around up")]
        public float RotateSpeed = 50;
        [Tooltip("how fast the controler rotates the pitch(up/down)")]
        public float PitchSpeed = 50;
        [Tooltip("how fast the controler zooms in and out")]
        public float ZoomSpeed = 10;
        [Header("Settings")]
        [Tooltip("smallest possible zoom value")]
        public float MinZoom = 2;
        [Tooltip("largest possible zoom value")]
        public float MaxZoom = 15;
        [Tooltip("can be used to override the cameras transparency sort axis in 2d game(in defense demo for example)")]
        public Vector3 SortAxis;
        [Header("Colliders")]
        [Tooltip("optional, used when calculating where the mouse is on the grid")]
        public Collider MouseCollider;
        [Tooltip("optional, pushes the camera zoom out when the camera would clip otherwise")]
        public Collider ZoomCollider;
        [Tooltip("when a zoom collider is used this determines how far back the camera is pushed")]
        public float ZoomColliderDistance = 1f;
        [Header("TOUCH")]
        [Tooltip("whether the controler should pan when two fingers are on the screen")]
        public bool TwoFingerPan = true;
        [Tooltip("how fast the controler zooms in and out when pinching")]
        public float PinchSpeed = 0.005f;
        [Tooltip("offset between touch position and cursor positon, can be used so that the finger does not cover the thing it interacts with")]
        public Vector2 TouchOffset = Vector3.zero;

        private Camera _camera;
        private Transform _pivot;
        private int _defaultCulling;
        private Vector2? _previousMouseScreenPositionPrimary;
        private Vector2? _previousMouseScreenPositionSecondary;
        private IMap _map;
        private IToolsManager _toolsManager;
        private CoroutineToken _jumpRoutine;
        private CoroutineToken _followRoutine;
        private bool _isPinching;
        private float _pinchDistance;

        public Vector3 Position { get => _pivot.position; set => _pivot.position = value; }
        public Quaternion Rotation { get => _pivot.localRotation; set => _pivot.localRotation = value; }
        public float Size
        {
            get
            {
                if (_camera.orthographic)
                    return _camera.orthographicSize;
                else
                    return -_camera.transform.localPosition.z;
            }
            set
            {
                if (_camera.orthographic)
                    _camera.orthographicSize = value;
                else
                    _camera.transform.localPosition = new Vector3(_camera.transform.localPosition.x, _camera.transform.localPosition.y, -value);
            }
        }

        Camera IMainCamera.Camera => _camera;

        protected virtual void Awake()
        {
            Dependencies.Register<IMouseInput>(this);
            Dependencies.Register<IMainCamera>(this);
        }

        protected virtual void Start()
        {
            _map = Dependencies.Get<IMap>();
            _toolsManager = Dependencies.Get<IToolsManager>();

            if (Camera)
                _camera = Camera;
            else
                _camera = GetComponent<Camera>();

            if (Pivot)
                _pivot = Pivot;
            else
                _pivot = transform;

            _defaultCulling = _camera.cullingMask;

            if (SortAxis.sqrMagnitude > 0)
            {
                _camera.transparencySortMode = TransparencySortMode.CustomAxis;
                _camera.transparencySortAxis = SortAxis;
            }
        }

        private void Update()
        {
            if (InputHelper.IsPointerOverUIObject())
                return;
            if (_jumpRoutine != null)
                return;

            var h = Input.GetAxis("Horizontal");
            var v = Input.GetAxis("Vertical");

            if ((h > 0 || v > 0) && _followRoutine?.IsActive == true)
            {
                _followRoutine.Stop();
                _followRoutine = null;
            }

            Vector3 position = _pivot.position;
            var deltaTime = Time.unscaledDeltaTime;

            position += _pivot.right * h * Speed * Size * deltaTime;
            position += Vector3.Cross(_pivot.right, _map.IsXY ? Vector3.back : Vector3.up) * v * Speed * Size * deltaTime;

            position = _map.ClampPosition(position);

            _pivot.position = position;

            if (Input.GetKey(KeyCode.Q))
                rotate(deltaTime);
            else if (Input.GetKey(KeyCode.E))
                rotate(-deltaTime);

            if (InputHelper.IsPointerOutsideScreen())
                return;

            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (Input.touchCount >= 2)
            {
                Vector2 touch0, touch1;
                float distance;
                touch0 = Input.GetTouch(0).position;
                touch1 = Input.GetTouch(1).position;
                distance = Vector2.Distance(touch0, touch1);

                if (_isPinching)
                {
                    scroll = _pinchDistance - distance;
                }

                _isPinching = true;
                _pinchDistance = distance;
            }
            else
            {
                _isPinching = false;
            }

            if (Input.GetKey(KeyCode.LeftShift) && PitchSpeed != 0)
            {
                if (Mathf.Abs(scroll) > 0)
                    pitch(scroll);
            }
            else
            {
                zoom(scroll, _isPinching);
            }
        }

        private void LateUpdate()
        {
            switch (getPrimaryPhase())
            {
                case TouchPhase.Began:
                    if (_followRoutine?.IsActive == true)
                    {
                        _followRoutine.Stop();
                        _followRoutine = null;
                    }

                    _previousMouseScreenPositionPrimary = GetMouseScreenPosition();
                    break;
                case TouchPhase.Moved:
                    if (_previousMouseScreenPositionPrimary.HasValue)
                    {
                        var screenPosition = GetMouseScreenPosition();

                        if (InputHelper.GetShift())
                        {
                            rotate((screenPosition.x - _previousMouseScreenPositionPrimary.Value.x) / 500f);
                            pitch((screenPosition.y - _previousMouseScreenPositionPrimary.Value.y) / 500f);
                        }
                        else if (InputHelper.GetAlt())
                        {
                            zoom((screenPosition.y - _previousMouseScreenPositionPrimary.Value.y) / 500f);
                        }
                        else
                        {
                            var position = GetMousePosition(screenPosition);
                            var positionPrevious = GetMousePosition(_previousMouseScreenPositionPrimary.Value);

                            _pivot.Translate(positionPrevious - position, Space.World);
                            _pivot.position = _map.ClampPosition(_pivot.position);
                        }

                        _previousMouseScreenPositionPrimary = screenPosition;
                    }
                    else
                    {
                        _previousMouseScreenPositionPrimary = GetMouseScreenPosition();
                    }
                    break;
                case TouchPhase.Ended:
                    _previousMouseScreenPositionPrimary = null;
                    break;
            }

            if (_previousMouseScreenPositionPrimary == null)
            {
                switch (getSecondaryPhase())
                {
                    case TouchPhase.Began:
                        _previousMouseScreenPositionSecondary = GetMouseScreenPosition();
                        break;
                    case TouchPhase.Moved:
                        if (_previousMouseScreenPositionSecondary.HasValue)
                        {
                            var screenPosition = GetMouseScreenPosition();

                            if (InputHelper.GetShift())
                            {
                                zoom((screenPosition.y - _previousMouseScreenPositionSecondary.Value.y) / 500f);
                            }
                            else if (InputHelper.GetAlt())
                            {
                                var position = GetMousePosition(screenPosition);
                                var positionPrevious = GetMousePosition(_previousMouseScreenPositionSecondary.Value);

                                _pivot.Translate(positionPrevious - position, Space.World);
                                _pivot.position = _map.ClampPosition(_pivot.position);
                            }
                            else
                            {
                                rotate((screenPosition.x - _previousMouseScreenPositionSecondary.Value.x) / 500f);
                                pitch((screenPosition.y - _previousMouseScreenPositionSecondary.Value.y) / 500f);
                            }

                            _previousMouseScreenPositionSecondary = screenPosition;
                        }
                        else
                        {
                            _previousMouseScreenPositionSecondary = GetMouseScreenPosition();
                        }
                        break;
                    case TouchPhase.Ended:
                        _previousMouseScreenPositionSecondary = null;
                        break;
                }
            }

            if (ZoomCollider && !Camera.orthographic)
            {
                if (ZoomCollider.Raycast(new Ray(_camera.transform.position + _camera.transform.forward * -1000, _camera.transform.forward), out RaycastHit hit, 1000 + ZoomColliderDistance))
                {
                    Size += 1000 - hit.distance + ZoomColliderDistance;
                }
            }
        }

        public Ray GetRay(bool applyOffset = false)
        {
            var mousePosition = GetMouseScreenPosition(applyOffset);
            if (mousePosition.IsInvalid())
                return new Ray(Vector3.positiveInfinity, Vector3.positiveInfinity);
            return _camera.ScreenPointToRay(mousePosition);
        }

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

        public void SetCulling(LayerMask layerMask) => _camera.cullingMask = layerMask;
        public void ResetCulling() => _camera.cullingMask = _defaultCulling;

        public CoroutineToken Jump(Vector3 position, Quaternion? rotation = null)
        {
            _jumpRoutine?.Stop();
            _followRoutine?.Stop();

            _jumpRoutine = CoroutineToken.Start(jump(position, rotation), this);
            _followRoutine = null;

            return _jumpRoutine;
        }
        public CoroutineToken Follow(Transform leader)
        {
            _jumpRoutine?.Stop();
            _followRoutine?.Stop();

            _jumpRoutine = null;
            _followRoutine = CoroutineToken.Start(follow(leader), this);

            return _followRoutine;
        }

        public Ray GetRay(Vector2 screenPosition) => _camera.ScreenPointToRay(screenPosition);
        public Vector3 GetMousePosition(Vector2 screenPosition)
        {
            var plane = new Plane(_map.IsXY ? Vector3.forward : Vector3.up, Vector3.zero);
            var ray = GetRay(screenPosition);

            plane.Raycast(ray, out float distance);
            return ray.GetPoint(distance);
        }

        private void rotate(float value)
        {
            if (RotateSpeed == 0)
                return;
            _pivot.Rotate(_map.IsXY ? Vector3.back : Vector3.up, value * RotateSpeed, Space.World);
        }

        private void pitch(float value)
        {
            if (PitchSpeed == 0)
                return;
            var rotation = _pivot.localRotation.eulerAngles;
            rotation = new Vector3(Mathf.Clamp(rotation.x + -value * PitchSpeed, 5f, 89.9f), rotation.y, rotation.z);
            _pivot.localRotation = Quaternion.Euler(rotation);
        }

        private void zoom(float value, bool isPinching = false)
        {
            if (value == 0)
                return;

            float size = Size;
            if (isPinching)
            {
                if (PinchSpeed == 0)
                    return;

                size += value * PinchSpeed * Size;
            }
            else
            {
                if (ZoomSpeed == 0)
                    return;

                size -= value * ZoomSpeed;
            }
            Size = Mathf.Clamp(size, MinZoom, MaxZoom);
        }

        private TouchPhase getPrimaryPhase()
        {
            if (Input.GetMouseButtonDown(2))
                return TouchPhase.Began;
            if (Input.GetMouseButton(2))
                return TouchPhase.Moved;
            if (Input.GetMouseButtonUp(2))
                return TouchPhase.Ended;

            if (TwoFingerPan && Input.touchCount == 2)
                return TouchPhase.Moved;

            if (Input.touchCount != 1 || InputHelper.IsPointerOut())
                return TouchPhase.Ended;

            if (_toolsManager.ActiveTool == null || _toolsManager.ActiveTool.IsTouchPanAllowed)
            {
                return Input.GetTouch(0).phase;
            }
            else
            {
                return TouchPhase.Ended;
            }
        }

        private TouchPhase getSecondaryPhase()
        {
            if (Input.GetMouseButtonDown(1))
                return TouchPhase.Began;
            if (Input.GetMouseButtonUp(1))
                return TouchPhase.Ended;
            if (Input.GetMouseButton(1))
                return TouchPhase.Moved;
            return TouchPhase.Ended;
        }

        private IEnumerator jump(Vector3 position, Quaternion? rotation, float duration = 0.2f)
        {
            var time = 0f;

            var startPosition = Position;
            var startRotation = Rotation;

            while (time < duration)
            {
                yield return null;

                time += Time.unscaledDeltaTime;

                Position = Vector3.Lerp(startPosition, position, time / duration);
                if (rotation.HasValue)
                    Rotation = Quaternion.Lerp(startRotation, rotation.Value, time / duration);
            }

            Position = position;
            if (rotation.HasValue)
                Rotation = rotation.Value;

            _jumpRoutine = null;
        }
        private IEnumerator follow(Transform transform)
        {
            var time = 0f;

            var startPosition = Position;

            while (true)
            {
                yield return null;

                time += Time.unscaledDeltaTime;

                if (!transform || !transform.gameObject.activeInHierarchy)
                {
                    _followRoutine = null;
                    yield break;
                }

                if (time < 0.2f)
                    Position = Vector3.Lerp(startPosition, transform.position, time / 0.2f);
                else
                    Position = transform.position;
            }
        }
    }
}