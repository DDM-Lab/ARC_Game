using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// sub container for item 'slots' for storages in <see cref="ItemStorageMode.Stacked"/><br/>
    /// a stacked storage can define a number of stacks<br/>
    /// each stack can only contain one type of item<br/>
    /// makes for some nicely visualized storages
    /// </summary>
    [Serializable]
    public class ItemStack
    {
        [Tooltip("how many (storage)units the stack can hold, how many items that is depends on the items UnitSize")]
        public int UnitCapacity;

        public ItemQuantity Items { get; private set; }
        public int ReservedCapacity { get; private set; }

        public bool HasItems => Items != null && Items.Item != null;
        public float FillDegree => HasItems ? (float)Items.Quantity / (UnitCapacity * Items.Item.UnitSize) : 0f;

        public event Action<ItemStack> Changed;

        public int GetItemCapacity(Item item)
        {
            return UnitCapacity * item.UnitSize;
        }
        public int GetItemCapacityRemaining(Item item)
        {
            if (HasItems)
            {
                if (Items.Item != item)
                    return 0;

                return UnitCapacity * item.UnitSize - Items.Quantity - ReservedCapacity;
            }
            else
            {
                return UnitCapacity * item.UnitSize;
            }
        }

        /// <summary>
        /// checks whether this stack is bound to a certain item, either because it contains a 
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool HasItem(Item item) => Items != null && Items.Item == item;

        /// <summary>
        /// adds number of items to stack, returns remaining items not added
        /// </summary>
        /// <param name="item">the type of item to add</param>
        /// <param name="quantity">how many of the item to add</param>
        /// <returns>the quantity that was not added because it does not fit</returns>
        public int AddQuantity(Item item, int quantity)
        {
            int capacity = UnitCapacity * item.UnitSize;

            if (HasItems)
            {
                if (Items.Item != item)
                    return quantity;

                int added = Mathf.Min(capacity - Items.Quantity - ReservedCapacity, quantity);

                Items.Quantity += added;

                onChanged();

                return quantity - added;
            }
            else
            {
                Items = new ItemQuantity()
                {
                    Item = item,
                    Quantity = Mathf.Min(capacity, quantity)
                };

                onChanged();

                return quantity - Items.Quantity;
            }
        }

        /// <summary>
        /// removes items from the stack
        /// </summary>
        /// <param name="item">the type of item to remove</param>
        /// <param name="quantity">how much of the item to remove</param>
        /// <returns>the remaining quantity that was not removed</returns>
        public int RemoveQuantity(Item item, int quantity)
        {
            if (!HasItems || Items.Item != item)
                return quantity;

            if (quantity >= Items.Quantity)
            {
                quantity -= Items.Quantity;
                if (ReservedCapacity == 0)
                    Items = null;
                else
                    Items.Quantity = 0;
            }
            else
            {
                Items.Quantity -= quantity;
                quantity = 0;
            }

            onChanged();

            return quantity;
        }

        public void SetQuantity(int amount)
        {
            if (amount == 0)
                Items = null;
            else
                Items.Quantity = amount;

            onChanged();
        }

        public int ReserveCapacity(Item item, int amount)
        {
            int capacity = UnitCapacity * item.UnitSize;

            if (HasItems)
            {
                if (Items.Item != item)
                    return amount;

                int added = Mathf.Min(capacity - Items.Quantity, amount);

                ReservedCapacity += added;

                onChanged();

                return amount - added;
            }
            else
            {
                Items = new ItemQuantity()
                {
                    Item = item,
                    Quantity = 0
                };

                ReservedCapacity = Mathf.Min(capacity, amount);

                onChanged();

                return amount - ReservedCapacity;
            }
        }
        public int UnreserveCapacity(Item item, int amount)
        {
            if (!HasItems || Items.Item != item)
                return amount;

            if (amount >= ReservedCapacity)
            {
                amount -= ReservedCapacity;
                ReservedCapacity = 0;
                if (Items.Quantity == 0)
                    Items = null;
            }
            else
            {
                ReservedCapacity -= amount;
                amount = 0;
            }

            onChanged();

            return amount;
        }

        private void onChanged() => Changed?.Invoke(this);

        #region Saving
        [Serializable]
        public class ItemStackData
        {
            public string Key;
            public int Quantity;
            public int ReservedCapacity;
        }
        public ItemStackData GetData()
        {
            return new ItemStackData()
            {
                Key = Items?.Item?.Key,
                Quantity = Items?.Quantity ?? 0,
                ReservedCapacity = ReservedCapacity
            };
        }
        public void SetData(ItemStackData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.Key))
            {
                Items = null;
                ReservedCapacity = 0;
            }
            else
            {
                Items = new ItemQuantity(Dependencies.Get<IKeyedSet<Item>>().GetObject(data.Key), data.Quantity);
                ReservedCapacity = data.ReservedCapacity;
            }

            onChanged();
        }
        #endregion
    }
}