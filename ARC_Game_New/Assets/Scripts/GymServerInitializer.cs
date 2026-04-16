using UnityEngine;

/// <summary>
/// Auto-initializer for GymServerManager.
/// Attach this to any existing GameObject in the scene, or it will create its own.
/// This script checks if GymServerManager exists, and if not, creates it.
/// </summary>
[DefaultExecutionOrder(-100)] // Execute before most other scripts
public class GymServerInitializer : MonoBehaviour
{
    void Awake()
    {
        // Check if GymServerManager already exists
        if (GymServerManager.Instance == null)
        {
            // Create a new GameObject with GymServerManager
            GameObject gymServerObj = new GameObject("GymServerManager");
            gymServerObj.AddComponent<GymServerManager>();

            Debug.Log("[GymServerInitializer] Created GymServerManager instance");
        }
        else
        {
            Debug.Log("[GymServerInitializer] GymServerManager already exists");
        }
    }
}
