using System.Collections.Generic;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that receives items passively<br/>
    /// for example from a <see cref="SaleWalker"/> walking by
    /// </summary>
    public interface IItemRecipient : IBuildingComponent, IItemOwner
    {
        /// <summary>
        /// use this to check which items the recipient needs
        /// </summary>
        /// <returns>items the recipient needs</returns>
        IEnumerable<Item> GetRecipientItems();
        /// <summary>
        /// checks how much of every needed item the recipient currently has
        /// </summary>
        /// <returns>current item quantities the recipient has</returns>
        IEnumerable<ItemQuantity> GetRecipientItemQuantities();
        /// <summary>
        /// passes a storage to the recipient which will take any item from it that it needs
        /// </summary>
        /// <param name="itemStorage">the storage the recipient will take from</param>
        void FillRecipient(ItemStorage itemStorage);
    }
}