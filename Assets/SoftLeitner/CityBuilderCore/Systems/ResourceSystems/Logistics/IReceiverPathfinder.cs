using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// calculates the best path for depositing an item(eg items produced in production)
    /// </summary>
    public interface IReceiverPathfinder
    {
        /// <summary>
        /// attempts to find a receiver and a path to it for an item a walker wants to deliver
        /// </summary>
        /// <param name="building">home building of the walker</param>
        /// <param name="currentPoint">current point of the walker(if not home)</param>
        /// <param name="items">items the walker is trying to get rid of</param>
        /// <param name="maxDistance">maximum distance a receiver can be from the walker</param>
        /// <param name="pathType">pathing type to use for the walking path</param>
        /// <param name="pathTag">additional pathing paramater</param>
        /// <param name="currentPriority"><see cref="IItemReceiver.Priority"/> of the building the walker is coming from, used to make sure an item is not delivered from one storage to another for example</param>
        /// <returns>a reference and path to a receiver if one was found</returns>
        BuildingComponentPath<IItemReceiver> GetReceiverPath(IBuilding building, Vector2Int? currentPoint, ItemQuantity items, float maxDistance, PathType pathType, object pathTag = null, int currentPriority = 0);
        BuildingComponentPathQuery<IItemReceiver> GetReceiverPathQuery(IBuilding building, Vector2Int? currentPoint, ItemQuantity items, float maxDistance, PathType pathType, object pathTag = null, int currentPriority = 0);
    }
}