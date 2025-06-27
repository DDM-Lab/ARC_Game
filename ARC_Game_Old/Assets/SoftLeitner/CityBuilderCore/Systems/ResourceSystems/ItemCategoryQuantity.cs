using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// convenience container that combines an ItemCategory with a quantity
    /// </summary>
    [Serializable]
    public class ItemCategoryQuantity
    {
        [Tooltip("what is it(wood, potato, ...)")]
        public ItemCategory ItemCategory;
        [Tooltip("how many are there(5, 10, ...)")]
        public int Quantity;

        public ItemCategoryQuantity()
        {

        }

        public ItemCategoryQuantity(ItemCategory itemCategory, int quantity)
        {
            ItemCategory = itemCategory;
            Quantity = quantity;
        }

        public int Remove(int max)
        {
            var count = Mathf.Min(Quantity, max);

            Quantity -= count;

            return count;
        }

        public override string ToString()
        {
            if (ItemCategory)
                return ItemCategory.GetName(Quantity);

            return base.ToString();
        }
    }
}