using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// averages the service values accross buildings on the map<br/>
    /// for example the WaterScore in THREE averages the water service value for all housing buildings so it can be displayed as a bar
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_average_service_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(AverageServiceScore))]
    public class AverageServiceScore : Score
    {
        [Tooltip("the risk values of all buildings in this category will be used(leave empty for all buildings)")]
        public BuildingCategory BuildingCategory;
        [Tooltip("the value of this service will be added up and averaged between the buildings")]
        public Service Service;
        [Tooltip("can be used intead of Service when calculating for an entire category services")]
        public ServiceCategory ServiceCategory;

        public override int Calculate()
        {
            IEnumerable<IBuilding> buildings;

            if (BuildingCategory)
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings(BuildingCategory);
            else
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings();

            if (Service)
            {
                return Mathf.RoundToInt(buildings
                    .SelectMany(b => b.GetBuildingComponents<IServiceRecipient>())
                    .Where(s => s.HasServiceValue(Service))
                    .Select(s => s.GetServiceValue(Service))
                    .DefaultIfEmpty()
                    .Average());
            }
            else if (ServiceCategory)
            {
                return Mathf.RoundToInt(buildings
                    .Where(b => ServiceCategory.HasValue(b))
                    .Select(b => ServiceCategory.GetValue(b))
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