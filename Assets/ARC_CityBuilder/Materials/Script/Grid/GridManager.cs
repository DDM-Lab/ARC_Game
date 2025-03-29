using UnityEngine;
using System.Collections.Generic;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance;

    public int width = 48;
    public int height = 32;
    public float cellSize = 1f;

    private Dictionary<Vector2Int, GridCell> grid = new();

    private void Awake()
    {
        Instance = this;
        InitializeGrid();
    }

    private void InitializeGrid()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int coord = new Vector2Int(x, y);
                Vector3 worldPos = GetWorldPosition(coord);
                grid[coord] = new GridCell(coord, worldPos);
            }
        }

        Debug.Log("[GridManager] Grid initialized.");
    }

    public Vector3 GetWorldPosition(Vector2Int coord)
    {
        return new Vector3(coord.x * cellSize, coord.y * cellSize, 0);
    }

    public bool IsCellAvailable(Vector2Int coord)
    {
        return grid.ContainsKey(coord) && grid[coord].isEmpty;
    }

    public void OccupyCell(Vector2Int coord, GameObject building)
    {
        if (grid.ContainsKey(coord))
        {
            grid[coord].PlaceBuilding(building);
        }
    }
}
