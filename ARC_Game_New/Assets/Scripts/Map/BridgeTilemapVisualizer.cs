using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections;

public class BridgeTilemapVisualizer : MonoBehaviour
{
    [Header("Tilemaps")]
    public Tilemap groundTilemap;
    public Tilemap roadTilemap;
    public Tilemap bridgeTilemap;

    [Header("Tiles")]
    public TileBase riverTile;
    public TileBase bridgeTile;

    IEnumerator Start()
    {
        Debug.Log("[BridgeTilemapVisualizer] Start called");

        MapConfigApplier applier = FindObjectOfType<MapConfigApplier>();
        if (applier != null)
        {
            Debug.Log("[BridgeTilemapVisualizer] Waiting for MapConfigApplier...");
            float timeout = 10f;
            float elapsed = 0f;
            while (!applier.hasApplied && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (!applier.hasApplied)
                Debug.LogWarning("[BridgeTilemapVisualizer] MapConfigApplier timed out — proceeding anyway.");
        }
        else
        {
            Debug.Log("[BridgeTilemapVisualizer] No MapConfigApplier found, running immediately.");
        }

        PlaceBridgeTiles();
    }

    void PlaceBridgeTiles()
    {
        if (groundTilemap == null || roadTilemap == null || bridgeTilemap == null || riverTile == null || bridgeTile == null)
        {
            Debug.LogWarning($"[BridgeTilemapVisualizer] Missing reference — ground:{groundTilemap} road:{roadTilemap} bridge:{bridgeTilemap} riverTile:{riverTile} bridgeTile:{bridgeTile}");
            return;
        }

        bridgeTilemap.ClearAllTiles();

        BoundsInt bounds = roadTilemap.cellBounds;
        Debug.Log($"[BridgeTilemapVisualizer] Scanning road bounds {bounds}");

        int roadCount = 0, bridgeCount = 0;

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);

                TileBase road = roadTilemap.GetTile(pos);
                if (road == null) continue;
                roadCount++;

                TileBase ground = groundTilemap.GetTile(pos);
                Debug.Log($"[BridgeTilemapVisualizer] Road at {pos}, ground tile: {(ground != null ? ground.name : "null")}, riverTile: {riverTile.name}, match: {ground == riverTile}");

                if (ground != riverTile) continue;

                bool hasVertical = roadTilemap.GetTile(pos + Vector3Int.up) != null
                                || roadTilemap.GetTile(pos + Vector3Int.down) != null;

                bridgeTilemap.SetTile(pos, bridgeTile);

                if (hasVertical)
                    bridgeTilemap.SetTransformMatrix(pos, Matrix4x4.identity);
                else
                    bridgeTilemap.SetTransformMatrix(pos, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, 90f), Vector3.one));

                bridgeCount++;
            }
        }

        Debug.Log($"[BridgeTilemapVisualizer] Done — {roadCount} road tiles scanned, {bridgeCount} bridges placed.");
    }
}
