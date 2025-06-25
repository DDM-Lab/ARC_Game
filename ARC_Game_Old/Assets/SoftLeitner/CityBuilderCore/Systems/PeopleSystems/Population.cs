using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// type of population(plebs, middle class, snobs, ...)<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_population.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(Population))]
    public class Population : KeyedObject
    {
        [Tooltip("name of the population for use in the UI")]
        public string Name;
    }
}