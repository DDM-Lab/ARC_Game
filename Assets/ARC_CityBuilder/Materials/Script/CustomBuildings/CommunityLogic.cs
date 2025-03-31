using CityBuilderCore;
using UnityEngine;
using System.Linq;


public class CommunityLogic : MonoBehaviour
{
    private StorageComponent _peopleStorage;
    private IMap _map;
    private IGridPositions _grid;
    
    public Item peopleItem;
    public float evacuationRatePerSecond = 5f; // people per second

    private void Awake()
    {
        _peopleStorage = GetComponent<StorageComponent>();
        if (_peopleStorage == null)
            Debug.LogError("[CommunityLogic] No StorageComponent found!");
        else
            Debug.Log("[CommunityLogic] StorageComponent found and ready.");
        _map = Dependencies.Get<IMap>();
        _grid = Dependencies.Get<IGridPositions>();
        // Initialize people
        _peopleStorage.Storage.AddItems(new ItemQuantity(peopleItem, _peopleStorage.Storage.GetItemCapacity(peopleItem)));
    }

    private bool IsFlooded()
    {  
        if (_map == null || _grid == null || _peopleStorage == null)
            return false;

        var buildingPoints = _peopleStorage.Building.GetPoints();
        foreach (var point in buildingPoints)
        {
            if (_map is CustomMap customMap && customMap.IsFlood(point))
                return true;
        }
        return false;
    }
    
    public void CheckFlooded()
    {
        var map = Dependencies.Get<IMap>() as CustomMap;
        if (map == null)
        {
            Debug.LogError("[CommunityLogic] Map is not CustomMap!");
            return;
        }

        if (_peopleStorage == null)
        {
            Debug.LogWarning("[CommunityLogic] No people storage found.");
            return;
        }

        var building = _peopleStorage.Building as Building;
        var center = building?.Point ?? Vector2Int.zero;
        var size = building != null ? building.Info.Size : new Vector2Int(1, 1);

        var positions = PositionHelper.GetStructurePositions(center, size);
        foreach (var pos in positions)
        {
            if (map.IsFlood(pos))
            {
                Debug.Log($"[CommunityLogic] Flood detected at {pos}, starting evacuation.");
                EvacuatePeople(Time.deltaTime);
                return;
            }
        }
    }

    public void EvacuatePeople(float deltaTime)
    {
        int toEvacuate = Mathf.FloorToInt(evacuationRatePerSecond * deltaTime);
        int remaining = _peopleStorage.Storage.RemoveItems(peopleItem, toEvacuate);
        if (toEvacuate > remaining)
        {
            Debug.Log($"[CommunityLogic] Evacuated {toEvacuate - remaining} people.");
        }
    }
}