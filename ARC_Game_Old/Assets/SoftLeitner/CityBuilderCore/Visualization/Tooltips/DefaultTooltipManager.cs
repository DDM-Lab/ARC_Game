using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// manages the tooltip, tooltips are only displayed after a delay and vanish when the mouse is moved<br/>
    /// the UI part of displaying the tooltip is done in <see cref="TooltipPanel"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_tooltip_manager.html")]
    public class DefaultTooltipManager : MonoBehaviour, ITooltipManager
    {
        [Tooltip("actual panel that displays the tooltip in the UI")]
        public TooltipPanel Panel;
        [Tooltip("how long the mouse has to remain in the same space until the tooltip is displayed")]
        public float Delay = 0.25f;

        private Vector3 _previousMousePosition;
        private ITooltipOwner _currentOwner;
        private ITooltipOwner _requestedOwner;
        private float _time;

        private void Awake()
        {
            Dependencies.Register<ITooltipManager>(this);
        }

        private void Start()
        {
            Panel.Hide();
        }

        private void Update()
        {
            var mousePosition = Input.mousePosition;

            if (_requestedOwner == null)
            {
                if (_currentOwner != null)
                {
                    Panel.Hide();
                    _time = 0f;
                }
            }
            else
            {
                if (_requestedOwner == _currentOwner)
                {
                    if (_previousMousePosition == mousePosition)
                    {
                        if (!Panel.IsVisible)
                        {
                            _time += Time.unscaledDeltaTime;
                            if (_time >= Delay)
                            {
                                Panel.Show(mousePosition, _requestedOwner);
                                _time = 0f;
                            }
                        }
                    }
                    else
                    {
                        Panel.Hide();
                        _time = 0f;
                    }
                }
                else
                {
                    Panel.Hide();
                    _time = 0f;
                }
            }

            _currentOwner = _requestedOwner;
            _previousMousePosition = Input.mousePosition;
        }

        public void Enter(ITooltipOwner owner)
        {
            _requestedOwner = owner;
        }

        public void Exit(ITooltipOwner owner)
        {
            if (_requestedOwner == owner)
                _requestedOwner = null;
        }
    }
}
