using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// behaviour that can be uniquely identified by a string key<br/>
    /// it can then be retrieved from <see cref="KeyedSet{T}"/> by its key
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_keyed_behaviour.html")]
    public class KeyedBehaviour : MonoBehaviour, IKeyed
    {
        [Tooltip("unique identifier among a type of objects(might be used in savegames, be careful when changing)")]
        public string Key;

        string IKeyed.Key => Key;
    }
}