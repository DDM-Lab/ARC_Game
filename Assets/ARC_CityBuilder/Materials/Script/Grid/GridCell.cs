using System.Collections.Generic;
using UnityEngine;

public class GridCell
{
    public Vector2Int coord;
    public Vector3 worldPosition;
    public bool isEmpty = true;
    public GameObject placedObject;

    public GridCell(Vector2Int coord, Vector3 worldPosition)
    {
        this.coord = coord;
        this.worldPosition = worldPosition;
    }

    public void PlaceBuilding(GameObject obj)
    {
        placedObject = obj;
        isEmpty = false;
    }
}
