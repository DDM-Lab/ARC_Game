namespace CityBuilderCore
{
    /// <summary>
    /// building component that provides others with items
    /// </summary>
    public interface IItemGiver : IBuildingTrait<IItemGiver>, IItemOwner
    {
        /// <summary>
        /// checks how much of an item is available to be given
        /// </summary>
        /// <param name="item">the wanted item</param>
        /// <returns>unreserved quantity available</returns>
        int GetGiveQuantity(Item item);
        /// <summary>
        /// reserves a quantity of items that may later be given<br/>
        /// dont reverse more than is available(<see cref="GetGiveQuantity(Item)"/>)
        /// </summary>
        /// <param name="item">wanted item</param>
        /// <param name="quantity">quantity of the item</param>
        void ReserveQuantity(Item item, int quantity);
        /// <summary>
        /// releases a quantity that was reserved by <see cref="ReserveQuantity(Item, int)"/> before<br/>
        /// should be called before acutally moving items with <see cref="Give(ItemStorage, Item, int)"/> or if the process was interrupted
        /// </summary>
        /// <param name="item"></param>
        /// <param name="quantity"></param>
        void UnreserveQuantity(Item item, int quantity);
        /// <summary>
        /// requests that a quantity of items is put into the specified storage<br/>
        /// if the full amount is not available the remaining quantity is returned
        /// </summary>
        /// <param name="storage">the storage the items will be added to</param>
        /// <param name="item">the item to give</param>
        /// <param name="quantity">the desired quantity</param>
        /// <returns>the remaining quantity that was not available</returns>
        int Give(ItemStorage storage, Item item, int quantity);
    }
}