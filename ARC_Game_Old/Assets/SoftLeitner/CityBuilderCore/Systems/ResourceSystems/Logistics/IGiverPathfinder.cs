using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// calculates the best path for getting a needed item
    /// </summary>
    public interface IGiverPathfinder
    {
        /// <summary>
        /// attempts to find an <see cref="IItemGiver"/> and a path to it for a needed item
        /// </summary>
        /// <param name="building">home building of the walker if any</param>
        /// <param name="currentPoint">current position of the walker(if not home)</param>
        /// <param name="items">items that are needed</param>
        /// <param name="maxDistance">maximum distance from walker to giver</param>
        /// <param name="pathType">defines the type of pathing to use</param>
        /// <param name="pathTag">additional pathing parameter</param>
        /// <returns></returns>
        BuildingComponentPath<IItemGiver> GetGiverPath(IBuilding building, Vector2Int? currentPoint, ItemQuantity items, float maxDistance, PathType pathType, object pathTag = null);
        BuildingComponentPathQuery<IItemGiver> GetGiverPathQuery(IBuilding building, Vector2Int? currentPoint, ItemQuantity items, float maxDistance, PathType pathType, object pathTag = null);
    }
}