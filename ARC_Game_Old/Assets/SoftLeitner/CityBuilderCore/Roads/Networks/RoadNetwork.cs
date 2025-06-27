using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper used by <see cref="IRoadManager"/> to manage a single road network<br/>
    /// it holds the pathfinding for both regular and blocked road pathing<br/>
    /// also it counts as a <see cref="IStructure"/> and can therefore be queried and checked against using <see cref="IStructureManager"/><br/>
    /// this basic RoadNetwork is not visualized in any way, it is only a logical helper<br/>
    /// for a visualized network inherit this class and implement <see cref="setPoint(Vector2Int, Road)"/> and <see cref="checkPoint(Vector2Int)"/><br/>
    /// examples of this can be found in <see cref="TilemapRoadNetwork"/> and <see cref="TerrainRoadNetwork"/>
    /// </summary>
    public class RoadNetwork : IStructure, ILayerDependency
    {
        public event Action<PointsChanged<IStructure>> PointsChanged;

        public Road Road { get; private set; }

        public GridPathfindingBase DefaultPathfinding { get; private set; }
        public GridPathfindingBase BlockedPathfinding { get; private set; }
        public List<Vector2Int> Blocked { get; private set; }

        public StructureReference StructureReference { get; set; }

        public bool IsDestructible => Road?.IsDestructible ?? true;
        public bool IsMovable => Road?.IsMovable ?? true;
        public bool IsDecorator => false;
        public bool IsWalkable => true;

        public int Level => Road == null ? _level : Road.Level.Value;
        public string Key => Road?.Key ?? "ROD";

        private int _level;
        private GridLinks _links;

        public RoadNetwork(GridPathfindingSettings pathfindingSettings, Road road, int level = 0)
        {
            Road = road;

            _level = level;
            _links = new GridLinks();

            DefaultPathfinding = pathfindingSettings.Create();
            BlockedPathfinding = pathfindingSettings.Create();
            Blocked = new List<Vector2Int>();

            StructureReference = new StructureReference(this);
        }

        public virtual void Initialize()
        {
            Dependencies.Get<IStructureManager>().RegisterStructure(this, true);
        }

        public virtual void Dispose()
        {
            DefaultPathfinding.Dispose();
            BlockedPathfinding.Dispose();
        }

        public void Calculate(int maxCalculations = PathQuery.DEFAULT_MAX_CALCULATIONS)
        {
            DefaultPathfinding.Calculate(maxCalculations);
            BlockedPathfinding.Calculate(maxCalculations);
        }

        public string GetName() => Road ? Road.Name : "Roads";

        public IEnumerable<Vector2Int> GetPoints() => DefaultPathfinding.GetPoints();
        public bool HasPoint(Vector2Int point) => DefaultPathfinding.HasPoint(point);

        public void Add(IEnumerable<Vector2Int> points) => Add(points, null);
        public List<Vector2Int> Add(IEnumerable<Vector2Int> points, Road road)
        {
            var structureManager = Dependencies.Get<IStructureManager>();

            List<Vector2Int> validPoints = points.Where(p => !DefaultPathfinding.HasPoint(p) && structureManager.CheckAvailability(p, Level, road)).ToList();
            if (validPoints.Count == 0)
                return null;

            foreach (var point in validPoints)
            {
                setPoint(point, road ?? Road);

                DefaultPathfinding.Add(point);
                if (!Blocked.Contains(point))
                    BlockedPathfinding.Add(point);
            }

            structureManager.Remove(validPoints, Level, true);

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, Enumerable.Empty<Vector2Int>(), validPoints));

            return validPoints;
        }
        public void Remove(IEnumerable<Vector2Int> points)
        {
            foreach (var point in points)
            {
                setPoint(point, null);

                DefaultPathfinding.Remove(point);
                BlockedPathfinding.Remove(point);
            }

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, points, Enumerable.Empty<Vector2Int>()));
        }

        public void Register(IEnumerable<Vector2Int> points)
        {
            foreach (var point in points)
            {
                DefaultPathfinding.Add(point);
                if (!Blocked.Contains(point))
                    BlockedPathfinding.Add(point);
            }
        }
        public void Deregister(IEnumerable<Vector2Int> points)
        {
            foreach (var point in points.Where(p => !checkPoint(p)))
            {
                DefaultPathfinding.Remove(point);
                BlockedPathfinding.Remove(point);
            }
        }

        public void RegisterLink(IGridLink link)
        {
            DefaultPathfinding.AddLink(link);
            _links.Add(link);
        }
        public void DeregisterLink(IGridLink link)
        {
            DefaultPathfinding.RemoveLink(link);
            _links.Remove(link);
        }
        public IEnumerable<IGridLink> GetLinks(Vector2Int start) => _links.Get(start);
        public IGridLink GetLink(Vector2Int start, Vector2Int end) => _links.Get(start, end);

        public void RegisterSwitch(Vector2Int point, RoadNetwork other)
        {
            DefaultPathfinding.AddSwitch(point, other.DefaultPathfinding);
            BlockedPathfinding.AddSwitch(point, other.BlockedPathfinding);
        }
        public void RegisterSwitch(Vector2Int entry, Vector2Int point, Vector2Int exit, RoadNetwork other)
        {
            DefaultPathfinding.AddSwitch(entry, point, exit, other.DefaultPathfinding);
            BlockedPathfinding.AddSwitch(entry, point, exit, other.BlockedPathfinding);
        }

        public void Block(IEnumerable<Vector2Int> points)
        {
            List<Vector2Int> blocked = new List<Vector2Int>();
            foreach (var point in points)
            {
                if (!Blocked.Contains(point))
                    blocked.Add(point);
                Blocked.Add(point);
            }

            foreach (var point in blocked)
            {
                if (DefaultPathfinding.HasPoint(point))
                    BlockedPathfinding.Remove(point);
            }
        }
        public void Unblock(IEnumerable<Vector2Int> points)
        {
            foreach (var point in points)
            {
                if (DefaultPathfinding.HasPoint(point))
                    BlockedPathfinding.Add(point);
            }
        }

        public void BlockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags) => BlockedPathfinding.BlockTags(points, tags);
        public void UnblockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags) => BlockedPathfinding.UnblockTags(points, tags);

        public virtual void CheckLayers(IEnumerable<Vector2Int> points)
        {
        }

        public virtual bool TryGetRoad(Vector2Int point, out Road road, out string stage)
        {
            road = null;
            stage = null;

            return HasPoint(point);
        }

        /// <summary>
        /// checks if a point is defined by the network itself<br/>
        /// points registered from the outside return false
        /// </summary>
        /// <param name="point">a point on the map</param>
        /// <returns>true if the network contains the point, false if the point does not exist or has only been registered from outside</returns>
        public bool CheckPoint(Vector2Int point) => checkPoint(point);

        protected virtual void setPoint(Vector2Int point, Road road) { }
        protected virtual bool checkPoint(Vector2Int point) => false;

        protected void onPointsChanged(PointsChanged<IStructure> pointsChanged)
        {
            PointsChanged?.Invoke(pointsChanged);
        }

        #region Saving
        [Serializable]
        public class RoadsData
        {
            public string Key;
            public RoadData[] Roads;
        }
        [Serializable]
        public class RoadData
        {
            public string Key;
            public Vector2Int[] Positions;
        }

        public virtual RoadsData SaveData()
        {
            return new RoadsData()
            {
                Key = Road?.Key,
                Roads = new RoadData[]
                {
                    new RoadData()
                    {
                        Key=string.Empty,
                        Positions=DefaultPathfinding.GetPoints().ToArray()
                    }
                }
            };
        }

        public virtual void LoadData(RoadsData roadsData)
        {
            var oldPoints = GetPoints().ToList();

            DefaultPathfinding.Clear();
            BlockedPathfinding.Clear();

            if (roadsData.Roads != null && roadsData.Roads.Length > 0)
            {
                foreach (var point in roadsData.Roads[0].Positions)
                {
                    DefaultPathfinding.Add(point);
                    if (!Blocked.Contains(point))
                        BlockedPathfinding.Add(point);
                }
            }

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, oldPoints, GetPoints()));
        }
        #endregion
    }
}
