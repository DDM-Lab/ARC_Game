using UnityEngine;

public class RegisterBuiltBuilding : MonoBehaviour
{
    public void OnBuildingBuilt(CityBuilderCore.Building building)
    {
        GameObject go = building.gameObject;

        var comp = go.GetComponent<BuildingComponent>();
        if (comp != null)
        {
            BuildingDatabase.Instance.RegisterBuilding(comp);
            Debug.Log($"[RegisterBuiltBuilding] ✅ Registered {comp.buildingName} at {go.transform.position}");
        }
        else
        {
            Debug.LogWarning("[RegisterBuiltBuilding] Built building has no BuildingComponent.");
        }
    }
    
}
