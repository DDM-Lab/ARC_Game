using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// kills a part of the population on start(relative to the capacity of the housing)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_depopulation_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(DepopulationHappening))]
    public class DepopulationHappening : TimingHappening
    {
        [Tooltip("ratio of the population to kill")]
        [Range(0, 1)]
        public float Mortality;

        public override void Start()
        {
            base.Start();

            Dependencies.Get<IPopulationManager>().GetHousings().ForEach(h => h.Kill(Mortality));
        }
    }
}
