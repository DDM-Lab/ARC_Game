using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// pathfinders calculate a path between points using different methods<br/>
    /// there is a sub-interface for every <see cref="PathType"/>, this is done so a particular pathfinder can be requested from <see cref="Dependencies"/>
    /// </summary>
    public interface IPathfinder
    {
        /// <summary>
        /// checks if a point exists within the pathfinder<br/>
        /// for example points on map pathing that are blocked or points for road pathing that dont have a road
        /// </summary>
        /// <param name="point">the map point to check</param>
        /// <param name="tag">additional pathfinding parameter(road type, walker info, ...)</param>
        /// <returns>whether the point exists in the pathfinder</returns>
        bool HasPoint(Vector2Int point, object tag = null);
        /// <summary>
        /// attempts to find a path between one of the starts and one of the ends
        /// </summary>
        /// <param name="starts">collection of one or more possible start points</param>
        /// <param name="targets">collection of one or more possible start points</param>
        /// <param name="tag">additional pathfinding parameter(road type, walker info, ...)</param>
        /// <returns>the calculated path or null when no path was found</returns>
        WalkingPath FindPath(Vector2Int[] starts, Vector2Int[] targets, object tag = null);
        PathQuery FindPathQuery(Vector2Int[] starts, Vector2Int[] targets, object tag = null);
    }
    /// <summary>
    /// pathfinder that finds a path inside a network of roads(uses A* by default)
    /// </summary>
    public interface IRoadPathfinder : IPathfinder { }
    /// <summary>
    /// same as <see cref="IRoadPathfinder"/> but without points that are blocked<br/>
    /// points can be blocked using <see cref="IRoadManager"/>, for example THREE uses the <see cref="RoadBlockerComponent"/> to do this
    /// </summary>
    public interface IRoadPathfinderBlocked : IPathfinder { }
    /// <summary>
    /// pathfinder that uses the unity navmesh to find a path on the map
    /// </summary>
    public interface IMapPathfinder : IPathfinder { }
    /// <summary>
    /// pthfinder that uses a A* grid to find a path<br/>
    /// builds a complete grid of points so only viable for smaller maps
    /// </summary>
    public interface IMapGridPathfinder : IPathfinder { }
}