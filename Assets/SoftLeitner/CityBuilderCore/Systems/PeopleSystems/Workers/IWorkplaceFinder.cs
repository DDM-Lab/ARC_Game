using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// finds the optimal workplace for any given worker and provide it with a path to work and supply if necessary
    /// </summary>
    public interface IWorkplaceFinder
    {
        /// <summary>
        /// attempts to find a worker path for a worker
        /// </summary>
        /// <param name="building">the structure the worker hails from</param>
        /// <param name="currentPoint">the current position of the worker</param>
        /// <param name="worker">the type of worker(mason, carpenter, ...)</param>
        /// <param name="storage">the storage the worker has available to carry supply to its workplace</param>
        /// <param name="maxDistance">maximum distance to workplace</param>
        /// <param name="pathType">the type of pathing the worker uses to get around</param>
        /// <param name="pathTag">additional parameter for pathfinding</param>
        /// <returns>if found, a workplace and a path to get there(possibly a path to some items the worker needs to pick up beforehand)</returns>
        WorkerPath GetWorkerPath(IBuilding building, Vector2Int? currentPoint, Worker worker, ItemStorage storage, float maxDistance, PathType pathType, object pathTag);
        /// <summary>
        /// attempts to find a worker path for a worker
        /// </summary>
        /// <param name="building">the structure the worker hails from</param>
        /// <param name="currentPoint">the current position of the worker</param>
        /// <param name="worker">the type of worker(mason, carpenter, ...)</param>
        /// <param name="storage">the storage the worker has available to carry supply to its workplace</param>
        /// <param name="maxDistance">maximum distance to workplace</param>
        /// <param name="pathType">the type of pathing the worker uses to get around</param>
        /// <param name="pathTag">additional parameter for pathfinding</param>
        /// <returns>a query that on completion may return, if found, a workplace and a path to get there(possibly a path to some items the worker needs to pick up beforehand)</returns>
        WorkerPathQuery GetWorkerPathQuery(IBuilding building, Vector2Int? currentPoint, Worker worker, ItemStorage storage, float maxDistance, PathType pathType, object pathTag);
    }
}