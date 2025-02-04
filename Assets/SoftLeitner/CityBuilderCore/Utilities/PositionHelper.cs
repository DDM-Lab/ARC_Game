using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    public static class PositionHelper
    {
        /// <summary>
        /// returns positions for L shaped path between the points
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public static IEnumerable<Vector2Int> GetRoadPositions(Vector2Int start, Vector2Int end)
        {
            if (start == end)
            {
                return new Vector2Int[] { end };
            }

            if (PathHelper.IsMapHex)
            {
                return GetRoadPositionsHex(start, end);
            }
            else
            {
                return GetRoadPositionsRect(start, end);
            }
        }
        public static IEnumerable<Vector2Int> GetRoadPositionsRect(Vector2Int start, Vector2Int end)
        {
            var xDirection = Math.Sign(end.x - start.x);
            var yDirection = Math.Sign(end.y - start.y);

            if (Math.Abs(start.x - end.x) >= Math.Abs(start.y - end.y))
            {
                for (int x = start.x; x != end.x; x += xDirection)
                {
                    yield return new Vector2Int(x, start.y);//move x towards end
                }

                for (int y = start.y; y != end.y; y += yDirection)
                {
                    yield return new Vector2Int(end.x, y);//move y towards end
                }
            }
            else
            {
                for (int y = start.y; y != end.y; y += yDirection)
                {
                    yield return new Vector2Int(start.x, y);//move y towards end
                }

                for (int x = start.x; x != end.x; x += xDirection)
                {
                    yield return new Vector2Int(x, end.y);//move x towards end
                }
            }

            yield return end;
        }
        public static IEnumerable<Vector2Int> GetRoadPositionsHex(Vector2Int start, Vector2Int end)
        {
            var startC = toCube(start);
            var endC = toCube(end);
            var delta = endC - startC;
            var deltaAbs = delta.Abs();

            int axisA, axisB, axisC;

            if (deltaAbs.x <= deltaAbs.y && deltaAbs.x <= deltaAbs.z)
            {
                axisA = 0;
                if (deltaAbs.y <= deltaAbs.z)
                {
                    axisB = 1;
                    axisC = 2;
                }
                else
                {
                    axisB = 2;
                    axisC = 1;
                }
            }
            else if (deltaAbs.y <= deltaAbs.z)
            {
                axisA = 1;
                if (deltaAbs.x <= deltaAbs.z)
                {
                    axisB = 0;
                    axisC = 2;
                }
                else
                {
                    axisB = 2;
                    axisC = 0;
                }
            }
            else
            {
                axisA = 2;
                if (deltaAbs.x <= deltaAbs.y)
                {
                    axisB = 0;
                    axisC = 1;
                }
                else
                {
                    axisB = 1;
                    axisC = 0;
                }
            }

            var current = startC;

            yield return fromCube(current);

            var valueB = getCubeValue(delta, axisB);
            var signB = Math.Sign(valueB);
            valueB = Math.Abs(valueB);

            for (int i = 0; i < valueB; i++)
            {
                current += getCubeDirection(axisB) * signB;
                current += getCubeDirection(axisC) * -signB;

                yield return fromCube(current);
            }

            var valueA = getCubeValue(delta, axisA);
            var signA = Math.Sign(valueA);
            valueA = Math.Abs(valueA);

            for (int i = 0; i < valueA; i++)
            {
                current += getCubeDirection(axisA) * signA;
                current += getCubeDirection(axisC) * -signA;

                yield return fromCube(current);
            }
        }

        private static int getCubeValue(Vector3Int cube, int axis)
        {
            if (axis == 0)
                return cube.x;
            else if (axis == 1)
                return cube.y;
            else
                return cube.z;
        }
        private static Vector3Int getCubeDirection(int axis)
        {
            if (axis == 0)
                return new Vector3Int(1, 0, 0);
            else if (axis == 1)
                return new Vector3Int(0, 1, 0);
            else
                return new Vector3Int(0, 0, 1);
        }

        public static IEnumerable<Vector2Int> GetRoadPositionsHexLinear(Vector2Int start, Vector2Int end)
        {
            foreach (var point in cubeLine(toCube(start), toCube(end)))
            {
                yield return fromCube(point);
            }
        }

        /// <summary>
        /// returns positions in a box with the passed points as corners
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public static IEnumerable<Vector2Int> GetBoxPositions(Vector2Int start, Vector2Int end)
        {
            var xDirection = Math.Sign(end.x - start.x);
            var yDirection = Math.Sign(end.y - start.y);

            if (xDirection == 0)
                xDirection = 1;
            if (yDirection == 0)
                yDirection = 1;

            for (int x = start.x; x != end.x + xDirection; x += xDirection)
            {
                for (int y = start.y; y != end.y + yDirection; y += yDirection)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }
        /// <summary>
        /// returns positions in a box in multiples of size with the passed points as corners<br/>
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static IEnumerable<Vector2Int> GetBoxPositions(Vector2Int start, Vector2Int end, Vector2Int size)
        {
            if (size.x <= 0 || size.y <= 0)
                yield break;

            var xDirection = Math.Sign(end.x - start.x);
            var yDirection = Math.Sign(end.y - start.y);

            if (xDirection == 0)
                xDirection = 1;
            if (yDirection == 0)
                yDirection = 1;

            int deltaX = 0;
            for (int x = start.x; x != end.x + xDirection; x += xDirection)
            {
                int deltaY = 0;
                for (int y = start.y; y != end.y + yDirection; y += yDirection)
                {
                    if (deltaX % size.x == 0 && deltaY % size.y == 0)
                        yield return new Vector2Int(x, y);

                    deltaY++;
                }
                deltaX++;
            }
        }

        public static IEnumerable<Vector2Int> GetStructurePositions(Vector2Int point, Vector2Int size)
        {
            if (PathHelper.IsMapHex)
            {
                yield return point;

                for (int i = 1; i < size.x; i++)
                {
                    foreach (var adjacent in GetAdjacentHex(point, Vector2Int.one, i))
                    {
                        yield return adjacent;
                    }
                }
            }
            else
            {
                for (int x = 0; x < size.x; x++)
                {
                    for (int y = 0; y < size.y; y++)
                    {
                        yield return new Vector2Int(point.x + x, point.y + y);
                    }
                }
            }
        }
        public static IEnumerable<Vector2Int> GetStructurePositions(Vector2Int point, Vector2Int size, BuildingRotation rotation)
        {
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    yield return rotation.RotateBuildingPoint(point, new Vector2Int(x, y), size);
                }
            }
        }

        public static IEnumerable<Vector2Int> GetAdjacentHex(Vector2Int point, Vector2Int size, int distance)
        {
            if (distance == 0)
            {
                yield return point;
                yield break;
            }

            distance += size.x - 1;

            point.x -= distance;

            //NE
            for (int i = 0; i < distance; i++)
            {
                yield return point;

                if (point.y % 2 == 1)
                    point.x++;
                point.y++;
            }

            //E
            for (int i = 0; i < distance; i++)
            {
                yield return point;

                point.x++;
            }

            //SE
            for (int i = 0; i < distance; i++)
            {
                yield return point;

                if (point.y % 2 == 1)
                    point.x++;
                point.y--;
            }

            //SW
            for (int i = 0; i < distance; i++)
            {
                yield return point;

                if (point.y % 2 == 0)
                    point.x--;
                point.y--;
            }

            //W
            for (int i = 0; i < distance; i++)
            {
                yield return point;

                point.x--;
            }

            //NW
            for (int i = 0; i < distance; i++)
            {
                yield return point;

                if (point.y % 2 == 0)
                    point.x--;
                point.y++;
            }
        }
        public static IEnumerable<Vector2Int> GetAdjacentRect(Vector2Int point, Vector2Int size, int distance)
        {
            if (distance == 0)
            {
                yield return point;
                yield break;
            }

            point += new Vector2Int(-distance, -distance);

            for (int i = 0; i < size.x + 2 * distance - 1; i++)
            {
                yield return point;

                point.x++;
            }

            for (int i = 0; i < size.y + 2 * distance - 1; i++)
            {
                yield return point;

                point.y++;
            }

            for (int i = 0; i < size.x + 2 * distance - 1; i++)
            {
                yield return point;

                point.x--;
            }

            for (int i = 0; i < size.y + 2 * distance - 1; i++)
            {
                yield return point;

                point.y--;
            }
        }
        public static IEnumerable<Vector2Int> GetAdjacentCross(Vector2Int point, Vector2Int size, int distance)
        {
            var p = new Vector2Int(point.x, point.y - distance);

            for (int i = 0; i < size.x; i++)
            {
                yield return p;

                p.x++;
            }

            p.Set(point.x + size.x - 1 + distance, point.y);

            for (int i = 0; i < size.y; i++)
            {
                yield return p;

                p.y++;
            }

            p.Set(point.x + size.x - 1, point.y + size.y - 1 + distance);

            for (int i = 0; i < size.x; i++)
            {
                yield return p;

                p.x--;
            }

            p.Set(point.x - distance, point.y + size.y - 1);

            for (int i = 0; i < size.y; i++)
            {
                yield return p;

                p.y--;
            }
        }

        public static IEnumerable<Vector2Int> GetAdjacent(Vector2Int point, Vector2Int size, bool diagonal = false, int offset = 0, int range = 1)
        {
            for (int i = 1; i <= range; i++)
            {
                if (PathHelper.IsMapHex)
                {
                    foreach (var adjacent in GetAdjacentHex(point, size, offset + i))
                    {
                        yield return adjacent;
                    }
                }
                else
                {
                    if (diagonal)
                    {
                        foreach (var adjacent in GetAdjacentRect(point, size, offset + i))
                        {
                            yield return adjacent;
                        }
                    }
                    else
                    {
                        foreach (var adjacent in GetAdjacentCross(point, size, offset + i))
                        {
                            yield return adjacent;
                        }
                    }
                }
            }
        }

        public static Vector2Int RotateHexPoint(Vector2Int origin, Vector2Int point, int amount = 1)
        {
            if (amount == 0)
                return point;

            var originC = toCube(origin);
            var pointC = toCube(point);

            var vector = pointC - originC;

            for (int i = 0; i < amount; i++)
            {
                //vector = new Vector3Int(-vector.z, -vector.x, -vector.y);
                vector = new Vector3Int(-vector.y, -vector.z, -vector.x);
            }

            var rotatedC = vector + originC;

            return fromCube(rotatedC);
        }

        #region hexUtils
        private static Vector2Int fromCube(Vector3Int cube)
        {
            return new Vector2Int(cube.x + (cube.z - (cube.z & 1)) / 2, cube.z);
        }
        private static Vector3Int toCube(Vector2Int hex)
        {
            var x = hex.x - (hex.y - (hex.y & 1)) / 2;
            var z = hex.y;
            var y = -x - z;
            return new Vector3Int(x, y, z);
        }
        private static int cubeDistance(Vector3Int a, Vector3Int b)
        {
            return (Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) + Math.Abs(a.z - b.z)) / 2;
        }
        private static Vector3Int cubeLerp(Vector3Int a, Vector3Int b, float t)
        {
            return Vector3Int.RoundToInt(Vector3.Lerp(a, b, t));
        }
        private static List<Vector3Int> cubeLine(Vector3Int a, Vector3Int b)
        {
            var dist = cubeDistance(a, b);
            var points = new List<Vector3Int>();
            for (int i = 0; i <= dist; i++)
            {
                points.Add(cubeLerp(a, b, 1f / dist * i));
            }
            return points;
        }
        #endregion
    }
}