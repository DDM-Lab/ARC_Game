using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// convenience container that combines an item with a quantity
    /// </summary>
    [Serializable]
    public class ItemQuantity
    {
        [Tooltip("what is it(wood, potato, ...)")]
        public Item Item;
        [Tooltip("how many are there(5, 10, ...)")]
        public int Quantity;

        /// <summary>
        /// how many (storage)units the item quantity would take up
        /// </summary>
        public float UnitQuantity => (float)Quantity / Item.UnitSize;

        public ItemQuantity()
        {

        }

        public ItemQuantity(Item item, int quantity)
        {
            Item = item;
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
            if (Item)
                return Quantity.ToString() + "x" + Item.Name;

            return base.ToString();
        }

        #region Saving
        [Serializable]
        public class ItemQuantityData
        {
            public string Key;
            public int Quantity;

            public ItemQuantity GetItemQuantity() => FromData(this);
        }

        public ItemQuantityData GetData() => new ItemQuantityData() { Key = Item ? Item.Key : null, Quantity = Quantity };
        public static ItemQuantity FromData(ItemQuantityData data)
        {
            if (data == null)
                return null;
            return new ItemQuantity(data.Key == null ? null : Dependencies.Get<IKeyedSet<Item>>().GetObject(data.Key), data.Quantity);
        }
        #endregion
    }
}