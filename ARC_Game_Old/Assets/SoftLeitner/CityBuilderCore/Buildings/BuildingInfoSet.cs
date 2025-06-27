using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of buildings<br/>
    /// a set of all buildings in the game is needed by <see cref="ObjectRepository"/> so buildings can be found when a game gets loaded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_info_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(BuildingInfoSet))]
    public class BuildingInfoSet : KeyedSet<BuildingInfo> { }
}