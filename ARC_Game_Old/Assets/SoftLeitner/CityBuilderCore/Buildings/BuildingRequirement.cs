using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// class that expresses what is needed for a building to be placed, used in the regular <see cref="BuildingInfo"/><br/>
    /// Mode, Points and Count specify which or how many points of the building are checked<br/>
    /// the rest of the fields are the things that those points will be checked against
    /// </summary>
    [System.Serializable]
    public class BuildingRequirement
    {
        [Tooltip(@"how the points a building might be built on are checked for its requirements
Any		Count or more of the building
Average		all points summed and divided
Specific		Points specified in next field
All		all points of the building
AnySpecific	Count or more of Points")]
        public BuildingRequirementMode Mode;
        [Tooltip("the points being checked when using SPECIFIC mode")]
        public Vector2Int[] Points;
        [Tooltip("the minimum number of points when using ANY mode")]
        public int Count = 1;
        [Tooltip("a window of layer values that has to be fulfilled to be able to build(set layer to NONE to ignore)")]
        public LayerRequirement LayerRequirement;
        [Tooltip("an object the maps ground has to exhibit to be able to build(TILES when using the included maps)")]
        public Object[] GroundOptions;
        [Tooltip("a building that has to be present already(two buildings can occupy the same space by using different levels)")]
        public BuildingInfo Building;
        [Tooltip("the points of the other building that can be used to meet the requirement(Optional)")]
        public Vector2Int[] BuildingPoints;

        public bool IsFulfilled(Vector2Int point, Vector2Int size, BuildingRotation rotation, IEnumerable<Vector2Int> points = null)
        {
            if (LayerRequirement != null && LayerRequirement.Layer)
            {
                if (!check(point, size, rotation, points, p => LayerRequirement.IsFulfilled(p)))
                    return false;
            }

            if (GroundOptions != null && GroundOptions.Length > 0)
            {
                if (!check(point, size, rotation, points, p => Dependencies.Get<IMap>().CheckGround(p, GroundOptions)))
                    return false;
            }

            if (Building)
            {
                if (BuildingPoints != null && BuildingPoints.Length > 0)
                {
                    if (!check(point, size, rotation, points, p =>
                    {
                        var building = Dependencies.Get<IBuildingManager>().GetBuilding(p).FirstOrDefault(b => b.Info == Building);
                        if (building == null)
                            return false;
                        return BuildingPoints.Select(bp => building.RotateBuildingPoint(bp)).Contains(p);
                    }))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!check(point, size, rotation, points, p => Dependencies.Get<IBuildingManager>().GetBuilding(p).Any(b => b.Info == Building)))
                        return false;
                }
            }

            return true;
        }

        private bool check(Vector2Int point, Vector2Int size, BuildingRotation rotation, IEnumerable<Vector2Int> points, System.Predicate<Vector2Int> predicate)
        {
            switch (Mode)
            {
                case BuildingRequirementMode.Any: return checkAny(point, size, rotation, Count, predicate);
                case BuildingRequirementMode.AnySpecific: return checkAnySpecific(point, size, rotation, points ?? Points, Count, predicate);
                case BuildingRequirementMode.Average: return checkAverage(point, size, rotation, predicate);
                case BuildingRequirementMode.Specific: return checkSpecific(point, size, rotation, points ?? Points, predicate);
                default:
                case BuildingRequirementMode.All: return checkAll(point, size, predicate);
            }
        }

        private static bool checkAny(Vector2Int point, Vector2Int size, BuildingRotation rotation, int count, System.Predicate<Vector2Int> predicate)
        {
            var fulfilledPoints = 0;
            foreach (var structurePoint in PositionHelper.GetStructurePositions(point, size, rotation))
            {
                if (predicate(structurePoint))
                    fulfilledPoints++;
                if (fulfilledPoints >= count)
                    return true;
            }
            return false;
        }
        private static bool checkAnySpecific(Vector2Int point, Vector2Int size, BuildingRotation rotation, IEnumerable<Vector2Int> points, int count, System.Predicate<Vector2Int> predicate)
        {
            var fulfilledPoints = 0;
            foreach (var structurePoint in points.Select(p => rotation.RotateBuildingPoint(point, p, size)))
            {
                if (predicate(structurePoint))
                    fulfilledPoints++;
                if (fulfilledPoints >= count)
                    return true;
            }
            return false;
        }
        private static bool checkAverage(Vector2Int point, Vector2Int size, BuildingRotation rotation, System.Predicate<Vector2Int> predicate)
        {
            var sum = 0f;
            foreach (var structurePoint in PositionHelper.GetStructurePositions(point, size, rotation))
            {
                if (predicate(structurePoint))
                    sum += 1f;
            }
            return sum / (size.x * size.y) >= 0.5f;
        }
        private static bool checkSpecific(Vector2Int point, Vector2Int size, BuildingRotation rotation, IEnumerable<Vector2Int> points, System.Predicate<Vector2Int> predicate)
        {
            foreach (var structurePoint in points.Select(p => rotation.RotateBuildingPoint(point, p, size)))
            {
                if (!predicate(structurePoint))
                    return false;
            }
            return true;
        }
        private static bool checkAll(Vector2Int point, Vector2Int size, System.Predicate<Vector2Int> predicate)
        {
            foreach (var structurePoint in PositionHelper.GetStructurePositions(point, size))
            {
                if (!predicate(structurePoint))
                    return false;
            }
            return true;
        }
    }
}
