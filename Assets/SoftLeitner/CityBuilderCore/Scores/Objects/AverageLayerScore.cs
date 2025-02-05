using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// averages the layer values accross buildings on the map<br/>
    /// for example the DesirabilityScore in THREE averages the desirability layer value for all housing buildings
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_average_layer_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(AverageLayerScore))]
    public class AverageLayerScore : Score
    {
        [Tooltip("the layer values of all buildings in this category will be used(leave empty for all buildings)")]
        public BuildingCategory BuildingCategory;
        [Tooltip("the value of this layer will be asessed for the buildings and averaged")]
        public Layer Layer;

        public override int Calculate()
        {
            IEnumerable<IBuilding> buildings;

            if (BuildingCategory)
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings(BuildingCategory);
            else
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings();

            return Mathf.RoundToInt(buildings
                .Select(b => (float)Dependencies.Get<ILayerManager>().GetValue(b.Point, Layer))
                .DefaultIfEmpty()
                .Average());
        }
    }
}