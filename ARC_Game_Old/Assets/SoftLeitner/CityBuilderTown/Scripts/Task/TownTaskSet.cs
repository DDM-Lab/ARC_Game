using CityBuilderCore;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// some collection of tasks<br/>
    /// a set of all taks in the game is needed by <see cref="TownManager"/> so tasks can be found when a game gets loaded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_task_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Town/" + nameof(TownTaskSet))]
    public class TownTaskSet : KeyedSet<TownTask> { }
}
