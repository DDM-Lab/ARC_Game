using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// non generic base class so <see cref="ViewBuildingBar{T}"/> can be accessed in <see cref="IBarManager"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/views">https://citybuilder.softleitner.com/manual/views</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_view_building_bar_base.html")]
    public abstract class ViewBuildingBarBase : View
    {
        [Tooltip("prefab of the bar that will be instantiated on buildings")]
        public BuildingValueBar Bar;

        public abstract IBuildingValue BuildingValue { get; }

        public override void Activate() => Dependencies.Get<IBarManager>().ActivateBar(this);
        public override void Deactivate() => Dependencies.Get<IBarManager>().DeactivateBar(this);
    }
}