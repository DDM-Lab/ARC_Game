using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// optional interface that adjusts the height of entities on the map<br/>
    /// the <see cref="DefaultMapHeight"/> implementation does this by moving them up and down which makes sense for 3d games<br/>
    /// 2d games can ignore this entirely or perhaps add an implementation that scales entities a little bit
    /// </summary>
    public interface IGridHeights
    {
        /// <summary>
        /// calculates height at given world position
        /// </summary>
        /// <param name="position">absolute world position</param>
        /// <param name="pathType">height may be different when moving on a road for example</param>
        /// <returns>height at position or 0 if height cannot be represented by a number(layer height for example)</returns>
        float GetHeight(Vector3 position, PathType pathType = PathType.Map);
        /// <summary>
        /// applies heigth to the passed transform<br/>
        /// the transforms position may be used to calculate the height
        /// </summary>
        /// <param name="transform">the transform that will be changed</param>
        /// <param name="position">world position to calculate height for</param>
        /// <param name="pathType">height may be different when moving on a road for example</param>
        /// <param name="overrideValue">regular height calculation may not always apply, for example on bridges(which use IOverrideHeight)</param>
        void ApplyHeight(Transform transform, Vector3 position, PathType pathType = PathType.Map, float? overrideValue = null);
        /// <summary>
        /// applies heigth to the passed position<br/>
        /// the transforms position may be used to calculate the height
        /// </summary>
        /// <param name="position">world position to calculate height for</param>
        /// <param name="pathType">height may be different when moving on a road for example</param>
        /// <param name="overrideValue">regular height calculation may not always apply, for example on bridges(which use IOverrideHeight)</param>
        Vector3 ApplyHeight(Vector3 position, PathType pathType = PathType.Map, float? overrideValue = null);
    }
}
