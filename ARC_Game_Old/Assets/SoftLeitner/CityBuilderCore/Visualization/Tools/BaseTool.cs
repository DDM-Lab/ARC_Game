using System;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for tools that are managed by <see cref="ToolsManager"/><br/>
    /// gets activated, deactivated, has a cost, ...<br/>
    /// in general tools are buttons in the UI that can be clicked to interact with the game world using the pointer<br/>
    /// for exmaple placing or demolishing buildings and structures
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_base_tool.html")]
    public abstract class BaseTool : TooltipOwnerBase
    {
        [Tooltip("gets de/activated along with the tool")]
        public View View;

        /// <summary>
        /// whether the <see cref="IGridOverlay"/> gets shown for this tool
        /// </summary>
        public virtual bool ShowGrid => true;
        /// <summary>
        /// whether the camera can be panned by touch drag
        /// </summary>
        public virtual bool IsTouchPanAllowed => false;

        [Tooltip("fired whenever the tool becomes active")]
        public ToolEvent Activating;
        [Tooltip("fired when the tool is applied, for example when clicking to build something")]
        public ToolEvent Applied;

        public bool IsToolActive { get; private set; }
        public bool IsTouchActivated { get; private set; }

        private bool _hasActivatedView;

        private void Update()
        {
            if (IsToolActive)
                updateTool();
        }

        public virtual void ActivateTool()
        {
            Activating?.Invoke(this);

            IsTouchActivated = Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Ended;
            IsToolActive = true;

            if (ShowGrid)
                Dependencies.GetOptional<IGridOverlay>()?.Show();

            if (View && !Dependencies.Get<IViewsManager>().HasActiveView)
            {
                _hasActivatedView = true;
                Dependencies.Get<IViewsManager>().ActivateView(View);
            }
            else
            {
                _hasActivatedView = false;
            }
        }

        public virtual void DeactivateTool()
        {
            IsToolActive = false;
            if (ShowGrid)
                Dependencies.GetOptional<IGridOverlay>()?.Hide();

            if (_hasActivatedView && Dependencies.Get<IViewsManager>().ActiveView == View)
            {
                Dependencies.Get<IViewsManager>().ActivateView(null);
            }
        }

        public virtual int GetCost(Item item) => 0;

        protected void onApplied()
        {
            Applied?.Invoke(this);
        }

        protected virtual void updateTool()
        {
        }
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ToolEvent : UnityEvent<BaseTool> { }
}