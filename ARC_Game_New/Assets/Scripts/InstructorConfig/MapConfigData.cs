using System;
using System.Collections.Generic;
using UnityEngine;

// ─── Tile Layer (painted on a grid) ───────────────────────────────────────────
public enum TileType
{
    Empty    = 0,
    Road     = 1,
    Land     = 2,
    Blocking = 3, // represents forest/obstacle positions at tile level
    River    = 4
}

// ─── GameObject Layer (placed objects snapped to grid) ────────────────────────
public enum PlacedObjectType
{
    Forest       = 0,
    Community    = 1,
    AbandonedSite = 2,
    Vehicle      = 3,
    Motel        = 4
}

[Serializable]
public class PlacedObjectData
{
    public PlacedObjectType type;
    public int gridX;
    public int gridY;
    public int width  = 1;
    public int height = 1;
}

// ─── Scenario Parameters ──────────────────────────────────────────────────────
[Serializable]
public class ScenarioParameters
{
    // Economy
    public int   initialBudget           = 15000;
    public float dailyBudgetAllocation   = 2000f;
    public float foodCostPerPerson       = 10f;
    public float shelterCostPerPerson    = 5f;
    public float workerTrainingCost      = 500f;

    // Population & Satisfaction
    public int   initialSatisfaction     = 80;
    public int   totalPopulation         = 200;
    public int   numberOfCommunities     = 3;

    // Workers
    public int   initialWorkerCount      = 10;

    // Timing
    public int   gameDurationDays        = 8;
    public float dayDurationSeconds      = 120f;
}

// ─── Full Map Config (serialized to JSON) ─────────────────────────────────────
[Serializable]
public class MapConfig
{
    public int    schemaVersion = 1;
    public string timestamp;

    public int gridWidth;
    public int gridHeight;

    /// <summary>Flat array. Index: tiles[y * gridWidth + x]</summary>
    public int[] tiles;

    public List<PlacedObjectData> objects    = new List<PlacedObjectData>();
    public ScenarioParameters     parameters = new ScenarioParameters();

    public void Initialize(int width, int height)
    {
        gridWidth  = width;
        gridHeight = height;
        tiles      = new int[width * height];
        objects    = new List<PlacedObjectData>();
        parameters = new ScenarioParameters();
    }

    public bool InBounds(int x, int y) =>
        x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;

    public TileType GetTile(int x, int y)
    {
        if (!InBounds(x, y)) return TileType.Empty;
        return (TileType)tiles[y * gridWidth + x];
    }

    public void SetTile(int x, int y, TileType type)
    {
        if (!InBounds(x, y)) return;
        tiles[y * gridWidth + x] = (int)type;
    }
}
