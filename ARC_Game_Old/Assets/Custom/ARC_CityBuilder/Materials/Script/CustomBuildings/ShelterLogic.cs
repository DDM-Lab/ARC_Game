using CityBuilderCore;
using UnityEngine;
using System.Linq;

public class ShelterLogic : MonoBehaviour
{
    [Header("Shelter Configuration")]
    [SerializeField] private bool isOperational = true;
    [SerializeField] private int minimumFoodRequired = 5;
    [SerializeField] private int maxOccupants = 20;
    [SerializeField] private int currentOccupants = 0;
    
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
    /// Check if shelter is operational
    /// </summary>
    public bool IsOperational()
    {
        return isOperational && gameObject.activeInHierarchy;
    }

    /// <summary>
    /// Check if shelter needs food delivery
    /// </summary>
    public bool NeedsFood()
    {
        if (!IsOperational() || _storage?.Storage == null || foodItem == null)
            return false;

        int currentFood = _storage.Storage.GetItemQuantity(foodItem);
        return currentFood < minimumFoodRequired;
    }

    /// <summary>
    /// Add food to the shelter
    /// </summary>
    public bool AddFood(int amount)
    {
        if (_storage?.Storage == null || foodItem == null)
        {
            Debug.LogWarning($"[ShelterLogic] Cannot add food: storage or foodItem not set.");
            return false;
        }

        int added = _storage.Storage.AddItems(foodItem, amount);
        Debug.Log($"[ShelterLogic] Added {added}x {foodItem.Key} to {name}. Total: {_storage.Storage.GetItemQuantity(foodItem)}");
        return added > 0;
    }

    /// <summary>
    /// Get current food amount
    /// </summary>
    public int GetCurrentFood()
    {
        if (_storage?.Storage == null || foodItem == null)
            return 0;

        return _storage.Storage.GetItemQuantity(foodItem);
    }

    /// <summary>
    /// Get current number of people in shelter
    /// </summary>
    public int GetCurrentOccupants()
    {
        if (_storage?.Storage == null || peopleItem == null)
            return currentOccupants;

        return _storage.Storage.GetItemQuantity(peopleItem);
    }

    /// <summary>
    /// Set shelter operational status
    /// </summary>
    public void SetOperational(bool operational)
    {
        isOperational = operational;
        Debug.Log($"[ShelterLogic] {name} operational status set to: {operational}");
    }

    /// <summary>
    /// Set the number of occupants (for manual control)
    /// </summary>
    public void SetOccupants(int occupants)
    {
        currentOccupants = Mathf.Clamp(occupants, 0, maxOccupants);
        Debug.Log($"[ShelterLogic] {name} occupants set to: {currentOccupants}");
    }

    /// <summary>
    /// Check if shelter is at capacity
    /// </summary>
    public bool IsAtCapacity()
    {
        return GetCurrentOccupants() >= maxOccupants;
    }

    /// <summary>
    /// Get food percentage (0-1)
    /// </summary>
    public float GetFoodPercentage()
    {
        if (_storage?.Storage == null || foodItem == null)
            return 0f;

        int maxCapacity = _storage.Storage.GetItemCapacity(foodItem);
        if (maxCapacity <= 0) return 0f;

        return (float)GetCurrentFood() / maxCapacity;
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
