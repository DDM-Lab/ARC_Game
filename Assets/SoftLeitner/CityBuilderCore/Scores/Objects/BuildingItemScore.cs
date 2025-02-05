using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// item quantity in building storage
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_item_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(BuildingItemScore))]
    public class BuildingItemScore : Score
    {
        public enum BuildingItemCalculation
        {
            /// <summary>
            /// total sum of items owned across buildings
            /// </summary>
            Total,
            /// <summary>
            /// average of items owned per building
            /// </summary>
            Average,
            /// <summary>
            /// how many items are stored relative to the capacity
            /// </summary>
            Relative
        }

        [Tooltip(@"Total - total sum of items owned across buildings
Average - average of items owned per building
Relative - how many items are stored relative to the capacity")]
        public BuildingItemCalculation Mode;

        [Tooltip("the building to check, use BuildingCategory instead to check multiple or leave empty to check all")]
        public BuildingInfo Building;
        [Tooltip("the building category to check, use Building instead to check only one type or leave empty to check all")]
        public BuildingCategory BuildingCategory;

        [Tooltip("the item to count, use ItemCategory instead to count multiple")]
        public Item Item;
        [Tooltip("the item category to count, use Item instead to only count a single item type")]
        public ItemCategory ItemCategory;

        public override int Calculate()
        {
            IEnumerable<IBuilding> buildings;

            if (Building)
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings(Building);
            else if (BuildingCategory)
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings(BuildingCategory);
            else
                buildings = Dependencies.Get<IBuildingManager>().GetBuildings();

            var quantity = 0;
            var capacity = 0;
            var count = 0;

            foreach (var building in buildings)
            {
                count++;

                foreach (var owner in building.GetBuildingParts<IItemOwner>())
                {
                    if (Item)
                    {
                        quantity += owner.ItemContainer.GetItemQuantity(Item);

                        if (Mode == BuildingItemCalculation.Relative)
                            capacity += owner.ItemContainer.GetItemCapacity(Item);
                    }
                    else if (ItemCategory)
                    {
                        quantity += owner.ItemContainer.GetItemQuantity(ItemCategory);

                        if (Mode == BuildingItemCalculation.Relative)
                            capacity += owner.ItemContainer.GetItemCapacity(ItemCategory);
                    }
                }
            }

            switch (Mode)
            {
                case BuildingItemCalculation.Total:
                    return quantity;
                case BuildingItemCalculation.Average:
                    if (count == 0)
                        return 0;

                    return quantity / count;
                case BuildingItemCalculation.Relative:
                    if (quantity == 0)
                        return 0;

                    return capacity / quantity * 100;
                default:
                    return 0;
            }
        }
    }
}