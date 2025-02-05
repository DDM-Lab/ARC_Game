using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// facade for an integer from 0 to 3 describing the 4 possible directions for a building<br/>
    /// used for transforming points in and out of building rotation(keeps building origin the same while rotating size and visuals)
    /// </summary>
    public class BuildingRotationRectangle : BuildingRotation
    {
        public BuildingRotationRectangle()
        {

        }
        public BuildingRotationRectangle(int state)
        {
            State = state;
        }
        public BuildingRotationRectangle(Quaternion rotation)
        {
            float angle;
            if (Dependencies.Get<IMap>().IsXY)
            {
                angle = rotation.eulerAngles.z;

                if (angle > 300)
                    State = 0;
                else if (angle > 200)
                    State = 1;
                else if (angle > 100)
                    State = 2;
                else if (angle > 50)
                    State = 3;
                else
                    State = 0;
            }
            else
            {
                angle = rotation.eulerAngles.y;

                if (angle > 300)
                    State = 0;
                else if (angle > 200)
                    State = 3;
                else if (angle > 100)
                    State = 2;
                else if (angle > 50)
                    State = 1;
                else
                    State = 0;
            }
        }

        public override void TurnClockwise()
        {
            State++;
            if (State > 3)
                State = 0;
        }
        public override void TurnCounterClockwise()
        {
            State--;
            if (State < 0)
                State = 3;
        }

        /// <summary>
        /// calculates the rotated corner of a building from the placement origin<br/>
        /// so bottom left with rotation 2 returns top right corner<br/>
        /// can be used to get get the transform position from the origin
        /// </summary>
        /// <param name="origin">origin point</param>
        /// <param name="size">building size</param>
        /// <returns></returns>
        public override Vector2Int RotateOrigin(Vector2Int origin, Vector2Int size)
        {
            size = RotateSize(size);

            switch (State)
            {
                default:
                    return origin;
                case 1:
                    return new Vector2Int(origin.x, origin.y + size.y);
                case 2:
                    return origin + size;
                case 3:
                    return new Vector2Int(origin.x + size.x, origin.y);
            }
        }
        /// <summary>
        /// inverse of <see cref="RotateOrigin(Vector2Int, Vector2Int)"/>
        /// calculates the bottom left origin from the transform corner
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public override Vector2Int UnrotateOrigin(Vector2Int point, Vector2Int size)
        {
            size = RotateSize(size);

            switch (State)
            {
                default:
                    return point;
                case 1:
                    return new Vector2Int(point.x, point.y - size.y);
                case 2:
                    return point - size;
                case 3:
                    return new Vector2Int(point.x - size.x, point.y);
            }
        }

        /// <summary>
        /// rotates a point relative to the building(access points for example)
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public override Vector2Int RotateBuildingPoint(Vector2Int origin, Vector2Int point, Vector2Int size)
        {
            return RotateOrigin(origin, size) + getRelativeSize(point) + getOffset();
        }

        /// <summary>
        /// calculates the rotated size on the grid starting from the origin
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public override Vector2Int RotateSize(Vector2Int size)
        {
            if (State % 2 == 0)
                return size;
            else
                return new Vector2Int(size.y, size.x);
        }

        /// <summary>
        /// returns the world rotation for the building visual
        /// </summary>
        /// <returns></returns>
        public override Quaternion GetRotation() => GetRotation(Dependencies.Get<IMap>().IsXY);
        public override Quaternion GetRotation(bool isXY)
        {
            if (isXY)
                return Quaternion.AngleAxis(State * 90, Vector3.back);
            else
                return Quaternion.AngleAxis(State * 90, Vector3.up);
        }

        private Vector2Int getOffset()
        {
            switch (State)
            {
                default:
                    return Vector2Int.zero;
                case 1:
                    return new Vector2Int(0, -1);
                case 2:
                    return -Vector2Int.one;
                case 3:
                    return new Vector2Int(-1, 0);
            }
        }

        private Vector2Int getRelativeSize(Vector2Int size)
        {
            switch (State)
            {
                default:
                    return size;
                case 1:
                    return new Vector2Int(size.y, -size.x);
                case 2:
                    return new Vector2Int(-size.x, -size.y);
                case 3:
                    return new Vector2Int(-size.y, size.x);
            }
        }
    }
}