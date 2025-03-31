using CityBuilderCore;
using UnityEngine;
using System.Linq;

public class ShelterLogic : MonoBehaviour
{
    private StorageComponent _storage;

    public Item foodItem; // assign in inspector or script

    private void Awake()
    {
        _storage = GetComponent<StorageComponent>();

        if (_storage == null)
        {
            Debug.LogError($"[ShelterLogic] No StorageComponent found on {name}");
        }
    }

    public void ClearFoodStorage()
    {
        if (_storage == null || _storage.Storage == null)
        {
            Debug.LogWarning($"[ShelterLogic] Cannot consume items: storage not initialized.");
            return;
        }

        var storedItems = _storage.Storage.GetItemQuantities()?.ToList();
        if (storedItems == null || storedItems.Count == 0)
        {
            Debug.Log($"[ShelterLogic] No items to consume in {name}.");
            return;
        }

        foreach (var itemQuantity in storedItems)
        {
            _storage.Storage.RemoveItems(itemQuantity.Item, itemQuantity.Quantity);
            Debug.Log($"[ShelterLogic] Consumed {itemQuantity.Quantity}x {itemQuantity.Item.Key} from {name}.");
        }
    }

    public void GenerateFoodOrderDebug()
    {
        if (_storage == null || foodItem == null)
        {
            Debug.LogWarning($"[ShelterLogic] Cannot generate food order.");
            return;
        }

        // Create new order array
        var orders = new StorageOrder[_storage.Orders.Length + 1];
        _storage.Orders.CopyTo(orders, 0);
        orders[^1] = new StorageOrder
        {
            Item = foodItem,
            Ratio = 1f,
            Mode = StorageOrderMode.Get // actively pull items
        };

        _storage.Orders = orders;

        // 🔄 Refresh the component to reflect the new orders
        _storage.InitializeComponent();

        Debug.Log($"[ShelterLogic] Food order added to {name}.");

        // ✅ Notify all kitchens after adding order when using prepared mode (TBA later)
        //GameDatabase.Instance.NotifyKitchensOfNewOrder();
        
    }

}
