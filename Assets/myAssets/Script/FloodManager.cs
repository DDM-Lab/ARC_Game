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

    public float floodChance = 0.3f; // Probability per round of flooding neighboring tiles
    public float recedeChance = 0.1f; // Probability of flood receding
    
    public bool Flood_Debug = true; // Set to true to enable debug logs

    private HashSet<Vector3Int> riverTiles = new HashSet<Vector3Int>(); // Stores river tiles
    private Dictionary<Vector3Int, int> floodedTiles = new Dictionary<Vector3Int, int>(); // Stores flood status

    private void Start()
    {
        InitializeRiverTiles();
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

    public void SimulateFlooding()
    {
        DebugLog("Flooding is being simulated for this round...");
        List<Vector3Int> newFloodTiles = new List<Vector3Int>();

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

        // Apply new flood tiles
        foreach (Vector3Int tile in newFloodTiles)
        {
            floodedTilemap.SetTile(tile, floodedTile);
            floodedTiles[tile] = 1; // Mark as flooded
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

        // Remove flood from receded tiles
        foreach (Vector3Int tile in recedeTiles)
        {
            floodedTilemap.SetTile(tile, null);
            floodedTiles.Remove(tile);
        }
        Debug.Log("Flooding simulation complete.");
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
