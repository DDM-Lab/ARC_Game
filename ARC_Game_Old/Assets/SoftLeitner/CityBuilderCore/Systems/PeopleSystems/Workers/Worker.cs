using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// a type of worker(mason, carpenter, laborer)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_worker.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(Worker))]
    public class Worker : ScriptableObject
    {
        [Tooltip("name of the worker for use in UI")]
        public string Name;
        [Tooltip("icon of the worker for use in UI")]
        public Sprite Icon;
    }
}