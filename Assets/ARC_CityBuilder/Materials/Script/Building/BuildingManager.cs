using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public class BuildingManager : MonoBehaviour
{
    public static BuildingManager Instance;

    private List<BuildingComponent> buildings = new();
    public Tilemap floodTilemap; // Assign the flooded area tilemap in the inspector

    private void Awake()
    {
        Instance = this;
        RegisterSceneBuildings();
    }

    private void RegisterSceneBuildings()
    {
        BuildingComponent[] sceneBuildings = GameObject.FindObjectsOfType<BuildingComponent>();
        buildings.AddRange(sceneBuildings);
        Debug.Log($"[BuildingManager] Registered {buildings.Count} buildings from scene.");
    }

    public void CheckFloodingAndTriggerEvents()
    {
        foreach (var building in buildings)
        {
            Bounds bounds;

            // Use collider or renderer to get bounds
            var collider = building.GetComponent<Collider2D>();
            if (collider != null)
            {
                bounds = collider.bounds;
            }
            else if (building.GetComponent<Renderer>() != null)
            {
                bounds = building.GetComponent<Renderer>().bounds;
            }
            else
            {
                Debug.LogWarning($"[BuildingManager] {building.name} has no collider or renderer. Skipping.");
                continue;
            }

            Vector3Int min = floodTilemap.WorldToCell(bounds.min);
            Vector3Int max = floodTilemap.WorldToCell(bounds.max);

            bool touchedFlood = false;

            for (int x = min.x; x <= max.x && !touchedFlood; x++)
            {
                for (int y = min.y; y <= max.y && !touchedFlood; y++)
                {
                    Vector3Int tilePos = new Vector3Int(x, y, 0);
                    if (floodTilemap.HasTile(tilePos))
                    {
                        touchedFlood = true;

                        // Only trigger if this is a new flood contact
                        if (!building.isFlooded)
                        {
                            building.isFlooded = true;
                            building.TriggerEvent(new EvacuationEvent());
                            Debug.Log($"[BuildingManager] {building.buildingName} is now flooded and evacuation triggered.");
                        }
                    }
                }
            }

            // If flood was not detected this round, reset flag
            if (!touchedFlood && building.isFlooded)
            {
                building.isFlooded = false;
                building.isEvacuated = false; // Optional: reset evacuation status
                building.assignedTasks.Clear(); // Optional: clear tasks if flood recedes
                Debug.Log($"[BuildingManager] {building.buildingName} is no longer flooded.");
            }
        }
    }


}
