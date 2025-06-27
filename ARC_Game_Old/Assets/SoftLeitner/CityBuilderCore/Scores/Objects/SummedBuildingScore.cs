using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// sums the values for different buildings<br/>
    /// for example monument scores are added together
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_summed_building_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(SummedBuildingScore))]
    public class SummedBuildingScore : Score
    {
        [Tooltip(@"give building types a score that will be added up for buildings on the map
ie monolith=40 pyramid=100 >> 3xmonolith+1xpyramid=220")]
        public BuildingEvaluation[] Evaluations;

        public override int Calculate()
        {
            return Evaluations.Sum(i => i.GetEvaluation());
        }
    }
}