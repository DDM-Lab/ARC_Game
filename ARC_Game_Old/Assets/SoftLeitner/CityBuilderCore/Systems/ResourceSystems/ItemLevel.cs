using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// convenience container that combines an item with a quantity and a capacity<br/>
    /// can be used to represent how filled something is with an item
    /// </summary>
    [Serializable]
    public class ItemLevel
    {
        [Tooltip("what is it(wood, potato, ...)")]
        public Item Item;
        [Tooltip("how many are there(5, 10, ...)")]
        public int Quantity;
        [Tooltip("how many could be there(10, 20, ...)")]
        public int Capacity;

        /// <summary>
        /// how many (storage)units the item quantity would take up
        /// </summary>
        public float UnitQuantity => (float)Quantity / Item.UnitSize;

        public ItemLevel()
        {

        }

        public ItemLevel(Item item, int quantity, int capacity)
        {
            Item = item;
            Quantity = quantity;
            Capacity = capacity;
        }

        public int Remove(int max)
        {
            var count = Mathf.Min(Quantity, max);

            Quantity -= count;

            return count;
        }

        public override string ToString()
        {
            if (Item)
                return Quantity.ToString() + "x" + Item.Name;

            return base.ToString();
        }

        #region Saving
        [Serializable]
        public class ItemLevelData
        {
            public string Key;
            public int Quantity;
            public int Capacity;

            public ItemLevel GetItemQuantity() => FromData(this);
        }

        public ItemLevelData GetData() => new ItemLevelData() { Key = Item ? Item.Key : null, Quantity = Quantity, Capacity = Capacity };
        public static ItemLevel FromData(ItemLevelData data)
        {
            if (data == null)
                return null;
            return new ItemLevel(data.Key == null ? null : Dependencies.Get<IKeyedSet<Item>>().GetObject(data.Key), data.Quantity, data.Capacity);
        }
        #endregion
    }
}