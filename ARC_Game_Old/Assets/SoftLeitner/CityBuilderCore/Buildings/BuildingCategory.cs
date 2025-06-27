using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// category for bundling and filtering buildings(entertainment, religion, ....), mainly used in scores
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_category.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(BuildingCategory))]
    public class BuildingCategory : KeyedObject
    {
        [Tooltip("name used when refering to a singular building of the category('you need to build a X')")]
        public string NameSingular;
        [Tooltip("name used when refering to multiple buildings of the category('you need to build 10 Y')")]
        public string NamePlural;
        [Tooltip("collection of all the buildings in the category")]
        public BuildingInfo[] Buildings;

        private HashSet<BuildingInfo> _buildings;

        public bool Contains(BuildingInfo building)
        {
            if (_buildings == null)
                _buildings = new HashSet<BuildingInfo>(Buildings);
            return _buildings.Contains(building);
        }

        public string GetName(int quantity)
        {
            if (quantity > 1)
                return $"{quantity} {NamePlural}";
            else
                return NameSingular;
        }
    }
}