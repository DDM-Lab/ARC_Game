using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some collection of roads, a set of all roads should be set in <see cref="ObjectRepository"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_road_set.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Sets/" + nameof(RoadSet))]
    public class RoadSet : KeyedSet<Road> { }
}