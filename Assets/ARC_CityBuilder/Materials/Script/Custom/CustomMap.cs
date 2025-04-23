using UnityEngine;
using UnityEngine.Tilemaps;
using CityBuilderCore;

public class CustomMap : DefaultMap
{
    [Tooltip("Tilemap that contains flood tiles")]
    public Tilemap FloodTiles;

    /// <summary>
    /// Checks if a tile at a grid position is flooded
    /// </summary>
    public bool IsFlood(Vector2Int point)
    {
        if (FloodTiles == null)
            return false;

        return FloodTiles.HasTile((Vector3Int)point);
    }

    /// <summary>
    /// Overload to check flooding based on world position
    /// </summary>
    public bool IsFlood(Vector3 worldPosition)
    {
        if (FloodTiles == null)
            return false;

        Vector3Int cell = FloodTiles.WorldToCell(worldPosition);
        return FloodTiles.HasTile(cell);
    }

    
}
