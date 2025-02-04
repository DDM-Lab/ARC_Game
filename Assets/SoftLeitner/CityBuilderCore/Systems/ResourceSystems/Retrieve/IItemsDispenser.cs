using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// dispenses items to items retrievers<br/>
    /// (eg map resources like trees that dispense wood, animals that dispense meat, ...)
    /// </summary>
    public interface IItemsDispenser
    {
        /// <summary>
        /// key used to identify a kind of dispenser<br/>
        /// has to match <see cref="ItemsRetrieverWalker.DispenserKey"/>
        /// </summary>
        string Key { get; }
        /// <summary>
        /// absolute position of the dispenser on the map
        /// </summary>
        Vector3 Position { get; }
        /// <summary>
        /// dispenses items from the dispense, depending on the dispenser this might destroy it or make it inactive for a while
        /// </summary>
        /// <returns>the items dispensed that can be added to the retriever and brought home</returns>
        ItemQuantity Dispense();
    }
}