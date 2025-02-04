using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// transforms between grid and world positions
    /// </summary>
    public interface IGridPositions
    {
        /// <summary>
        /// turns a world position into a map point
        /// </summary>
        /// <param name="position">absolute world space position(transform.position)</param>
        /// <returns>map point of the cell the position was inside</returns>
        Vector2Int GetGridPoint(Vector3 position);
        /// <summary>
        /// turns a map point into the world position in its lower corner(without height)
        /// </summary>
        /// <param name="point">map/cell point</param>
        /// <returns>absolute world position in the corner of the cell</returns>
        Vector3 GetWorldPosition(Vector2Int point);
        /// <summary>
        /// calculates the cells center from its corner position<br/>
        /// to get the cell center from any position in it use <see cref="Extensions.GetWorldCenterPosition(IGridPositions, Vector3)"/>
        /// </summary>
        /// <param name="position">absolute world space position of the cell cornder</param>
        /// <returns>center of the cell in absolute world space</returns>
        Vector3 GetCenterFromPosition(Vector3 position);
        /// <summary>
        /// calculates the cells corner position from its center
        /// </summary>
        /// <param name="center">absolute world space position of the cell center</param>
        /// <returns>corner of the cell in absolute world space</returns>
        Vector3 GetPositionFromCenter(Vector3 center);
    }
}