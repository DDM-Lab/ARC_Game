using CityBuilderCore;
using UnityEngine;
using System.Linq;

public class CommunityLogic : MonoBehaviour
{
    private StorageComponent _peopleStorage;
    private IMap _map;
    private IGridPositions _grid;

    public Item peopleItem;
    [Range(0f, 1f)]
    public float floodEvacuationRatio = 0.3f;
    public float evacuationRatePerSecond = 5f;

    private bool _isFloodedMode = false;

    private void Awake()
    {
        _peopleStorage = GetComponent<StorageComponent>();
        if (_peopleStorage == null)
            Debug.LogError("[CommunityLogic] No StorageComponent found!");
        else
            Debug.Log("[CommunityLogic] StorageComponent found and ready.");

        _map = Dependencies.Get<IMap>();
        _grid = Dependencies.Get<IGridPositions>();

        // Initialize with full population
        _peopleStorage.Storage.AddItems(new ItemQuantity(peopleItem, _peopleStorage.Storage.GetItemCapacity(peopleItem)));
    }

    public bool CheckFlooded()
    {
        var map = Dependencies.Get<IMap>() as CustomMap;
        if (map == null)
        {
            Debug.LogError("[CommunityLogic] Map is not CustomMap!");
            return false;
        }

        if (_peopleStorage == null)
        {
            Debug.LogWarning("[CommunityLogic] No people storage found.");
            return false;
        }

        var building = _peopleStorage.Building as Building;
        var center = building?.Point ?? Vector2Int.zero;
        var size = building != null ? building.Info.Size : new Vector2Int(1, 1);

        var positions = PositionHelper.GetStructurePositions(center, size);
        foreach (var pos in positions)
        {
            if (map.IsFlood(pos))
            {
                Debug.Log($"[CommunityLogic] Flood detected at {pos}.");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Call this externally when flood tiles are updated to add/remove evacuation orders.
    /// </summary>
    public void CheckFloodStatusAndUpdateOrder()
    {
        if (_peopleStorage == null || _map == null || _grid == null)
            return;

        var building = _peopleStorage.Building as Building;
        if (building == null)
            return;

        var center = building.Point;
        var size = building.Info.Size;
        var positions = PositionHelper.GetStructurePositions(center, size);

        bool isFlooded = CheckFlooded();

        if (isFlooded && !_isFloodedMode)
        {
            _isFloodedMode = true;
            EnterFloodedMode();
        }
        else if (!isFlooded && _isFloodedMode)
        {
            _isFloodedMode = false;
            ExitFloodedMode();
        }
    }

    public void EnterFloodedMode()
    {
        if (!_peopleStorage.Orders.Any(o => o.Item == peopleItem && o.Mode == StorageOrderMode.Empty))
        {
            var orders = _peopleStorage.Orders.ToList();
            orders.Add(new StorageOrder
            {
                Item = peopleItem,
                Ratio = floodEvacuationRatio,
                Mode = StorageOrderMode.Empty
            });
            _peopleStorage.Orders = orders.ToArray();
            _peopleStorage.InitializeComponent();
            Debug.Log($"[CommunityLogic] {name} entered flooded mode. Evacuation order added.");
        }
    }

    public void ExitFloodedMode()
    {
        var orders = _peopleStorage.Orders
            .Where(o => !(o.Item == peopleItem && o.Mode == StorageOrderMode.Empty))
            .ToArray();

        _peopleStorage.Orders = orders;
        _peopleStorage.InitializeComponent();
        Debug.Log($"[CommunityLogic] {name} exited flooded mode. Evacuation order removed.");
    }
}
