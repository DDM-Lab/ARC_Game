using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// view that displays an overlay for efficiency on a tilemap
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/views">https://citybuilder.softleitner.com/manual/views</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_view_efficiency.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Views/" + nameof(ViewEfficiency))]
    public class ViewEfficiency : View
    {
        [Tooltip("gradient for building efficiency from 0 to 1 that will be displayed as an overlay")]
        public Gradient Gradient;

        public override void Activate() => Dependencies.Get<IOverlayManager>().ActivateOverlay(this);
        public override void Deactivate() => Dependencies.Get<IOverlayManager>().ClearOverlay();
    }
}
