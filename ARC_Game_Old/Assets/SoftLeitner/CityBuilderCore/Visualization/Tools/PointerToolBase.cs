using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// special base class for tools that use the pointer<br/>
    /// handles differences between mouse/touch<br/>
    /// manages mouse down/up for dragging
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_pointer_tool_base.html")]
    public abstract class PointerToolBase : BaseTool
    {
        [Tooltip("when true touch dragging with this tool will pan the camera instead")]
        public bool AllowTouchPan;

        private bool _wasPressed;
        private bool _isDown;
        private bool _isTouchPanning;
        private Vector2Int _dragStart;
        private IMouseInput _mouseInput;

        public override bool IsTouchPanAllowed => _isTouchPanning;

        protected virtual void Start()
        {
            _mouseInput = Dependencies.Get<IMouseInput>();
        }

        public override void DeactivateTool()
        {
            base.DeactivateTool();

            _wasPressed = false;
            _isDown = false;
            _isTouchPanning = false;
        }

        protected override void updateTool()
        {
            base.updateTool();

            var mousePoint = _mouseInput.GetMouseGridPosition(IsTouchActivated);

            var isPressed = Input.GetMouseButtonDown(0);
            var isReleased = Input.GetMouseButtonUp(0);

            var isCancel = Input.GetMouseButtonDown(1) || Input.touchCount > 1;

            if (InputHelper.IsPointerOut())
            {
                if (isReleased)
                {
                    _wasPressed = false;
                    _isDown = false;

                    updatePointer(mousePoint, _dragStart, false, false);
                }
            }
            else
            {
                if (isCancel)
                {
                    _wasPressed = false;
                    _isDown = false;
                    _isTouchPanning = false;
                }

                if (isPressed)
                {
                    _isDown = true;
                    _dragStart = mousePoint;
                }

                if (AllowTouchPan)
                {
                    if (Input.touchCount > 0)
                    {
                        if (_isDown && _dragStart != mousePoint)
                        {
                            _wasPressed = false;
                            _isDown = false;
                            _isTouchPanning = true;
                        }
                        else if (Input.touchCount > 1)
                        {
                            _isTouchPanning = true;
                        }
                    }

                    if (isReleased && _isTouchPanning)
                    {
                        _isTouchPanning = false;
                    }
                }

                bool isApply = _wasPressed && isReleased;

                updatePointer(mousePoint, _dragStart, _wasPressed && _isDown, isApply);

                if (isApply)
                {
                    _wasPressed = false;
                    _isDown = false;
                }

                if (isPressed)
                    _wasPressed = true;
            }
        }

        protected abstract void updatePointer(Vector2Int mousePoint, Vector2Int dragStart, bool isDown, bool isApply);
    }
}
