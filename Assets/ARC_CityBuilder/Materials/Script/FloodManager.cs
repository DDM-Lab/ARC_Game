using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class FloodManager : MonoBehaviour
{
    public Tilemap riverTilemap;  // River tilemap reference
    public Tilemap floodedTilemap; // Flooded tilemap reference
    public TileBase floodedTile;   // Darker water tile to indicate flooding
    public Tilemap mountainTilemap; // Mountain tilemap to reduce flooding probability
    public Tilemap forestTilemap;   // Forest tilemap to reduce flooding probability
    public float floodChance = 0.3f; // Probability per round of flooding neighboring tiles
    public float recedeChance = 0.1f; // Probability of flood receding per round
    public bool Flood_Debug = true; // Set to true to enable debug logs

    private HashSet<Vector3Int> riverTiles = new HashSet<Vector3Int>(); // Stores river tiles
    private Dictionary<Vector3Int, int> floodedTiles = new Dictionary<Vector3Int, int>(); // Stores flood status
    public Dictionary<Vector3Int, int> waterBodySizes = new Dictionary<Vector3Int, int>(); // Stores water body sizes
    public HashSet<Vector3Int> largeWaterBodies = new HashSet<Vector3Int>(); // Large water body tiles

    private const int TERRAIN_RADIUS = 5; // Radius to check for mountains/forests
    private const float MAX_TERRAIN_WEIGHT = 0.99f; // Max reduction from terrain (e.g., 60%)

    private void Start()
    {
        InitializeRiverTiles();
        IdentifyWaterBodies();
    }

    private void InitializeRiverTiles()
    {
        // Loop through the tilemap and find all river tiles
        BoundsInt bounds = riverTilemap.cellBounds;
        foreach (Vector3Int pos in bounds.allPositionsWithin)
        {
            if (riverTilemap.HasTile(pos))
            {
                riverTiles.Add(pos);
            }
        }
        DebugLog("River tiles count: " + riverTiles.Count);
    }

    private void IdentifyWaterBodies()
    {
        HashSet<Vector3Int> visited = new HashSet<Vector3Int>();

        foreach (Vector3Int startTile in riverTiles)
        {
            if (!visited.Contains(startTile))
            {
                List<Vector3Int> waterBody = new List<Vector3Int>();
                Queue<Vector3Int> queue = new Queue<Vector3Int>();
                queue.Enqueue(startTile);
                visited.Add(startTile);

                while (queue.Count > 0)
                {
                    Vector3Int current = queue.Dequeue();
                    waterBody.Add(current);

                    foreach (Vector3Int neighbor in GetNeighbors(current))
                    {
                        if (riverTiles.Contains(neighbor) && !visited.Contains(neighbor))
                        {
                            queue.Enqueue(neighbor);
                            visited.Add(neighbor);
                        }
                    }
                }

                // If the water body is large, mark all tiles in it
                if (waterBody.Count >= 100)
                {
                    foreach (Vector3Int tile in waterBody)
                    {
                        largeWaterBodies.Add(tile);
                    }
                }

                // Store the size for each tile
                foreach (Vector3Int tile in waterBody)
                {
                    waterBodySizes[tile] = waterBody.Count;
                }
            }
        }
    }

    public (float recedeChance, float spreadChance, float floodChance) GetFloodChances(Vector3Int tilePosition)
    {
        GlobalEnums.WeatherType currentWeather = GlobalManager.Instance.currentWeather;

        float baseFloodChance, baseSpreadChance, baseRecedeChance;

        switch (currentWeather)
        {
            case GlobalEnums.WeatherType.Sunny:
                baseFloodChance = 0.01f;
                baseSpreadChance = 0.01f;
                baseRecedeChance = 0.65f;
                break;
            case GlobalEnums.WeatherType.Rainy:
                baseFloodChance = 0.2f;
                baseSpreadChance = 0.2f;
                baseRecedeChance = 0.05f;
                break;
            case GlobalEnums.WeatherType.Stormy:
                baseFloodChance = 0.3f;
                baseSpreadChance = 0.3f;
                baseRecedeChance = 0.01f;
                break;
            default:
                baseFloodChance = 0.1f;
                baseSpreadChance = 0.1f;
                baseRecedeChance = 0.1f;
                break;
        }

        // Only large water bodies trigger flooding events
        bool isLargeWaterBody = largeWaterBodies.Contains(tilePosition);

        if (isLargeWaterBody)
        {
            baseFloodChance *= 2.0f;  // Higher flood chance
            baseSpreadChance *= 2.0f;
        }
        else
        {
            baseFloodChance *= 0.1f;  // Lower flood chance
            baseSpreadChance *= 0.1f;
        }

        int nearbyMountainForestTiles = CountNearbyMountainsForests(tilePosition, TERRAIN_RADIUS);
        float terrainWeight = Mathf.Clamp01(nearbyMountainForestTiles / 3f) * MAX_TERRAIN_WEIGHT; // Scale impact
        /*bool unsafeTerrain = IsFarFromMountainsOrForests(tilePosition, TERRAIN_RADIUS);
        if (unsafeTerrain)
        {
            DebugLog("Unsafe terrain detected at " + tilePosition);
            baseFloodChance = baseFloodChance * 5.0f;
            baseSpreadChance = baseSpreadChance * 5.0f; 
            baseRecedeChance = baseRecedeChance * 0.5f; 
        }
        else
        {
            baseFloodChance = baseFloodChance * 0.01f;
            baseSpreadChance = baseSpreadChance * 0.01f;
            baseRecedeChance = baseRecedeChance * 3.0f;
        }*/
        float adjustedFloodChance = baseFloodChance * (1.0f - terrainWeight);
        float adjustedSpreadChance = baseSpreadChance * (1.0f - terrainWeight);
        float adjustedRecedeChance = baseRecedeChance * (nearbyMountainForestTiles > 3 ? 1f : 3.0f);
        return (adjustedRecedeChance, adjustedSpreadChance, adjustedFloodChance);
    }

    private int CountNearbyMountainsForests(Vector3Int tilePosition, int radius)
    {
        int count = 0;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                Vector3Int neighbor = new Vector3Int(tilePosition.x + dx, tilePosition.y + dy, tilePosition.z);
                if (mountainTilemap.HasTile(neighbor) || forestTilemap.HasTile(neighbor))
                {
                    count++;
                }
            }
        }
        return count;
    }

    public bool IsFarFromMountainsOrForests(Vector3Int tilePosition, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                Vector3Int neighbor = new Vector3Int(tilePosition.x + dx, tilePosition.y + dy, tilePosition.z);
                if (mountainTilemap.HasTile(neighbor) || forestTilemap.HasTile(neighbor))
                {
                    return false; // Found a nearby mountain or forest
                }
            }
        }
        return true; // No mountains or forests within the given radius
    }

    public void SimulateFlooding()
    {
        DebugLog("Flooding is being simulated for this round...");

        HashSet<Vector3Int> newFloodTiles = new HashSet<Vector3Int>();

        // --- Phase 1: Generate new floods from river-adjacent tiles ---
        foreach (var riverTile in riverTiles)
        {
            foreach (Vector3Int neighbor in GetNeighbors(riverTile))
            {
                if (!floodedTiles.ContainsKey(neighbor))
                {
                    var (_, _, floodChance) = GetFloodChances(neighbor);
                    if (Random.value < floodChance)
                    {
                        newFloodTiles.Add(neighbor);
                    }
                }
            }
        }

        // --- Phase 2: Spread flooding outward (chained spreading) ---
        GlobalEnums.WeatherType currentWeather = GlobalManager.Instance.currentWeather;
        int spreadIterations; //Allows the flood to push deeper into vulnerable terrain according to weather

        switch (currentWeather)
        {
            case GlobalEnums.WeatherType.Sunny:
                spreadIterations = 1;
                break;
            case GlobalEnums.WeatherType.Rainy:
                spreadIterations = 2;
                break;
            case GlobalEnums.WeatherType.Stormy:
                spreadIterations = 4;
                break;
            default:
                spreadIterations = 2;
                break;
        }
        HashSet<Vector3Int> frontier = new HashSet<Vector3Int>(floodedTiles.Keys);

        for (int i = 0; i < spreadIterations; i++)
        {
            HashSet<Vector3Int> nextFrontier = new HashSet<Vector3Int>();

            foreach (var tile in frontier)
            {
                foreach (Vector3Int neighbor in GetNeighbors(tile))
                {
                    if (!floodedTiles.ContainsKey(neighbor) && !newFloodTiles.Contains(neighbor))
                    {
                        var (_, spreadChance, _) = GetFloodChances(neighbor);
                        if (Random.value < spreadChance)
                        {
                            newFloodTiles.Add(neighbor);
                            nextFrontier.Add(neighbor); // Spread further from here in next iteration
                        }
                    }
                }
            }

            frontier = nextFrontier;
        }

        // --- Phase 3: Apply new flooded tiles ---
        foreach (var tile in newFloodTiles)
        {
            floodedTilemap.SetTile(tile, floodedTile);
            floodedTiles[tile] = 1;
            //DebugLog("Flooded: " + tile);
        }

        // --- Phase 4: Recede logic ---
        List<Vector3Int> recedeTiles = new List<Vector3Int>();
        foreach (var tile in floodedTiles.Keys)
        {
            var (recedeChance, _, _) = GetFloodChances(tile);
            if (Random.value < recedeChance)
            {
                recedeTiles.Add(tile);
            }
        }

        foreach (var tile in recedeTiles)
        {
            floodedTilemap.SetTile(tile, null);
            floodedTiles.Remove(tile);
            //DebugLog("Receded: " + tile);
        }

        // --- Summary ---
        Debug.Log($"Flood Summary: {newFloodTiles.Count} new flooded, {recedeTiles.Count} receded.");
    }


    private List<Vector3Int> GetNeighbors(Vector3Int tile)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>
        {
            new Vector3Int(tile.x + 1, tile.y, tile.z),
            new Vector3Int(tile.x - 1, tile.y, tile.z),
            new Vector3Int(tile.x, tile.y + 1, tile.z),
            new Vector3Int(tile.x, tile.y - 1, tile.z),
            new Vector3Int(tile.x + 1, tile.y + 1, tile.z),
            new Vector3Int(tile.x - 1, tile.y - 1, tile.z),
            new Vector3Int(tile.x - 1, tile.y + 1, tile.z),
            new Vector3Int(tile.x + 1, tile.y - 1, tile.z)
        };
        return neighbors;
    }

    /*private void DisableBuildingsOnTile(Vector3Int tile)
    {
        Collider2D[] objects = Physics2D.OverlapPointAll(floodedTilemap.CellToWorld(tile));

        foreach (Collider2D obj in objects)
        {
            Building building = obj.GetComponent<Building>();
            if (building != null)
            {
                building.DisableBuilding();
            }
        }
    }*/

    private void DebugLog(string message)
    {
        if (Flood_Debug)
        {
            Debug.Log(message);
        }
    }
}
