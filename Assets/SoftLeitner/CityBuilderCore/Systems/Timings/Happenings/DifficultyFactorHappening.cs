using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, while active, augments the game difficulty
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_difficulty_factor_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(DifficultyFactorHappening))]
    public class DifficultyFactorHappening : TimingHappening, IDifficultyFactor
    {
        [Tooltip("multiplies how fast risks increase while the happening is active")]
        public float RiskMultiplier = 1f;
        [Tooltip("multiplies how fast service decrease while the happening is active")]
        public float ServiceMultiplier = 1f;
        [Tooltip("multiplies how items are used up while the happening is active")]
        public float ItemsMultiplier = 1f;

        float IDifficultyFactor.RiskMultiplier => RiskMultiplier;
        float IDifficultyFactor.ServiceMultiplier => ServiceMultiplier;
        float IDifficultyFactor.ItemsMultiplier => ItemsMultiplier;

        public override void Activate()
        {
            base.Activate();

            Dependencies.Get<IGameSettings>().RegisterDifficultyFactor(this);
        }
        public override void Deactivate()
        {
            base.Deactivate();

            Dependencies.Get<IGameSettings>().DeregisterDifficultyFactor(this);
        }
    }
}
