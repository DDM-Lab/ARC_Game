using UnityEngine;
using System;
using System.Collections.Generic;
using GameActions;

/// <summary>
/// Headless Game Controller - Interface wrapper for Python/external control
///
/// This class provides a clean encapsulation interface between Python (via pythonnet)
/// and the existing Unity game systems. It does NOT modify game logic - it simply
/// routes calls to the appropriate singleton systems.
///
/// Purpose:
/// - Single source of truth: Unity C# game logic is authoritative
/// - Reduce technical debt: C# developers change game as normal
/// - Clean interface: New systems just need to be added here
/// - Headless capable: Can run without Unity Editor/Player
///
/// Usage (from Python via pythonnet):
///     controller = HeadlessGameController()
///     controller.Initialize()
///     state_json = controller.GetGameStateJson()
///     success = controller.ExecuteActionJson(action_json)
/// </summary>
public class HeadlessGameController
{
    // System references (Unity singletons)
    private ActionExecutor actionExecutor;
    private TaskSystem taskSystem;
    private WorkerSystem workerSystem;
    private BuildingSystem buildingSystem;
    private DeliverySystem deliverySystem;
    private GlobalClock globalClock;
    private SatisfactionAndBudget satisfactionAndBudget;

    // Initialization state
    private bool isInitialized = false;

    // Logging
    public Action<string> LogAction { get; set; }
    public Action<string> LogWarningAction { get; set; }
    public Action<string> LogErrorAction { get; set; }

    /// <summary>
    /// Initialize the controller and find all necessary Unity systems
    /// Call this once after instantiation
    /// </summary>
    public void Initialize()
    {
        if (isInitialized)
        {
            LogWarning("HeadlessGameController already initialized");
            return;
        }

        Log("Initializing HeadlessGameController...");

        // Find all singleton systems
        actionExecutor = ActionExecutor.Instance;
        taskSystem = TaskSystem.Instance;
        workerSystem = WorkerSystem.Instance;
        buildingSystem = UnityEngine.Object.FindObjectOfType<BuildingSystem>();
        deliverySystem = UnityEngine.Object.FindObjectOfType<DeliverySystem>();
        globalClock = GlobalClock.Instance;
        satisfactionAndBudget = SatisfactionAndBudget.Instance;

        // Validate critical systems
        if (actionExecutor == null) LogError("ActionExecutor not found!");
        if (taskSystem == null) LogError("TaskSystem not found!");
        if (workerSystem == null) LogError("WorkerSystem not found!");
        if (buildingSystem == null) LogError("BuildingSystem not found!");
        if (globalClock == null) LogError("GlobalClock not found!");
        if (satisfactionAndBudget == null) LogError("SatisfactionAndBudget not found!");

        isInitialized = true;
        Log("HeadlessGameController initialized successfully");
    }

    /// <summary>
    /// Get the full game state as a GameStatePayload object
    /// </summary>
    public GameStatePayload GetGameState(int taskId = -1)
    {
        if (!isInitialized)
        {
            LogError("Controller not initialized. Call Initialize() first.");
            return null;
        }

        if (taskSystem == null)
        {
            LogError("TaskSystem not available");
            return null;
        }

        // Use TaskSystem's existing method to build game state
        return taskSystem.GetCurrentGameState(taskId);
    }

    /// <summary>
    /// Get the game state as JSON string (for Python interop)
    /// </summary>
    public string GetGameStateJson(int taskId = -1)
    {
        GameStatePayload state = GetGameState(taskId);
        if (state == null) return "{}";

        // Serialize to JSON
        return JsonUtility.ToJson(state, prettyPrint: false);
    }

    /// <summary>
    /// Execute a game action
    /// </summary>
    public ActionExecutionResult ExecuteAction(GameAction action)
    {
        if (!isInitialized)
        {
            LogError("Controller not initialized. Call Initialize() first.");
            return new ActionExecutionResult
            {
                success = false,
                action_id = action?.action_id ?? "unknown",
                error_message = "Controller not initialized"
            };
        }

        if (actionExecutor == null)
        {
            LogError("ActionExecutor not available");
            return new ActionExecutionResult
            {
                success = false,
                action_id = action?.action_id ?? "unknown",
                error_message = "ActionExecutor not available"
            };
        }

        Log($"Executing action: {action.description}");
        return actionExecutor.ExecuteAction(action);
    }

