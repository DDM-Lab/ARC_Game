using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of walkers<br/>
    /// a set of all walkers in the game is needed by <see cref="ObjectRepository"/> so walkers can be found when a game gets loaded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_info_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(WalkerInfoSet))]
    public class WalkerInfoSet : ObjectSet<WalkerInfo> { }
}