using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// object that defines parameters that influence how hard a mission is<br/>
    /// for additional parameters just derive from this object
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_difficulty.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(Difficulty))]
    public class Difficulty : KeyedObject, IDifficultyFactor
    {
        [Tooltip("display name")]
        public string Name;

        [Tooltip("influences the speed at which risks increase")]
        public float RiskMultiplier;
        [Tooltip("influences the speed at which services deplete")]
        public float ServiceMultiplier;
        [Tooltip("influences the speed at which items deplete")]
        public float ItemsMultiplier;

        float IDifficultyFactor.RiskMultiplier => RiskMultiplier;
        float IDifficultyFactor.ServiceMultiplier => ServiceMultiplier;
        float IDifficultyFactor.ItemsMultiplier => ItemsMultiplier;
    }
}