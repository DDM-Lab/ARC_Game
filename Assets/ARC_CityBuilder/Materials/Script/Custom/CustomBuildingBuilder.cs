using System.Collections.Generic;
using UnityEngine;
using CityBuilderCore;

public class CustomBuildingBuilder : BuildingBuilder
{
    protected override void build(IEnumerable<Vector2Int> points)
    {
        var buildingManager = Dependencies.Get<IBuildingManager>();

        if (points == null || buildingManager == null || BuildingInfo == null || _gridPositions == null)
        {
            Debug.LogError("[CustomBuilder] Missing dependencies or points were null.");
            return;
        }

        foreach (var point in points)
        {
            if (_globalStorage != null && BuildingInfo.Cost != null)
            {
                foreach (var item in BuildingInfo.Cost)
                {
                    _globalStorage.Items.RemoveItems(item.Item, item.Quantity);
                }
            }

            BuildingInfo.Prepare(point, _rotation);

            // Calculate final position and rotation
            var worldPos = _gridPositions.GetWorldPosition(_rotation.RotateOrigin(point, BuildingInfo.Size));
            var rotation = _rotation.GetRotation();

            // Build the building
            var prefab = BuildingInfo.GetPrefab(_index);
            if (prefab == null)
            {
                Debug.LogError("[CustomBuilder] Building prefab is null.");
                continue;
            }

            var ibuilding = buildingManager.Add(worldPos, rotation, prefab);
            if (ibuilding == null)
            {
                Debug.LogError("[CustomBuilder] Failed to add building to manager.");
                continue;
            }

            var building = ibuilding as Building;
            if (building == null)
            {
                Debug.LogError("[CustomBuilder] Failed to cast IBuilding to Building.");
                continue;
            }

            // Register based on type
            if (building.TryGetComponent<ShelterLogic>(out var shelterLogic))
            {
                GameDatabase.Instance.RegisterShelter(building);
                Debug.Log($"[CustomBuilder] Registered shelter at {building.transform.position}");
            }
            else if (building.name.Contains("Kitchen")) // You can add tags or identifiers instead
            {
                GameDatabase.Instance.RegisterKitchen(building);
                Debug.Log($"[CustomBuilder] Registered kitchen at {building.transform.position}");
            }
            else
            {
                GameDatabase.Instance.RegisterGeneric(building);
                Debug.Log($"[CustomBuilder] Registered generic building at {building.transform.position}");
            }

            Built?.Invoke(building);
            _index++;
        }

        onApplied();
    }
}
