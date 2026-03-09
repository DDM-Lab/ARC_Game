using UnityEngine;
using System.Collections.Generic;

namespace GameActions
{
    /// <summary>
    /// Base class for all executable game actions
    /// Actions are received as JSON from external systems (WebSocket)
    /// Agnostic to source - can be LLM, scripted AI, or automation
    /// </summary>
    [System.Serializable]
    public class GameAction
    {
        public string action_id;
        public string action_type; // construction, worker, resource_transfer, worker_assignment, deconstruction
        public string description;
        public int cost;

        // Action-specific parameters (populated based on action_type)
        public ConstructionParams construction;
        public WorkerParams worker;
        public TransferParams transfer;
        public AssignmentParams assignment;
        public DeconstructionParams deconstruction;
    }

    /// <summary>
    /// Parameters for construction actions
    /// </summary>
    [System.Serializable]
    public class ConstructionParams
    {
        public string building_type; // Kitchen, Shelter, CaseworkSite
        public int site_id;
        public string site_name;
    }

    /// <summary>
    /// Parameters for worker actions (hiring, training)
    /// </summary>
    [System.Serializable]
    public class WorkerParams
    {
        public string worker_action_type; // hire_untrained, hire_trained, train_untrained
        public int quantity;
    }

    /// <summary>
    /// Parameters for resource transfer actions
    /// </summary>
    [System.Serializable]
    public class TransferParams
    {
        public string resource_type; // FoodPacks, Population
        public int quantity;
        public string source_facility;
        public string destination_facility;
    }

    /// <summary>
    /// Parameters for worker assignment actions
    /// </summary>
    [System.Serializable]
    public class AssignmentParams
    {
        public string building_name;
        public string worker_type; // trained, untrained
        public int quantity;
    }

    /// <summary>
    /// Parameters for deconstruction actions
    /// </summary>
    [System.Serializable]
    public class DeconstructionParams
    {
        public string building_name;
        public int frees_site_id;
    }

    /// <summary>
    /// Message format for receiving actions from WebSocket
    /// </summary>
    [System.Serializable]
    public class ActionMessage
    {
        public string type = "execute_action";
        public GameAction action;
        public string timestamp;
    }

    /// <summary>
    /// Response sent back after action execution
    /// </summary>
    [System.Serializable]
    public class ActionExecutionResult
    {
        public bool success;
        public string action_id;
        public string error_message;
        public string timestamp;
    }
}
