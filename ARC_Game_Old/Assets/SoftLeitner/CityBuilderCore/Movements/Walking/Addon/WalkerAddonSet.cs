using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// a collection of walker addons<br/>
    /// if any walker addons are saved(<see cref="WalkerAddon.Save"/>) a set of all walker addons in the game is needed by <see cref="ObjectRepository"/> so addons can be found when a game gets loaded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_addon_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(WalkerAddonSet))]
    public class WalkerAddonSet : KeyedSet<WalkerAddon> { }
}