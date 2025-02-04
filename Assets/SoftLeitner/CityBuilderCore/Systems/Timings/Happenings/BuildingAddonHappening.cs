using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, on activation, modifies the risk values of a set amount of buildings<br/>
    /// increase > arsonist, disease outbreak, natural disaster(volcano, earthquake) ...<br/>
    /// decrease > blessings, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_addon_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(BuildingAddonHappening))]
    public class BuildingAddonHappening : TimingHappening
    {
        [Tooltip("the addon that is added to buildings when the happening starts")]
        public BuildingAddon Addon;
        [Tooltip("the type of building that addons will be added to")]
        public BuildingInfo Building;
        [Tooltip("alternatively, the building category that addons will be added to")]
        public BuildingCategory BuildingCategory;
        [Tooltip("how many randomly selected building will be affected, 0 or less for all")]
        public int Count;
        [Tooltip("whether addons will be removed when the happening ends")]
        public bool Remove;

        public override void Start()
        {
            base.Start();

            foreach (var building in Dependencies.Get<IBuildingManager>().GetRandom(Count, b =>
            {
                if (b.HasBuildingAddon(Addon))
                    return false;
                if (Building && b.Info == Building)
                    return true;
                if (BuildingCategory && BuildingCategory.Contains(b.Info))
                    return true;
                return false;
            }))
            {
                building.AddAddon(Addon);
            }
        }

        public override void End()
        {
            base.End();

            if (Remove)
            {
                foreach (var addon in Dependencies.Get<IBuildingManager>().GetBuildingAddons(Addon).Random(Count).ToArray())
                {
                    addon.Remove();
                }
            }
        }
    }
}
