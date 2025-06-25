using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// keeps track of all the <see cref="IItemsDispenser"/> and calculates the dispenser a retriever goes for
    /// </summary>
    public interface IItemsDispenserManager
    {
        /// <summary>
        /// adds a dispenser to the manager so it can be found by retrievers
        /// </summary>
        /// <param name="dispenser">the retrievers to add</param>
        void Add(IItemsDispenser dispenser);
        /// <summary>
        /// removes a previously added retriever so it can no longer be found be retrievers
        /// </summary>
        /// <param name="dispenser">the dispenser to remove</param>
        void Remove(IItemsDispenser dispenser);

        /// <summary>
        /// looks for the closest dispenser that has the right key
        /// </summary>
        /// <param name="key">key used to discern different types of dispensers(rock, tree, gold, ...)</param>
        /// <param name="position">absolute world position of the retriever</param>
        /// <param name="maxDistance">maximum distance between dispenser and retriever</param>
        /// <returns>the closest dispenser if one was found</returns>
        IItemsDispenser GetDispenser(string key, Vector3 position, float maxDistance);
        /// <summary>
        /// checks if there is a dispenser with the right key in the vicinity
        /// </summary>
        /// <param name="key">key used to discern different types of dispensers(rock, tree, gold, ...)</param>
        /// <param name="position">absolute world position of the retriever</param>
        /// <param name="maxDistance">maximum distance between dispenser and retriever</param>
        /// <returns>true if a dispenser is available</returns>
        bool HasDispenser(string key, Vector3 position, float maxDistance);
    }
}