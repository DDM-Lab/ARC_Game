using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// simple <see cref="IToolsManager"/> implementation that should suffice for most cases
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_tools_manager.html")]
    public class ToolsManager : MonoBehaviour, IToolsManager
    {
        [Tooltip("tool that is activated when the active one gets deactivated(most likely the selection tool)")]
        public BaseTool FallbackTool;
        [Tooltip("toggle group that contains all the tools, used to deactivate all tools on RMB")]
        public ToggleGroup ToggleGroup;
        [Tooltip("fired when the active tool changes")]
        public UnityEvent<BaseTool> ToolChanged;

        public BaseTool ActiveTool => _activeTool;

        private BaseTool _activeTool;
        private IHighlightManager _highlighting;
        private float _mouseDown;

        protected virtual void Awake()
        {
            Dependencies.Register<IToolsManager>(this);
        }

        private void Start()
        {
            _highlighting = Dependencies.Get<IHighlightManager>();

            if (!_activeTool && FallbackTool)
                activateTool(FallbackTool);
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(1))
                _mouseDown = Time.unscaledTime;

            if (Input.GetMouseButtonUp(1) && (Time.unscaledTime - _mouseDown) < 0.2f && _activeTool && _activeTool != FallbackTool && !_activeTool.IsTouchActivated)
            {
                if (ToggleGroup)
                    ToggleGroup.SetAllTogglesOff(false);
                DeactivateTool(_activeTool);
            }
        }

        public void ActivateTool(BaseTool tool)
        {
            activateTool(tool);
            ToolChanged?.Invoke(_activeTool);
        }

        public void DeactivateTool(BaseTool tool)
        {
            if (_activeTool != tool)
                return;

            activateTool(null);
            ToolChanged?.Invoke(_activeTool);
        }

        public int GetCost(Item item) => _activeTool ? _activeTool.GetCost(item) : 0;

        private void activateTool(BaseTool tool)
        {
            if (_activeTool)
                _activeTool.DeactivateTool();
            _highlighting?.Clear();
            _activeTool = tool ? tool : FallbackTool;
            if (_activeTool)
                _activeTool.ActivateTool();
        }
    }
}