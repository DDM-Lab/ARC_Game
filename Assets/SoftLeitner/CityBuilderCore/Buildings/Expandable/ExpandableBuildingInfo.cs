using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// special info for buildings that can have variable sizes<br/>
    /// for example bridges in THREE, fields and storage areas in TOWN demo
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_expandable_building_info.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(ExpandableBuildingInfo))]
    public class ExpandableBuildingInfo : BuildingInfo
    {
        [Tooltip("Minumum allowed size of expansion, Y has to be 0 for linear expansion")]
        public Vector2Int ExpansionMinimum;
        [Tooltip("Maximum allowed size of expansion, Y has to be 0 for linear expansion")]
        public Vector2Int ExpansionMaximum;
        [Tooltip("Items to be subtracted from GlobalStorage for building per expansion")]
        public ItemQuantity[] ExpansionCost;
        [Tooltip("Regular size ist counted only before the expansion, use this field for size added after expansion, for linear expansion y should be 0 if it stays the same")]
        public Vector2Int SizePost;

        public bool IsArea => ExpansionMaximum.y > 0;

        public virtual bool CheckExpansionLimits(Vector2Int expansion)
        {
            if (expansion.x < ExpansionMinimum.x || expansion.y < ExpansionMinimum.y)
                return false;
            if (expansion.x > ExpansionMaximum.x || expansion.y > ExpansionMaximum.y)
                return false;
            return true;
        }

        public virtual bool CheckExpandedRequirements(Vector2Int point, Vector2Int expansion, BuildingRotation rotation) => CheckExpandedBuildingRequirements(point, expansion, rotation) && CheckExpandedRoadRequirements(point, expansion, rotation);
        public virtual bool CheckExpandedBuildingRequirements(Vector2Int point, Vector2Int expansion, BuildingRotation rotation)
        {
            return BuildingRequirements == null || BuildingRequirements.Length == 0 ||
                BuildingRequirements.All(r => r.IsFulfilled(point, Size + expansion + SizePost, rotation, r.Points.SelectMany(p => expandPoint(p, expansion))));
        }
        public virtual bool CheckExpandedRoadRequirements(Vector2Int point, Vector2Int expansion, BuildingRotation rotation)
        {
            if (RoadRequirements == null || RoadRequirements.Length == 0)
                return true;

            foreach (var roadRequirement in RoadRequirements)
            {
                if (expandPoint(roadRequirement.Point, expansion).Any(p => !Dependencies.Get<IRoadManager>().CheckRequirement(rotation.RotateBuildingPoint(point, p, Size + expansion + SizePost), roadRequirement)))
                    return false;
            }

            return true;
        }

        public virtual void PrepareExpanded(Vector2Int point, Vector2Int expansion, BuildingRotation rotation)
        {
            var size = Size + expansion + SizePost;

            foreach (var roadRequirement in RoadRequirements.Where(r => r.Amend && r.Road))
            {
                foreach (var roadPoint in expandPoint(roadRequirement.Point, expansion))
                {
                    Dependencies.Get<IRoadManager>().Add(new[] { rotation.RotateBuildingPoint(point, roadPoint, size) }, roadRequirement.Road);
                }
            }
        }

        private IEnumerable<Vector2Int> expandPoint(Vector2Int buildingPoint, Vector2Int expansion)
        {
            if (buildingPoint.x < Size.x)//start
            {
                yield return buildingPoint;
            }
            else if (buildingPoint.x == Size.x)//expansion
            {
                for (int i = 0; i < expansion.x; i++)
                {
                    yield return new Vector2Int(Size.x + i, 0);
                }
            }
            else//end
            {
                yield return buildingPoint + new Vector2Int(expansion.x - 1, 0);
            }
        }

        public override IBuilding Create(DefaultBuildingManager.BuildingMetaData metaData, Transform parent)
        {
            var expansion = JsonUtility.FromJson<ExpandableBuilding.ExpandableBuildingData>(metaData.Data).Expansion;

            var size = Size + expansion + SizePost;
            var rotation = BuildingRotation.Create(metaData.Rotation);
            var building = Instantiate((ExpandableBuilding)GetPrefab(metaData.Index), Dependencies.Get<IGridPositions>().GetWorldPosition(rotation.RotateOrigin(metaData.Point, size)), rotation.GetRotation(), parent);
            building.Expansion = expansion;
            building.Initialize();
            building.Id = new Guid(metaData.Id);

            return building;
        }
    }
}