    /// <summary>
    /// Execute a game action from JSON string (for Python interop)
    /// </summary>
    public string ExecuteActionJson(string actionJson)
    {
        try
        {
            // Parse JSON to GameAction
            GameAction action = JsonUtility.FromJson<GameAction>(actionJson);

            if (action == null)
            {
                LogError("Failed to parse action JSON");
                return JsonUtility.ToJson(new ActionExecutionResult
                {
                    success = false,
                    action_id = "unknown",
                    error_message = "Failed to parse action JSON"
                });
            }

            // Execute action
            ActionExecutionResult result = ExecuteAction(action);

            // Return result as JSON
            return JsonUtility.ToJson(result);
        }
        catch (Exception ex)
        {
            LogError($"Error executing action from JSON: {ex.Message}");
            return JsonUtility.ToJson(new ActionExecutionResult
            {
                success = false,
                action_id = "unknown",
                error_message = ex.Message
            });
        }
    }

    /// <summary>
    /// Execute multiple actions in sequence
    /// Returns list of execution results
    /// </summary>
    public List<ActionExecutionResult> ExecuteActions(List<GameAction> actions)
    {
        List<ActionExecutionResult> results = new List<ActionExecutionResult>();

        foreach (GameAction action in actions)
        {
            ActionExecutionResult result = ExecuteAction(action);
            results.Add(result);

            // Stop on first failure
            if (!result.success)
            {
                LogWarning($"Action {action.action_id} failed, stopping execution chain");
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Get current satisfaction score
    /// </summary>
    public int GetSatisfaction()
    {
        if (satisfactionAndBudget == null) return 0;
        return (int)satisfactionAndBudget.GetCurrentSatisfaction();
    }

    /// <summary>
    /// Get current budget
    /// </summary>
    public int GetBudget()
    {
        if (satisfactionAndBudget == null) return 0;
        return satisfactionAndBudget.GetCurrentBudget();
    }

    /// <summary>
    /// Get current day
    /// </summary>
    public int GetCurrentDay()
    {
        if (globalClock == null) return 1;
        return globalClock.GetCurrentDay();
    }

    /// <summary>
    /// Get current time segment (1-4, represents rounds)
    /// </summary>
    public int GetCurrentRound()
    {
        if (globalClock == null) return 1;
        return globalClock.GetCurrentTimeSegment();
    }

    /// <summary>
    /// Advance time by one round
    /// </summary>
    public void AdvanceTime()
    {
        if (globalClock == null)
        {
            LogError("GlobalClock not available");
            return;
        }

        // Trigger round progression
        // Note: This assumes GlobalClock has a method to manually advance
        // If not, you may need to add one
        Log("Advancing time by one round");
        // globalClock.AdvanceRound(); // Uncomment if this method exists
    }

    /// <summary>
    /// Reset the game to initial state
    /// </summary>
    public void ResetGame()
    {
        Log("Resetting game state...");

        // This would need to call reset methods on each system
        // For now, just log a warning that it's not fully implemented
        LogWarning("ResetGame() not fully implemented - requires system reset methods");

        // TODO: Add reset methods to each system and call them here
        // Example:
        // if (workerSystem != null) workerSystem.Reset();
        // if (buildingSystem != null) buildingSystem.Reset();
        // if (satisfactionAndBudget != null) satisfactionAndBudget.Reset();
    }

    // ===== LOGGING HELPERS =====

    private void Log(string message)
    {
        if (LogAction != null)
        {
            LogAction(message);
        }
        else
        {
            Debug.Log($"[HeadlessGameController] {message}");
        }
    }

    private void LogWarning(string message)
    {
        if (LogWarningAction != null)
        {
            LogWarningAction(message);
        }
        else
        {
            Debug.LogWarning($"[HeadlessGameController] {message}");
        }
    }

    private void LogError(string message)
    {
        if (LogErrorAction != null)
        {
            LogErrorAction(message);
        }
        else
        {
            Debug.LogError($"[HeadlessGameController] {message}");
        }
    }

    /// <summary>
    /// Get system health status (for debugging)
    /// </summary>
    public string GetSystemStatus()
    {
        return $@"Headless Game Controller Status:
- Initialized: {isInitialized}
- ActionExecutor: {(actionExecutor != null ? "OK" : "MISSING")}
- TaskSystem: {(taskSystem != null ? "OK" : "MISSING")}
- WorkerSystem: {(workerSystem != null ? "OK" : "MISSING")}
- BuildingSystem: {(buildingSystem != null ? "OK" : "MISSING")}
- DeliverySystem: {(deliverySystem != null ? "OK" : "MISSING")}
- GlobalClock: {(globalClock != null ? "OK" : "MISSING")}
- SatisfactionAndBudget: {(satisfactionAndBudget != null ? "OK" : "MISSING")}";
    }
}
