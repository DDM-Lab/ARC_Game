using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of buildings<br/>
    /// a set of all buildings in the game is needed by <see cref="ObjectRepository"/> so buildings can be found when a game gets loaded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_mission_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(MissionSet))]
    public class MissionSet : KeyedSet<Mission> { }
}