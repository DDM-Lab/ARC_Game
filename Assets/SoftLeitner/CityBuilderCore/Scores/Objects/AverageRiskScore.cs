using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// averages the risk values accross all building in a category<br/>
    /// for example the DiseaseScore in THREE averages the disease risk value for all housing buildings so it can be displayed as a bar in the UI
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_average_risk_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(AverageRiskScore))]
    public class AverageRiskScore : Score
    {
        [Tooltip("the risk values of all buildings in this category will be used(leave empty for all buildings)")]
        public BuildingCategory BuildingCategory;
        [Tooltip("the value of this risk will be added up and averages between the buildings")]
        public Risk Risk;
        [Tooltip("can be used intead of Risk when calculating for an entire category of risks")]
        public RiskCategory RiskCategory;

        public override int Calculate()
        {
            IEnumerable<IBuilding> buildings;

            if (BuildingCategory)
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings(BuildingCategory);
            else
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings();

            if (Risk)
            {
                return Mathf.RoundToInt(buildings
                    .SelectMany(b => b.GetBuildingComponents<IRiskRecipient>())
                    .Where(r => r.HasRiskValue(Risk))
                    .Select(r => 100f - r.GetRiskValue(Risk))
                    .DefaultIfEmpty()
                    .Average());
            }
            else if (RiskCategory)
            {
                return Mathf.RoundToInt(buildings
                    .Where(b => RiskCategory.HasValue(b))
                    .Select(b => 100f - RiskCategory.GetValue(b))
                    .DefaultIfEmpty()
                    .Average());
            }
            else
            {
                return 0;
            }
        }
    }
}