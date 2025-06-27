using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// facade for an integer from 0 to 3 describing the 4 possible directions for a building<br/>
    /// used for transforming points in and out of building rotation(keeps building origin the same while rotating size and visuals)
    /// </summary>
    public class BuildingRotationHexagon : BuildingRotation
    {
        public BuildingRotationHexagon()
        {

        }
        public BuildingRotationHexagon(int state)
        {
            State = state;
        }
        public BuildingRotationHexagon(Quaternion rotation)
        {
            float angle;
            if (Dependencies.Get<IMap>().IsXY)
            {
                angle = rotation.eulerAngles.z;

                if (angle > 300)
                    State = 0;
                else if (angle > 270)
                    State = 1;
                else if (angle > 210)
                    State = 2;
                else if (angle > 150)
                    State = 3;
                else if (angle > 90)
                    State = 4;
                else if (angle > 30)
                    State = 5;
                else
                    State = 0;
            }
            else
            {
                angle = rotation.eulerAngles.y;

                if (angle > 300)
                    State = 0;
                else if (angle > 270)
                    State = 5;
                else if (angle > 210)
                    State = 4;
                else if (angle > 150)
                    State = 3;
                else if (angle > 90)
                    State = 2;
                else if (angle > 30)
                    State = 1;
                else
                    State = 0;
            }
        }

        public override void TurnClockwise()
        {
            State++;
            if (State > 5)
                State = 0;
        }
        public override void TurnCounterClockwise()
        {
            State--;
            if (State < 0)
                State = 5;
        }

        /// <summary>
        /// calculates the rotated corner of a building from the placement origin<br/>
        /// so bottom left with rotation 2 returns top right corner<br/>
        /// can be used to get get the transform position from the origin
        /// </summary>
        /// <param name="origin">origin point</param>
        /// <param name="size">building size</param>
        /// <returns></returns>
        public override Vector2Int RotateOrigin(Vector2Int origin, Vector2Int size) => origin;
        /// <summary>
        /// inverse of <see cref="RotateOrigin(Vector2Int, Vector2Int)"/>
        /// calculates the bottom left origin from the transform corner
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public override Vector2Int UnrotateOrigin(Vector2Int point, Vector2Int size) => point;

        /// <summary>
        /// rotates a point relative to the building(access points for example)
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public override Vector2Int RotateBuildingPoint(Vector2Int origin, Vector2Int point, Vector2Int size) => PositionHelper.RotateHexPoint(origin, origin + point, State);

        /// <summary>
        /// calculates the rotated size on the grid starting from the origin
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public override Vector2Int RotateSize(Vector2Int size) => size;

        /// <summary>
        /// returns the world rotation for the building visual
        /// </summary>
        /// <returns></returns>
        public override Quaternion GetRotation() => GetRotation(Dependencies.Get<IMap>().IsXY);
        public override Quaternion GetRotation(bool isXY)
        {
            if (isXY)
                return Quaternion.AngleAxis(State * 60, Vector3.back);
            else
                return Quaternion.AngleAxis(State * 60, Vector3.up);
        }
    }
}