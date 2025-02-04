using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// provides access to the current location of the players pointer
    /// </summary>
    public interface IMouseInput
    {
        /// <summary>
        /// creates a ray under the players mouse that points in the cameras direction<br/>
        /// can be used to check whats currently under the player pointer
        /// </summary>
        /// <param name="applyOffset">whether the touch offset should be applied</param>
        /// <returns>a ray coming from the pointer</returns>
        Ray GetRay(bool applyOffset = false);
        /// <summary>
        /// calculates absolute world position on the map that the pointer is currently over
        /// </summary>
        /// <param name="applyOffset">whether the touch offset should be applied</param>
        /// <returns>a position on the map</returns>
        Vector3 GetMousePosition(bool applyOffset = false);
        /// <summary>
        /// calculates the current position of the players pointer in screen coordinates
        /// </summary>
        /// <param name="applyOffset">whether the touch offset should be applied</param>
        /// <returns>screen space position of the pointer</returns>
        Vector2 GetMouseScreenPosition(bool applyOffset = false);
        /// <summary>
        /// calculates the point on the map that the pointer is currently over
        /// </summary>
        /// <param name="applyOffset">whether the touch offset should be applied</param>
        /// <returns>a grid point on the map</returns>
        Vector2Int GetMouseGridPosition(bool applyOffset = false);
    }
}