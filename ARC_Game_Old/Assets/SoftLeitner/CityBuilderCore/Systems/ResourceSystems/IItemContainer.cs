using System.Collections.Generic;

namespace CityBuilderCore
{
    /// <summary>
    /// interface for classes that hold and manage items, the most common implementation is <see cref="ItemStorage"/><br/>
    /// <see cref="SplitItemContainer"/> and <see cref="MultiItemContainer"/> were created so components with more than one storage can act the same as if they only had one
    /// </summary>
    public interface IItemContainer
    {
        void ReserveCapacity(Item item, int amount);
        void UnreserveCapacity(Item item, int amount);

        void ReserveQuantity(Item item, int amount);
        void UnreserveQuantity(Item item, int amount);

        IEnumerable<Item> GetItems();
        IEnumerable<ItemQuantity> GetItemQuantities();

        int GetItemQuantity();
        int GetItemQuantity(Item item);
        int GetItemQuantity(ItemCategory item);
        int GetItemCapacity();
        int GetItemCapacity(Item item);
        int GetItemCapacity(ItemCategory item);
        int GetItemCapacityRemaining();
        int GetItemCapacityRemaining(Item item);
        int GetItemCapacityRemaining(ItemCategory item);

        /// <summary>
        /// adds items to that storage up to its capacity, remaining quantity is returned<br/>
        /// (adding 10 items to a storage that can only fit 4 more will return 6)
        /// </summary>
        /// <param name="item">the item to add</param>
        /// <param name="quantity">maximum quantity to add</param>
        /// <returns>remaining quantity that did not fit</returns>
        int AddItems(Item item, int quantity);
        /// <summary>
        /// removes items from storage and returns the remaining quantity if not enough items were present<br/>
        /// (removing 4 items from a store that contains only 3 will return 1)
        /// </summary>
        /// <param name="item">the item to remove</param>
        /// <param name="quantity">the maximum quantity to remove</param>
        /// <returns>remaining quantity not removed</returns>
        int RemoveItems(Item item, int quantity);
    }
}