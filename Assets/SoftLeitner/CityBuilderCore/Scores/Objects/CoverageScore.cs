using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// percentage of how much of the population is covered by certain buildings
    /// example: 1 temple covers 1x100 | 2 shrines cover 2x50 | population=400 => 50% coverage
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_coverage_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(CoverageScore))]
    public class CoverageScore : Score
    {
        [Tooltip(@"give building types a score that will be added up for buildings on the map and divided by the population count
ie Shrine=20 Temple=100 >> 3xshrine+1xtemple=160/40xvillages=4 religion coverage")]
        public BuildingEvaluation[] Evaluations;
        [Tooltip("the populations that will be summed up")]
        public Population[] Populations;

        public override int Calculate()
        {
            int valueTotal = Evaluations.Sum(i => i.GetEvaluation());
            int populationTotal = Populations.Sum(p => Dependencies.Get<IPopulationManager>().GetQuantity(p));

            if (populationTotal == 0)
                return 0;

            return Math.Min(100, valueTotal * 100 / populationTotal);
        }
    }
}