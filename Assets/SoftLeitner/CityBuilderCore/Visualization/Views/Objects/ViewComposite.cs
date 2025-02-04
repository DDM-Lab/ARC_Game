using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// view that transfers activation to its children
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/views">https://citybuilder.softleitner.com/manual/views</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_view_composite.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Views/" + nameof(ViewComposite))]
    public class ViewComposite : View
    {
        [Tooltip("the sub-views that will be activated/deactivated with this view")]
        public View[] Views;

        public override void Activate() => Views.ForEach(v => v.Activate());
        public override void Deactivate() => Views.ForEach(v => v.Deactivate());
    }
}