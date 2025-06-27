using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for behaviours that save data without being otherwise known to the rest of the system<br/>
    /// <see cref="DefaultGameManager"/> finds all instances and saves them with the specified key
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_extra_data_behaviour.html")]
    public abstract class ExtraDataBehaviour : KeyedBehaviour, ISaveData
    {
        public abstract void LoadData(string json);
        public abstract string SaveData();
    }
}