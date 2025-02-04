using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// rotates transforms, 3d implementations might rotate 360° while isometric games may only mirror
    /// </summary>
    public interface IGridRotations
    {
        /// <summary>
        /// adjusts a transform in order to visually reflect the direction it is facing
        /// </summary>
        /// <param name="transform">the transform that will be changed</param>
        /// <param name="direction">the direction the entity moves or looks at</param>
        void SetRotation(Transform transform, Vector3 direction);
        /// <summary>
        /// adjusts a transform by a rotation in degrees
        /// </summary>
        /// <param name="transform">the transform that will be chaned</param>
        /// <param name="rotation">the rotation in degrees</param>
        void SetRotation(Transform transform, float rotation);

        /// <summary>
        /// calculates a numeric rotation in degrees from a direction
        /// </summary>
        /// <param name="direction">direction the object is facing</param>
        /// <returns>rotation in degrees</returns>
        float GetRotation(Vector3 direction);
        /// <summary>
        /// calculates a direction from a numeric roation
        /// </summary>
        /// <param name="angle">rotation indegrees</param>
        /// <returns>direction the angle is facing</returns>
        Vector3 GetDirection(float angle);
    }
}