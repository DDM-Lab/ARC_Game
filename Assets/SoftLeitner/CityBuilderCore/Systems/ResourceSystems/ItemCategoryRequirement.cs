using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper used in <see cref="EvolutionStage"/> to set that a certain amount of an item category is needed to evolve<br/>
    /// for example housing might need 3 types of food and 2 types of luxury items to evolve
    /// </summary>
    [Serializable]
    public class ItemCategoryRequirement
    {
        [Tooltip("how many different items of the category are needed")]
        public int Quantity;
        [Tooltip("the category of items needed")]
        public ItemCategory ItemCategory;

        public bool IsFulfilled(Item[] items)
        {
            return items.Where(i => ItemCategory.Contains(i)).Count() >= Quantity;
        }
    }
}