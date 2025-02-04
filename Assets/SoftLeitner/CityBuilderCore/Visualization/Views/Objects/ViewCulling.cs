using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// view that changes camera culling<br/>
    /// eg hiding irrelevant buildings 
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/views">https://citybuilder.softleitner.com/manual/views</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_view_culling.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Views/" + nameof(ViewCulling))]
    public class ViewCulling : View
    {
        [Tooltip("the culling that will be set on the main camera when this view is active")]
        public LayerMask Culling;

        public override void Activate() => Dependencies.Get<IMainCamera>().SetCulling(Culling);
        public override void Deactivate() => Dependencies.Get<IMainCamera>().ResetCulling();
    }
}