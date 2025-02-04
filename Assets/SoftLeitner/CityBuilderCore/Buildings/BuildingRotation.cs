using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// facade for an integer from 0 to 3 describing the 4 possible directions for a building<br/>
    /// used for transforming points in and out of building rotation(keeps building origin the same while rotating size and visuals)
    /// </summary>
    public abstract class BuildingRotation
    {
        public int State { get; protected set; }

        public abstract void TurnClockwise();
        public abstract void TurnCounterClockwise();

        /// <summary>
        /// calculates the rotated corner of a building from the placement origin<br/>
        /// so bottom left with rotation 2 returns top right corner<br/>
        /// can be used to get get the transform position from the origin
        /// </summary>
        /// <param name="origin">origin point</param>
        /// <param name="size">building size</param>
        /// <returns></returns>
        public abstract Vector2Int RotateOrigin(Vector2Int origin, Vector2Int size);

        /// <summary>
        /// inverse of <see cref="RotateOrigin(Vector2Int, Vector2Int)"/>
        /// calculates the bottom left origin from the transform corner
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public abstract Vector2Int UnrotateOrigin(Vector2Int point, Vector2Int size);

        /// <summary>
        /// rotates a point relative to the building(access points for example)<br/>
        /// used to transform points in the building(0|1, 1|1) into world points
        /// </summary>
        /// <param name="origin">origin of the building in world space</param>
        /// <param name="point">point inside the building space</param>
        /// <param name="size">size of the building</param>
        /// <returns>rotated point in world space</returns>
        public abstract Vector2Int RotateBuildingPoint(Vector2Int origin, Vector2Int point, Vector2Int size);

        /// <summary>
        /// calculates the rotated size on the grid starting from the origin
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public abstract Vector2Int RotateSize(Vector2Int size);

        /// <summary>
        /// returns the world rotation for the building visual
        /// </summary>
        /// <returns></returns>
        public abstract Quaternion GetRotation();
        public abstract Quaternion GetRotation(bool isXY);

        public static BuildingRotation Create()
        {
            if (PathHelper.IsMapHex)
                return new BuildingRotationHexagon();
            else
                return new BuildingRotationRectangle();
        }
        public static BuildingRotation Create(int state)
        {
            if (PathHelper.IsMapHex)
                return new BuildingRotationHexagon(state);
            else
                return new BuildingRotationRectangle(state);
        }
        public static BuildingRotation Create(Quaternion rotation)
        {
            if (PathHelper.IsMapHex)
                return new BuildingRotationHexagon(rotation);
            else
                return new BuildingRotationRectangle(rotation);
        }
        public static BuildingRotation Create(int state, bool isHex)
        {
            if (isHex)
                return new BuildingRotationHexagon(state);
            else
                return new BuildingRotationRectangle(state);
        }
    }
}