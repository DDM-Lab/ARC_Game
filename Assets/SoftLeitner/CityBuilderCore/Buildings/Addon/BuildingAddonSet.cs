using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// collection of building addons. if any building addons are saved(<see cref="BuildingAddon.Save"/>)<br/>
    /// a set of all addons in the game is needed by <see cref="ObjectRepository"/> so they can be found when a game gets loaded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_addon_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(BuildingAddonSet))]
    public class BuildingAddonSet : KeyedSet<BuildingAddon> { }
}