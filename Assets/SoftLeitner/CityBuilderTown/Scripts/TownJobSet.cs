using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of jobs<br/>
    /// a set of all jobs in the game is needed by <see cref="TownManager"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_town_job_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Town/" + nameof(TownJobSet))]
    public class TownJobSet : KeyedSet<TownJob> { }
}
