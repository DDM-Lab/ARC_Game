using System.Collections.Generic;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that needs to be supplied with items by others(eg production buildings that get supplied by storage)
    /// </summary>
    public interface IItemReceiver : IBuildingTrait<IItemReceiver>, IItemOwner
    {
        /// <summary>
        /// can be used so certain receivers are preferred over others<br/>
        /// for example construction sites or production may be more important that putting items into storage
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// returns the kinds of items the receiver needs
        /// </summary>
        /// <returns>the pertinent items</returns>
        IEnumerable<Item> GetReceiveItems();
        /// <summary>
        /// how much of the item the receiver can still fit
        /// </summary>
        /// <param name="item"></param>
        /// <returns>unreserved capacity available</returns>
        int GetReceiveCapacityRemaining(Item item);
        /// <summary>
        /// reserves a capacity in the receiver so it is not filled up by someone else
        /// </summary>
        /// <param name="item">the item to be reserved</param>
        /// <param name="quantity">the quantity to reserve</param>
        void ReserveCapacity(Item item, int quantity);
        /// <summary>
        /// releases capacity that was earlier reserved by <see cref="ReserveCapacity(Item, int)"/><br/>
        /// call before actually moving items into the receiver or if the delivery was somehow interrupted
        /// </summary>
        /// <param name="item">item to unreserve</param>
        /// <param name="quantity">quantity to unreserve</param>
        void UnreserveCapacity(Item item, int quantity);
        /// <summary>
        /// requests that a quantity of items is moved from the storage to the receiver<br/>
        /// if the full amount does not fit the remaining quantity is returned
        /// </summary>
        /// <param name="storage">the storage that items will be taken from</param>
        /// <param name="item">the item to receive</param>
        /// <param name="quantity">the maximum quantity of items to receive</param>
        /// <returns>remaining quantity that did not fit</returns>
        int Receive(ItemStorage storage, Item item, int quantity);
    }
}