using CityBuilderCore;
using UnityEngine;

[CreateAssetMenu(menuName = "CityBuilder/CustomBuildingInfo")]
public class CustomBuildingInfo : BuildingInfo
{
    public override bool CheckRequirements(Vector2Int point, BuildingRotation rotation)
    {
        //Debug.Log("🟡 [CustomBuildingInfo] CheckRequirements called!");
        return base.CheckRequirements(point, rotation);
    }

    public override bool CheckBuildingRequirements(Vector2Int point, BuildingRotation rotation)
    {
        //Debug.Log("🟠 [CustomBuildingInfo] CheckBuildingRequirements called!");
        return base.CheckBuildingRequirements(point, rotation);
    }

    public override bool CheckRoadRequirements(Vector2Int point, BuildingRotation rotation)
    {
        //Debug.Log("🔵 [CustomBuildingInfo] CheckRoadRequirements called!");
        return base.CheckRoadRequirements(point, rotation);
    }

    public override bool CheckAvailability(Vector2Int point)
    {
        //Debug.Log("🟢 [CustomBuildingInfo] CheckAvailability called!");
        return base.CheckAvailability(point);
    }

    public override void Prepare(Vector2Int point, BuildingRotation rotation)
    {
        Debug.Log("🟣 [CustomBuildingInfo] Prepare called!");
        base.Prepare(point, rotation);
    }

    public override IBuilding Create(DefaultBuildingManager.BuildingMetaData metaData, Transform parent)
    {
        Debug.Log("🔴 [CustomBuildingInfo] Create called! This confirms we’re using the correct BuildingInfo.");

        var rotation = BuildingRotation.Create(metaData.Rotation);
        var building = Instantiate(
            GetPrefab(metaData.Index),
            Dependencies.Get<IGridPositions>().GetWorldPosition(rotation.RotateOrigin(metaData.Point, Size)),
            rotation.GetRotation(),
            parent
        );

        building.Initialize();
        building.Id = new System.Guid(metaData.Id);

        Debug.Log($"[CustomBuildingInfo] ✅ 建筑已建成：{Name}，位置：{building.transform.position}");

        return building;
    }
}
