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
    private const float MAX_TERRAIN_WEIGHT = 0.6f; // Max reduction from terrain (e.g., 60%)

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
                baseFloodChance = 0.1f;
                baseSpreadChance = 0.05f;
                baseRecedeChance = 0.6f;
                break;
            case GlobalEnums.WeatherType.Rainy:
                baseFloodChance = 0.2f;
                baseSpreadChance = 0.2f;
                baseRecedeChance = 0.1f;
                break;
            case GlobalEnums.WeatherType.Stormy:
                baseFloodChance = 0.3f;
                baseSpreadChance = 0.3f;
                baseRecedeChance = 0.05f;
                break;
            default:
                baseFloodChance = 0.2f;
                baseSpreadChance = 0.1f;
                baseRecedeChance = 0.3f;
                break;
        }

        // Only large water bodies trigger flooding events
        bool isLargeWaterBody = largeWaterBodies.Contains(tilePosition);

        if (isLargeWaterBody)
        {
            baseFloodChance *= 2.0f;  // Higher flood chance
            baseSpreadChance *= 3.0f;
        }
        else
        {
            baseFloodChance *= 0.5f;  // Lower flood chance
            baseSpreadChance *= 0.5f;
        }

        int nearbyMountainForestTiles = CountNearbyMountainsForests(tilePosition, TERRAIN_RADIUS);
        float terrainWeight = Mathf.Clamp01(nearbyMountainForestTiles / 2f) * MAX_TERRAIN_WEIGHT; // Scale impact

        float adjustedFloodChance = baseFloodChance * (isLargeWaterBody ? 2.0f : 1.0f) * (1.0f - terrainWeight);
        float adjustedSpreadChance = baseSpreadChance;// * (1.0f - terrainWeight);

        return (baseRecedeChance, adjustedSpreadChance, adjustedFloodChance);
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

        // Flood propagation chance based on weather
        float floodChance, spreadChance, recedeChance;
        (recedeChance, spreadChance, floodChance) = GetFloodChances(Vector3Int.zero);

        List<Vector3Int> newFloodTiles = new List<Vector3Int>();

        foreach (var riverTile in riverTiles)
        {
            List<Vector3Int> neighbors = GetNeighbors(riverTile);

            foreach (Vector3Int neighbor in neighbors)
            {
                if (!floodedTiles.ContainsKey(neighbor) && Random.value < floodChance)
                {
                    newFloodTiles.Add(neighbor);
                }
            }
        }

        // Flood spreading logic
        List<Vector3Int> spreadFloodTiles = new List<Vector3Int>();
        foreach (var floodTile in floodedTiles.Keys)
        {
            List<Vector3Int> neighbors = GetNeighbors(floodTile);

            foreach (Vector3Int neighbor in neighbors)
            {
                if (!floodedTiles.ContainsKey(neighbor) && Random.value < spreadChance)
                {
                    spreadFloodTiles.Add(neighbor);
                }
            }
        }

        // Apply new flood tiles
        foreach (Vector3Int tile in newFloodTiles)
        {
            floodedTilemap.SetTile(tile, floodedTile);
            floodedTiles[tile] = 1;
            //DisableBuildingsOnTile(tile);
        }

        // Chance to recede floods
        List<Vector3Int> recedeTiles = new List<Vector3Int>();

        foreach (var tile in floodedTiles.Keys)
        {
            if (Random.value < recedeChance)
            {
                recedeTiles.Add(tile);
            }
        }

        foreach (Vector3Int tile in recedeTiles)
        {
            floodedTilemap.SetTile(tile, null);
            floodedTiles.Remove(tile);
        }

        // Flood propagation
        foreach (var riverTile in riverTiles)
        {
            List<Vector3Int> neighbors = GetNeighbors(riverTile);

            foreach (Vector3Int neighbor in neighbors)
            {
                if (!floodedTiles.ContainsKey(neighbor) && Random.value < floodChance)
                {
                    newFloodTiles.Add(neighbor);
                }
            }
        }

        Debug.Log($"Flood Simulation: {newFloodTiles.Count} new floods, {spreadFloodTiles.Count} spread, {recedeTiles.Count} receded.");

    }

    private List<Vector3Int> GetNeighbors(Vector3Int tile)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>
        {
            new Vector3Int(tile.x + 1, tile.y, tile.z),
            new Vector3Int(tile.x - 1, tile.y, tile.z),
            new Vector3Int(tile.x, tile.y + 1, tile.z),
            new Vector3Int(tile.x, tile.y - 1, tile.z)
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
