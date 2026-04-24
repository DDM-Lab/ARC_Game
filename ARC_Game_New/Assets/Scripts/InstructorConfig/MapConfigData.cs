using System;
using System.Collections.Generic;

// ─── Tile Layer enum (used for palette selection; stored as separate bool arrays) ─
public enum TileType
{
    Land     = 0,  // base ground — rendered below all other layers
    River    = 1,  // above Land
    Blocking = 2,  // above River  (forest edge / obstacle markers)
    Road     = 3,  // topmost tile layer
    Empty    = 4   // palette "erase all" sentinel — NOT stored
}

// ─── GameObject Layer ─────────────────────────────────────────────────────────
public enum PlacedObjectType
{
    Forest        = 0,
    Community     = 1,
    AbandonedSite = 2,
    Vehicle       = 3,
    Motel         = 4
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
    public int   initialBudget          = 15000;
    public float dailyBudgetAllocation  = 2000f;
    public float foodCostPerPerson      = 10f;
    public float shelterCostPerPerson   = 5f;
    public float workerTrainingCost     = 500f;

    // Population & Satisfaction
    public int   initialSatisfaction    = 80;
    public int   totalPopulation        = 200;
    public int   numberOfCommunities    = 3;

    // Workers
    public int   initialWorkerCount     = 10;

    // Timing
    public int   gameDurationDays       = 8;
    public float dayDurationSeconds     = 120f;
}

// ─── Full Map Config (serialized to JSON) ─────────────────────────────────────
[Serializable]
public class MapConfig
{
    public int    schemaVersion = 2;  // bumped from 1 (layer system)
    public string timestamp;

    public int gridWidth;
    public int gridHeight;

    // ── Tile layers (bottom → top render order) ───────────────────────────────
    // Each is a flat bool array, index: layer[y * gridWidth + x]
    // true = this layer is present at that cell
    public bool[] landLayer;
    public bool[] riverLayer;
    public bool[] blockingLayer;
    public bool[] roadLayer;

    // ── Object & parameter layers ─────────────────────────────────────────────
    public List<PlacedObjectData> objects    = new List<PlacedObjectData>();
    public ScenarioParameters     parameters = new ScenarioParameters();

    // ─────────────────────────────────────────────────────────────────────────

    public void Initialize(int width, int height)
    {
        gridWidth  = width;
        gridHeight = height;
        int n      = width * height;

        landLayer     = new bool[n];
        riverLayer    = new bool[n];
        blockingLayer = new bool[n];
        roadLayer     = new bool[n];

        objects    = new List<PlacedObjectData>();
        parameters = new ScenarioParameters();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public bool InBounds(int x, int y) =>
        x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;

    int I(int x, int y) => y * gridWidth + x;

    // Per-layer getters
    public bool GetLand    (int x, int y) => InBounds(x, y) && landLayer    [I(x,y)];
    public bool GetRiver   (int x, int y) => InBounds(x, y) && riverLayer   [I(x,y)];
    public bool GetBlocking(int x, int y) => InBounds(x, y) && blockingLayer[I(x,y)];
    public bool GetRoad    (int x, int y) => InBounds(x, y) && roadLayer    [I(x,y)];

    // Per-layer setters
    public void SetLand    (int x, int y, bool v) { if (InBounds(x,y)) landLayer    [I(x,y)] = v; }
    public void SetRiver   (int x, int y, bool v) { if (InBounds(x,y)) riverLayer   [I(x,y)] = v; }
    public void SetBlocking(int x, int y, bool v) { if (InBounds(x,y)) blockingLayer[I(x,y)] = v; }
    public void SetRoad    (int x, int y, bool v) { if (InBounds(x,y)) roadLayer    [I(x,y)] = v; }

    // Generic access by TileType enum (Empty does nothing)
    public bool GetLayer(int x, int y, TileType t) => t switch
    {
        TileType.Land     => GetLand    (x, y),
        TileType.River    => GetRiver   (x, y),
        TileType.Blocking => GetBlocking(x, y),
        TileType.Road     => GetRoad    (x, y),
        _                 => false
    };

    public void SetLayer(int x, int y, TileType t, bool v)
    {
        switch (t)
        {
            case TileType.Land:     SetLand    (x, y, v); break;
            case TileType.River:    SetRiver   (x, y, v); break;
            case TileType.Blocking: SetBlocking(x, y, v); break;
            case TileType.Road:     SetRoad    (x, y, v); break;
        }
    }

    /// <summary>Remove all tile layers from a single cell.</summary>
    public void EraseAll(int x, int y)
    {
        SetLand    (x, y, false);
        SetRiver   (x, y, false);
        SetBlocking(x, y, false);
        SetRoad    (x, y, false);
    }
}
