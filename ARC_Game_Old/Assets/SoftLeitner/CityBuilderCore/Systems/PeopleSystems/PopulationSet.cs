using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of populations<br/>
    /// a set of all populations in the game is needed in <see cref="ObjectRepository"/> so populations can be found when a game gets loaded
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_population_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(PopulationSet))]
    public class PopulationSet : KeyedSet<Population> { }
}