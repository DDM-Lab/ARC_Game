using UnityEngine;
using UnityEngine.EventSystems;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for ui behaviours that display tooltips
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_tooltip_owner_base.html")]
    public abstract class TooltipOwnerBase : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ITooltipOwner
    {
        public virtual string TooltipName => null;
        public virtual string TooltipDescription => null;

        protected bool _isPointerInside;

        protected virtual void OnDisable()
        {
            if (_isPointerInside)
                exit();
        }

        public void OnPointerEnter(PointerEventData eventData) => enter();
        public void OnPointerExit(PointerEventData eventData) => exit();

        protected void enter()
        {
            _isPointerInside = true;
            if (TooltipName == null)
                return;
            Dependencies.GetOptional<ITooltipManager>()?.Enter(this);
        }
        protected void exit()
        {
            _isPointerInside = false;
            if (TooltipName == null)
                return;
            Dependencies.GetOptional<ITooltipManager>()?.Exit(this);
        }
    }
}
