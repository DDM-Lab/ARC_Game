using CityBuilderCore;
using UnityEngine;
using System.Linq;

public class ShelterLogic : MonoBehaviour
{
    private StorageComponent _storage;

    public Item foodItem; // assign in inspector or script
    public Item peopleItem; // assign in inspector or script
    private void Awake()
    {
        _storage = GetComponent<StorageComponent>();

        if (_storage == null)
        {
            Debug.LogError($"[ShelterLogic] No StorageComponent found on {name}");
        }
    }

    /// <summary>
    /// Clears only food items (not people).
    /// </summary>
    public void ClearFoodStorage()
    {
        if (_storage?.Storage == null || foodItem == null)
        {
            Debug.LogWarning($"[ShelterLogic] Cannot clear food: storage or foodItem not set.");
            return;
        }

        int removed = _storage.Storage.RemoveItems(foodItem, _storage.Storage.GetItemQuantity(foodItem));
        Debug.Log($"[ShelterLogic] Cleared {removed}x {foodItem.Key} from {name}.");
    }

    /// <summary>
    /// Adds or updates food order without duplicating.
    /// </summary>
    public void GenerateFoodOrderDebug()
    {
        if (_storage == null || foodItem == null)
        {
            Debug.LogWarning($"[ShelterLogic] Cannot generate food order.");
            return;
        }

        bool found = false;

        // Check if a food order already exists
        for (int i = 0; i < _storage.Orders.Length; i++)
        {
            if (_storage.Orders[i].Item == foodItem)
            {
                _storage.Orders[i].Mode = StorageOrderMode.Get;
                _storage.Orders[i].Ratio = 1f;
                found = true;
                break;
            }
        }

        if (!found)
        {
            var orders = _storage.Orders.ToList();
            orders.Add(new StorageOrder
            {
                Item = foodItem,
                Ratio = 1f,
                Mode = StorageOrderMode.Get
            });
            _storage.Orders = orders.ToArray();
        }

        Debug.Log($"[ShelterLogic] Food order {(found ? "updated" : "added")} for {name}.");
        BuildingSystem.Instance?.NotifyKitchensOfNewOrder();
    }

}
