using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// <see cref="RoadNetwork"/> implementation that visualizes the roads of the network on a <see cref="Terrain"/><br/>
    /// the <see cref="RoadStage.Index"/> determines which terrain layer is used<br/>
    /// when a road is removed the layer at the groundIndex is set
    /// </summary>
    public class TilemapRoadNetwork : RoadNetwork
    {
        public Tilemap Tilemap { get; private set; }

        public TilemapRoadNetwork(GridPathfindingSettings pathfindingSettings, Road road, Tilemap tilemap, int level = 0) : base(pathfindingSettings, road, level)
        {
            Tilemap = tilemap;
        }

        private Dictionary<TileBase, Road> _roadTiles = new Dictionary<TileBase, Road>();
        private LazyDependency<ILayerManager> _layerManager = new LazyDependency<ILayerManager>();

        public override void Initialize()
        {
            foreach (var position in Tilemap.cellBounds.allPositionsWithin)
            {
                if (Tilemap.HasTile(position))
                {
                    var point = (Vector2Int)position;
                    if (Blocked.Contains(point))
                        continue;

                    DefaultPathfinding.Add(point);
                    BlockedPathfinding.Add(point);
                }
            }

            var roads = Dependencies.GetOptional<IObjectSet<Road>>();
            if (roads != null)
            {
                foreach (var road in roads.Objects)
                {
                    foreach (var stage in road.Stages)
                    {
                        if (stage.Tile == null || _roadTiles.ContainsKey(stage.Tile))
                            continue;
                        _roadTiles.Add(stage.Tile, road);
                    }
                }
            }

            base.Initialize();
        }

        public override void CheckLayers(IEnumerable<Vector2Int> points)
        {
            if (Tilemap == null)
                return;

            foreach (var point in points ?? GetPoints())
            {
                var currentTile = Tilemap.GetTile((Vector3Int)point);
                if (currentTile == null)
                    continue;

                var road = _roadTiles.GetValueOrDefault(currentTile);
                if (road == null)
                    continue;

                var roadTile = road.GetTile(point, _layerManager.Value);
                if (currentTile == roadTile)
                    continue;

                Tilemap.SetTile((Vector3Int)point, roadTile);
            }
        }

        public override bool TryGetRoad(Vector2Int point, out Road road, out string stage)
        {
            road = null;
            stage = null;

            if (!HasPoint(point))
                return false;

            var tile = Tilemap.GetTile((Vector3Int)point);
            if (tile == null)
                return false;

            if (Road)
            {
                foreach (var roadStage in Road.Stages)
                {
                    if (roadStage.Tile == tile)
                    {
                        road = Road;
                        stage = roadStage.Key;
                        return true;
                    }
                }
            }
            else
            {
                foreach (var roadObject in Dependencies.Get<IObjectSet<Road>>().Objects)
                {
                    foreach (var roadStage in roadObject.Stages)
                    {
                        if (roadStage.Tile == tile)
                        {
                            road = roadObject;
                            stage = roadStage.Key;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        protected override void setPoint(Vector2Int point, Road road) => Tilemap?.SetTile((Vector3Int)point, road?.GetTile(point, _layerManager.Value));
        protected override bool checkPoint(Vector2Int point) => Tilemap.HasTile((Vector3Int)point);

        #region Saving
        public override RoadsData SaveData()
        {
            RoadsData data = new RoadsData() { Key = Road?.Key };

            Dictionary<TileBase, string> tiles = new Dictionary<TileBase, string>();
            foreach (var road in Dependencies.Get<IObjectSet<Road>>().Objects)
            {
                foreach (var stage in road.Stages)
                {
                    tiles.Add(stage.Tile, stage.Key);
                }
            }

            Dictionary<string, List<Vector2Int>> roadPositions = new Dictionary<string, List<Vector2Int>>();
            foreach (var position in Tilemap.cellBounds.allPositionsWithin)
            {
                if (Tilemap.HasTile(position))
                {
                    var tile = Tilemap.GetTile(position);
                    if (!tiles.ContainsKey(tile))
                        continue;
                    string key = tiles[tile];

                    if (!roadPositions.ContainsKey(key))
                        roadPositions.Add(key, new List<Vector2Int>());
                    roadPositions[key].Add((Vector2Int)position);
                }
            }

            data.Roads = roadPositions.Select(p => new RoadData() { Key = p.Key, Positions = p.Value.ToArray() }).ToArray();

            return data;
        }

        public override void LoadData(RoadsData roadsData)
        {
            var oldPoints = GetPoints().ToList();

            DefaultPathfinding.Clear();
            BlockedPathfinding.Clear();

            foreach (var point in oldPoints)
            {
                Tilemap.SetTile((Vector3Int)point, null);
            }

            Dictionary<string, TileBase> tiles = new Dictionary<string, TileBase>();
            foreach (var road in Dependencies.Get<IObjectSet<Road>>().Objects)
            {
                foreach (var stage in road.Stages)
                {
                    tiles.Add(stage.Key, stage.Tile);
                }
            }

            foreach (var data in roadsData.Roads)
            {
                if (!tiles.ContainsKey(data.Key))
                    continue;
                var tile = tiles[data.Key];

                foreach (var point in data.Positions)
                {
                    Tilemap.SetTile((Vector3Int)point, tile);

                    DefaultPathfinding.Add(point);
                    if (!Blocked.Contains(point))
                        BlockedPathfinding.Add(point);
                }
            }

            onPointsChanged(new PointsChanged<IStructure>(this, oldPoints, GetPoints()));
        }
        #endregion
    }
}
