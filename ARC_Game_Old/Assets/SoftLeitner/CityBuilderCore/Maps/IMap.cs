using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// basic map functionality like map size and buildability
    /// </summary>
    public interface IMap
    {
        /// <summary>
        /// whether the map grid is in xy, usually in 2d games<br/>
        /// 3d games often use Y as the height axis and have their grid in XZ
        /// </summary>
        bool IsXY { get; }
        /// <summary>
        /// whether we're on a hexagonal map which may need some special handling
        /// </summary>
        bool IsHex { get; }
        /// <summary>
        /// map size in cell count
        /// </summary>
        Vector2Int Size { get; }
        /// <summary>
        /// delta position from one cell to the next
        /// </summary>
        Vector3 CellOffset { get; }
        /// <summary>
        /// world position at the center of the map
        /// </summary>
        Vector3 WorldCenter { get; }

        /// <summary>
        /// checks if a point is inside the map size
        /// </summary>
        /// <param name="point">the point to check</param>
        /// <returns>true if the point is inside the map</returns>
        bool IsInside(Vector2Int point);
        /// <summary>
        /// check if a point can be walked on and may be used in pathfinding
        /// </summary>
        /// <param name="point">the point to check</param>
        /// <returns>true if the point allows walkers to pass</returns>
        bool IsWalkable(Vector2Int point);
        /// <summary>
        /// checks if a point is ok to build on or if the map somehow blocks building there
        /// </summary>
        /// <param name="point">the point on the map to check</param>
        /// <param name="mask">the structure levels to check, 0 for all</param>
        /// <returns></returns>
        bool IsBuildable(Vector2Int point, int mask, object tag = null);
        /// <summary>
        /// checks the ground for certain features<br/>
        /// for example in tilemap base maps this would be a tile
        /// </summary>
        /// <param name="point">the point on the map to check</param>
        /// <param name="options">a set of features that would be acceptable</param>
        /// <returns>true if the ground exhibits one of the features</returns>
        bool CheckGround(Vector2Int point, Object[] options);
        /// <summary>
        /// clamps a position to be inside the maps size<br/>
        /// if, for example, a position 5,-10 is passed for a map that starts at 0,0 the position will be clamped to 5,0
        /// </summary>
        /// <param name="position">the position to check</param>
        /// <returns>the clamped position</returns>
        Vector3 ClampPosition(Vector3 position);
        /// <summary>
        /// clamps a point to be inside the maps size
        /// </summary>
        /// <param name="point">a point that may or may not be inside the map</param>
        /// <returns>a point inside the map</returns>
        Vector2Int ClampPoint(Vector2Int point);
        /// <summary>
        /// get position variance for walkers and such, can be used so walkers on the same point dont overlap
        /// </summary>
        /// <returns></returns>
        Vector3 GetVariance();
    }
}