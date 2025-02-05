using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// <see cref="RoadNetwork"/> implementation that visualizes the roads of the network on a <see cref="Terrain"/><br/>
    /// the <see cref="RoadStage.Index"/> determines which terrain layer is used<br/>
    /// when a road is removed the layer at the groundIndex is set
    /// </summary>
    public class TerrainRoadNetwork : RoadNetwork
    {
        public Terrain Terrain { get; private set; }

        private bool _persist;
        private int _groundIndex;
        private List<int> _roadIndices;

        private LazyDependency<IMap> _map = new LazyDependency<IMap>();
        private LazyDependency<IGridPositions> _gridPositions = new LazyDependency<IGridPositions>();

        public TerrainRoadNetwork(GridPathfindingSettings pathfindingSettings, Road road, Terrain terrain, bool persist, int groundIndex = 0, int level = 0) : base(pathfindingSettings, road, level)
        {
            Terrain = terrain;
            _persist = persist;
            _groundIndex = groundIndex;
        }

        public override void Initialize()
        {
            if (Road)
                _roadIndices = Road.Stages.Select(s => s.Index).ToList();
            else
                _roadIndices = Dependencies.Get<IObjectSet<Road>>().Objects.SelectMany(r => r.Stages).Select(s => s.Index).Distinct().ToList();

            for (int x = 0; x < _map.Value.Size.x; x++)
            {
                for (int y = 0; y < _map.Value.Size.y; y++)
                {
                    var point = new Vector2Int(x, y);
                    if (Blocked.Contains(point))
                        continue;

                    if (_roadIndices.Contains(getIndex(point)))
                    {
                        DefaultPathfinding.Add(point);
                        BlockedPathfinding.Add(point);
                    }
                }
            }

            base.Initialize();
        }

        public void Reload()
        {
            var oldPoints = GetPoints().ToList();

            DefaultPathfinding.Clear();
            BlockedPathfinding.Clear();

            for (int x = 0; x < _map.Value.Size.x; x++)
            {
                for (int y = 0; y < _map.Value.Size.y; y++)
                {
                    var point = new Vector2Int(x, y);
                    if (Blocked.Contains(point))
                        continue;

                    if (_roadIndices.Contains(getIndex(point)))
                    {
                        DefaultPathfinding.Add(point);
                        BlockedPathfinding.Add(point);
                    }
                }
            }

            onPointsChanged(new PointsChanged<IStructure>(this, oldPoints, GetPoints()));
        }

        public override void CheckLayers(IEnumerable<Vector2Int> points)
        {
            if (Terrain == null)
                return;

            foreach (var point in points ?? GetPoints())
            {
                var currentIndex = getIndex(point);
                if (!_roadIndices.Contains(currentIndex))
                    continue;

                var road = Dependencies.Get<IObjectSet<Road>>().Objects.FirstOrDefault(o => o.Stages.Any(s => s.Index == currentIndex));
                if (road == null)
                    continue;

                var actualIndex = road.GetIndex(point);
                if (currentIndex == actualIndex)
                    continue;

                setPoint(point, actualIndex);
            }
        }

        public override bool TryGetRoad(Vector2Int point, out Road road, out string stage)
        {
            road = null;
            stage = null;

            if (!HasPoint(point))
                return false;

            var index = getIndex(point);
            if (index == -1 || !_roadIndices.Contains(index))
                return false;

            if (Road)
            {
                foreach (var roadStage in Road.Stages)
                {
                    if (roadStage.Index == index)
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
                        if (roadStage.Index == index)
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

        protected override void setPoint(Vector2Int point, Road road)
        {
            setPoint(point, road?.GetIndex(point) ?? -1);
        }
        protected override bool checkPoint(Vector2Int point)
        {
            return _roadIndices.Contains(getIndex(point));
        }

        private int getIndex(Vector2Int point)
        {
            var a = getAlphamap(point);
            for (int i = 0; i < a.GetLength(2); i++)
            {
                if (a[0, 0, i] > 0)
                    return i;
            }
            return -1;
        }
        private float[,,] getAlphamap(Vector2Int point)
        {
            var data = Terrain.terrainData;

            var position = _gridPositions.Value.GetWorldCenterPosition(point);
            var size = data.size;
            var factor = new Vector3(position.x / size.x, position.y / size.y, position.z / size.z);

            var terrainPoint = new Vector2(data.alphamapResolution * factor.x, data.alphamapResolution * factor.z);

            return data.GetAlphamaps(Mathf.FloorToInt(terrainPoint.x), Mathf.FloorToInt(terrainPoint.y), 1, 1);
        }

        private void setPoint(Vector2Int point, int roadIndex)
        {
            var data = Terrain.terrainData;

            var position = _gridPositions.Value.GetWorldPosition(point);
            var size = data.size;
            var factor = new Vector3(position.x / size.x, position.y / size.y, position.z / size.z);

            var terrainPoint = new Vector2(data.alphamapResolution * factor.x, data.alphamapResolution * factor.z);

            var map = Dependencies.Get<IMap>();
            var terrainSize = new Vector2Int(Mathf.CeilToInt((float)data.alphamapResolution / map.Size.x), Mathf.CeilToInt((float)data.alphamapResolution / map.Size.y));

            var a = data.GetAlphamaps(Mathf.FloorToInt(terrainPoint.x), Mathf.FloorToInt(terrainPoint.y), terrainSize.x, terrainSize.y);

            for (int x = 0; x < a.GetLength(0); x++)
            {
                for (int y = 0; y < a.GetLength(1); y++)
                {
                    for (int i = 0; i < a.GetLength(2); i++)
                    {
                        a[x, y, i] = 0f;
                    }

                    if (roadIndex >= 0)
                        a[x, y, roadIndex] = 1f;
                    else
                        a[x, y, _groundIndex] = 1f;
                }
            }

            data.SetAlphamaps(Mathf.FloorToInt(terrainPoint.x), Mathf.FloorToInt(terrainPoint.y), a);
        }

        #region Saving
        public override RoadsData SaveData()
        {
            if (!_persist)
                return null;

            RoadsData data = new RoadsData() { Key = Road?.Key };

            Dictionary<int, string> tiles = new Dictionary<int, string>();
            foreach (var road in Dependencies.Get<IObjectSet<Road>>().Objects)
            {
                foreach (var stage in road.Stages)
                {
                    tiles.Add(stage.Index, stage.Key);
                }
            }

            Dictionary<string, List<Vector2Int>> roadPositions = new Dictionary<string, List<Vector2Int>>();
            foreach (var point in DefaultPathfinding.GetPoints())
            {
                var a = getAlphamap(point);
                int index = -1;
                for (var i = 0; i < a.GetLength(2); i++)
                {
                    if (a[0, 0, i] == 1f)
                    {
                        index = i;
                        break;
                    }
                }

                if (!tiles.ContainsKey(index))
                    continue;
                string key = tiles[index];

                if (!roadPositions.ContainsKey(key))
                    roadPositions.Add(key, new List<Vector2Int>());
                roadPositions[key].Add(point);
            }

            data.Roads = roadPositions.Select(p => new RoadData() { Key = p.Key, Positions = p.Value.ToArray() }).ToArray();

            return data;
        }

        public override void LoadData(RoadsData roadsData)
        {
            if (!_persist)
                return;

            var oldPoints = GetPoints().ToList();

            DefaultPathfinding.Clear();
            BlockedPathfinding.Clear();

            foreach (var point in oldPoints)
            {
                setPoint(point, null); ;
            }

            Dictionary<string, int> tiles = new Dictionary<string, int>();
            foreach (var road in Dependencies.Get<IObjectSet<Road>>().Objects)
            {
                foreach (var stage in road.Stages)
                {
                    tiles.Add(stage.Key, stage.Index);
                }
            }

            foreach (var data in roadsData.Roads)
            {
                if (!tiles.ContainsKey(data.Key))
                    continue;
                var index = tiles[data.Key];

                foreach (var point in data.Positions)
                {
                    setPoint(point, index);

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
