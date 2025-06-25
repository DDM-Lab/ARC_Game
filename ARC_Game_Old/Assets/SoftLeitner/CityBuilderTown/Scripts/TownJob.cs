using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// some tasks can only be done by walkers with a specific job<br/>
    /// for example building a house or farming
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_town_job.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Town/" + nameof(TownJob))]
    public class TownJob : KeyedObject
    {
        [Tooltip("name of the job, may be used in ui and debug")]
        public string Name;
        [Tooltip("description of the job, may be used in ui and debug")]
        [TextArea]
        public string Description;
        [Tooltip("icon of the job, may be used in ui and debug")]
        public Sprite Icon;
        [Tooltip("hat that is instantiated on walkers that have this job assigned(TownWalker.HatParent)")]
        public GameObject Hat;
    }
}