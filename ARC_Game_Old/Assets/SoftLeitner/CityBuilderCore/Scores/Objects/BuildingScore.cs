using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// counts how many buildings currently exist on the map
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(BuildingScore))]
    public class BuildingScore : Score
    {
        [Tooltip("the building to count")]
        public BuildingInfo Building;
        [Tooltip("the building category to count(dont set building)")]
        public BuildingCategory BuildingCategory;

        public override int Calculate()
        {
            if (Building)
                return Dependencies.Get<IBuildingManager>().Count(Building);
            else if (BuildingCategory)
                return Dependencies.Get<IBuildingManager>().Count(BuildingCategory);
            else
                return 0;
        }
    }
}