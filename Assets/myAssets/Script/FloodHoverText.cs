using System.Collections.Generic; 
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using TMPro;

public class FloodHoverUI : MonoBehaviour
{
    public Tilemap floodedTilemap;  // Reference to the flood tilemap
    public FloodManager floodManager; // Reference to FloodManager
    public TextMeshProUGUI floodInfoText;  // UI Text for displaying flood info

    private Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;
        floodInfoText.gameObject.SetActive(false); // Hide text initially
    }

    private void Update()
    {
        Vector3 worldPoint = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int tilePosition = floodedTilemap.WorldToCell(worldPoint);

        if (floodedTilemap.HasTile(tilePosition) || IsAdjacentToFloodedTile(tilePosition))
        {
            var (recedeChance, spreadChance, floodChance) = floodManager.GetFloodChances(tilePosition);
            floodInfoText.text = $"Recede: {recedeChance * 100:F1}%\nSpread: {spreadChance * 100:F1}%\nFlood: {floodChance * 100:F1}%";
            floodInfoText.transform.position = Input.mousePosition + new Vector3(10, -10, 0);
            floodInfoText.gameObject.SetActive(true);
        }
        else
        {
            floodInfoText.gameObject.SetActive(false);
        }
    }

    private bool IsAdjacentToFloodedTile(Vector3Int tilePosition)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>
        {
            new Vector3Int(tilePosition.x + 1, tilePosition.y, tilePosition.z),
            new Vector3Int(tilePosition.x - 1, tilePosition.y, tilePosition.z),
            new Vector3Int(tilePosition.x, tilePosition.y + 1, tilePosition.z),
            new Vector3Int(tilePosition.x, tilePosition.y - 1, tilePosition.z)
        };

        foreach (Vector3Int neighbor in neighbors)
        {
            if (floodedTilemap.HasTile(neighbor))
                return true;
        }

        return false;
    }
}

