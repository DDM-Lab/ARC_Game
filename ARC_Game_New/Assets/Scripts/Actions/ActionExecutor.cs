using UnityEngine;
using System;
using System.Linq;
using GameActions;

/// <summary>
/// Executes game actions received from external systems
/// Agnostic to source - can be LLM, scripted AI, or automation
/// </summary>
public class ActionExecutor : MonoBehaviour
{
    public static ActionExecutor Instance { get; private set; }

    [Header("System References")]
    public BuildingSystem buildingSystem;
    public WorkerSystem workerSystem;
    public DeliverySystem deliverySystem;

    [Header("Debug")]
    public bool logActions = true;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // Auto-find systems if not assigned
        if (buildingSystem == null) buildingSystem = FindObjectOfType<BuildingSystem>();
        if (workerSystem == null) workerSystem = WorkerSystem.Instance;
        if (deliverySystem == null) deliverySystem = FindObjectOfType<DeliverySystem>();

        if (buildingSystem == null) Debug.LogError("ActionExecutor: BuildingSystem not found!");
        if (workerSystem == null) Debug.LogError("ActionExecutor: WorkerSystem not found!");
        if (deliverySystem == null) Debug.LogError("ActionExecutor: DeliverySystem not found!");
    }

    /// <summary>
    /// Execute a game action
    /// Returns result indicating success/failure
    /// </summary>
    public ActionExecutionResult ExecuteAction(GameAction action)
    {
        if (logActions)
        {
            Debug.Log($"🎮 Executing action: {action.description}");
        }

        try
        {
            switch (action.action_type)
            {
                case "construction":
                    return ExecuteConstruction(action);
                case "worker":
                    return ExecuteWorker(action);
                case "resource_transfer":
                    return ExecuteTransfer(action);
                case "worker_assignment":
                    return ExecuteAssignment(action);
                case "deconstruction":
                    return ExecuteDeconstruction(action);
                default:
                    return Failure(action.action_id, $"Unknown action type: {action.action_type}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Action execution failed: {ex.Message}\n{ex.StackTrace}");
            return Failure(action.action_id, ex.Message);
        }
    }

    // ===== ACTION EXECUTION METHODS =====

    ActionExecutionResult ExecuteConstruction(GameAction action)
    {
        var p = action.construction;

        // Validate parameters
        if (p == null) return Failure(action.action_id, "Missing construction parameters");
        if (buildingSystem == null) return Failure(action.action_id, "BuildingSystem not available");

        // Find site
        AbandonedSite site = FindSiteById(p.site_id);
        if (site == null)
        {
            return Failure(action.action_id, $"Site {p.site_id} not found");
        }
        if (!site.IsAvailable())
        {
            return Failure(action.action_id, $"Site {p.site_id} not available");
        }

        // Parse building type
        BuildingType buildingType = ParseBuildingType(p.building_type);

        // Check budget
        if (!HasBudget(action.cost))
        {
            int currentBudget = SatisfactionAndBudget.Instance.GetCurrentBudget();
            return Failure(action.action_id, $"Insufficient budget (need ${action.cost}, have ${currentBudget})");
        }

        // Execute construction
        buildingSystem.CreateBuildingImmediately(site, buildingType);

        if (logActions)
        {
            Debug.Log($"✅ Built {buildingType} at site {p.site_id} (cost: ${action.cost})");
        }

        return Success(action.action_id);
    }

    ActionExecutionResult ExecuteWorker(GameAction action)
    {
        var p = action.worker;

        if (p == null) return Failure(action.action_id, "Missing worker parameters");
        if (workerSystem == null) return Failure(action.action_id, "WorkerSystem not available");

        if (!HasBudget(action.cost))
        {
            int currentBudget = SatisfactionAndBudget.Instance.GetCurrentBudget();
            return Failure(action.action_id, $"Insufficient budget (need ${action.cost}, have ${currentBudget})");
        }

        switch (p.worker_action_type)
        {
            case "hire_untrained":
                for (int i = 0; i < p.quantity; i++)
                {
                    workerSystem.CreateUntrainedWorker();
                }
                SatisfactionAndBudget.Instance.RemoveBudget(action.cost, $"Hired {p.quantity} untrained workers");

                if (logActions)
                {
                    Debug.Log($"✅ Hired {p.quantity} untrained workers (cost: ${action.cost})");
                }
                break;

            case "hire_trained":
                for (int i = 0; i < p.quantity; i++)
                {
                    workerSystem.CreateTrainedWorker();
                }
                SatisfactionAndBudget.Instance.RemoveBudget(action.cost, $"Hired {p.quantity} trained workers");

                if (logActions)
                {
                    Debug.Log($"✅ Hired {p.quantity} trained workers (cost: ${action.cost})");
                }
                break;

            case "train_untrained":
                // For immediate LLM execution: convert untrained to trained directly
                // Get untrained workers
                var allWorkers = workerSystem.GetAllWorkers();
                var untrainedWorkers = allWorkers.Where(w => w.Type == WorkerType.Untrained && !w.IsWorking).Take(p.quantity).ToList();

                if (untrainedWorkers.Count < p.quantity)
                {
                    return Failure(action.action_id, $"Insufficient untrained workers (need {p.quantity}, have {untrainedWorkers.Count})");
                }

                // Remove untrained workers and create trained ones
                foreach (var worker in untrainedWorkers)
                {
                    workerSystem.RemoveWorker(worker);
                    workerSystem.CreateTrainedWorker();
                }

                SatisfactionAndBudget.Instance.RemoveBudget(action.cost, $"Trained {p.quantity} workers");

                if (logActions)
                {
                    Debug.Log($"✅ Trained {p.quantity} workers (cost: ${action.cost})");
                }
                break;

            default:
                return Failure(action.action_id, $"Unknown worker action: {p.worker_action_type}");
        }

        return Success(action.action_id);
    }

    ActionExecutionResult ExecuteTransfer(GameAction action)
    {
        var p = action.transfer;

        if (p == null) return Failure(action.action_id, "Missing transfer parameters");
        if (deliverySystem == null) return Failure(action.action_id, "DeliverySystem not available");

        // Find buildings
        MonoBehaviour source = FindBuildingByName(p.source_facility);
        MonoBehaviour destination = FindBuildingByName(p.destination_facility);

        if (source == null)
        {
            return Failure(action.action_id, $"Source building not found: {p.source_facility}");
        }
        if (destination == null)
        {
            return Failure(action.action_id, $"Destination building not found: {p.destination_facility}");
        }

        // Parse resource type
        ResourceType resourceType = p.resource_type == "FoodPacks" ? ResourceType.FoodPacks : ResourceType.Population;

        // Create delivery
        var tasks = deliverySystem.CreateDeliveryTask(source, destination, resourceType, p.quantity);

        if (tasks == null || tasks.Count == 0)
        {
            return Failure(action.action_id, "Failed to create delivery (route may be blocked or no vehicles available)");
        }

        if (logActions)
        {
            Debug.Log($"✅ Created transfer: {p.quantity} {p.resource_type} from {p.source_facility} to {p.destination_facility}");
        }

        return Success(action.action_id);
    }

    ActionExecutionResult ExecuteAssignment(GameAction action)
    {
        var p = action.assignment;

        if (p == null) return Failure(action.action_id, "Missing assignment parameters");
        if (workerSystem == null) return Failure(action.action_id, "WorkerSystem not available");

        Building building = FindBuildingByName(p.building_name) as Building;
        if (building == null)
        {
            return Failure(action.action_id, $"Building not found: {p.building_name}");
        }

        // Get building ID (use originalSiteId, not Unity's InstanceID)
        int buildingId = building.GetOriginalSiteId();

        // Use the WorkerSystem's assignment method
        // This method assigns workers automatically (trained first, then untrained)
        bool success = workerSystem.TryAssignWorkersToBuilding(buildingId, p.quantity);

        if (!success)
        {
            return Failure(action.action_id, $"Failed to assign workers to {p.building_name} (insufficient available workers)");
        }

        // Update building status after worker assignment
        // This triggers the building to check its worker count and transition to InUse if fully staffed
        building.UpdateWorkerStatus();

        if (logActions)
        {
            Debug.Log($"✅ Assigned workers to {p.building_name}");
        }

        return Success(action.action_id);
    }

    ActionExecutionResult ExecuteDeconstruction(GameAction action)
    {
        var p = action.deconstruction;

        if (p == null) return Failure(action.action_id, "Missing deconstruction parameters");

        Building building = FindBuildingByName(p.building_name) as Building;
        if (building == null)
        {
            return Failure(action.action_id, $"Building not found for deconstruction: {p.building_name}");
        }

        // Check if building has StartDeconstruction method
        try
        {
            building.StartDeconstruction();

            if (logActions)
            {
                Debug.Log($"✅ Started deconstruction of {p.building_name}");
            }

            return Success(action.action_id);
        }
        catch (Exception ex)
        {
            return Failure(action.action_id, $"Deconstruction not supported or failed: {ex.Message}");
        }
    }

    // ===== HELPER METHODS =====

    AbandonedSite FindSiteById(int siteId)
    {
        AbandonedSite[] sites = FindObjectsOfType<AbandonedSite>();
        return sites.FirstOrDefault(s => s.GetId() == siteId);
    }

    MonoBehaviour FindBuildingByName(string buildingName)
    {
        // Try exact match first
        Building[] buildings = FindObjectsOfType<Building>();
        Building building = buildings.FirstOrDefault(b => b.name == buildingName);
        if (building != null) return building;

        // Try partial match
        building = buildings.FirstOrDefault(b => b.name.Contains(buildingName));
        if (building != null) return building;

        // Check prebuilt buildings
        PrebuiltBuilding[] prebuilt = FindObjectsOfType<PrebuiltBuilding>();
        PrebuiltBuilding prebuiltBuilding = prebuilt.FirstOrDefault(p => p.name == buildingName);
        if (prebuiltBuilding != null) return prebuiltBuilding;

        // Try partial match for prebuilt
        prebuiltBuilding = prebuilt.FirstOrDefault(p => p.name.Contains(buildingName));
        return prebuiltBuilding;
    }

    BuildingType ParseBuildingType(string typeString)
    {
        return typeString switch
        {
            "Kitchen" => BuildingType.Kitchen,
            "Shelter" => BuildingType.Shelter,
            "CaseworkSite" => BuildingType.CaseworkSite,
            _ => BuildingType.Kitchen
        };
    }

    bool HasBudget(int cost)
    {
        return SatisfactionAndBudget.Instance != null &&
               SatisfactionAndBudget.Instance.GetCurrentBudget() >= cost;
    }

    ActionExecutionResult Success(string actionId)
    {
        return new ActionExecutionResult
        {
            success = true,
            action_id = actionId,
            error_message = null,
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    ActionExecutionResult Failure(string actionId, string error)
    {
        if (logActions)
        {
            Debug.LogWarning($"❌ Action {actionId} failed: {error}");
        }

        return new ActionExecutionResult
        {
            success = false,
            action_id = actionId,
            error_message = error,
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }
}
