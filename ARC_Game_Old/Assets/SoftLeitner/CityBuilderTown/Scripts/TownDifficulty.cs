using CityBuilderCore;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// inherited version of <see cref="Difficulty"/> with additional fields specific to the Town demo
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_difficulty.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Town/" + nameof(TownDifficulty))]
    public class TownDifficulty : Difficulty
    {
        [Tooltip("modifies how fast food is lost over time")]
        public float FoodModifier = 1f;
        [Tooltip("modifies how fast warmth is lost over time")]
        public float WarmModifier = 1f;
        [Tooltip("modifies how cold it is(affects warmth loss and wood usage)")]
        public float ColdModifier = 1f;
        [Tooltip("modifies how fast walkers age")]
        public float AgeModifier = 1f;
        [Tooltip("modifies how many trees and bushes grow at the start of summer")]
        public float GrowthModifier = 1f;
    }
}