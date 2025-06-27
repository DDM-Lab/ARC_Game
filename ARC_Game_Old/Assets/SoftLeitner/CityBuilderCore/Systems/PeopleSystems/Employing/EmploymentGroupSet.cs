using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of employment groups<br/>
    /// a set of all groups in the game is needed in <see cref="ObjectRepository"/> for the population manager to find priorities
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_employment_group_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(EmploymentGroupSet))]
    public class EmploymentGroupSet : KeyedSet<EmploymentGroup> { }
}