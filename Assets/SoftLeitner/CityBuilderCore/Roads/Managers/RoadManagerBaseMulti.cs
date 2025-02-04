using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for road managers that can create seperate road networks for different <see cref="Road"/>s<br/>
    /// it also creates a combined road network for walkers which do not specifiy which road they can walk on<br/>
    /// the kind of <see cref="Road"/> a walker can walk on is specified in <see cref="WalkerInfo.PathTag"/><br/>
    /// the base class provides all the plumbing between the networks and the manager<br/>
    /// the implementations have to provide the actual <see cref="RoadNetwork"/>s which determines how roads are visualized
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_road_manager_base_multi.html")]
    public abstract class RoadManagerBaseMulti : MonoBehaviour, IRoadManager, IRoadPathfinder, IRoadGridLinker
    {
        /// <summary>
        /// just a helper class that routes blocked pathfinding to the correct blocked methods(explizit interface implementation does not work in this case)
        /// </summary>
        private class BlockedProxy : IRoadPathfinderBlocked
        {
            private RoadManagerBaseMulti _manager;

            public BlockedProxy(RoadManagerBaseMulti manager)
            {
                _manager = manager;
            }

            public bool HasPoint(Vector2Int point, object tag = null) => _manager.HasPointBlocked(point, tag);
            public WalkingPath FindPath(Vector2Int[] starts, Vector2Int[] targets, object tag = null) => _manager.FindPathBlocked(starts, targets, tag);
            public PathQuery FindPathQuery(Vector2Int[] starts, Vector2Int[] targets, object tag = null) => _manager.FindPathQueryBlocked(starts, targets, tag);
        }

        public GridPathfindingSettings PathfindingSettings;
#pragma warning disable 0067
        public event Action<PointsChanged<IStructure>> PositionsChanged;
#pragma warning restore 0067

        public bool IsDestructible => true;
        public bool IsDecorator => false;
        public bool IsWalkable => true;
        public bool IsAllowedOnRoads => false;

        public Transform Root => transform;

        public StructureReference StructureReference { get; set; }

        private RoadNetwork _combinedNetwork;
        private Dictionary<Road, RoadNetwork> _roadNetworks;

        protected virtual void Awake()
        {
            Dependencies.Register<IRoadManager>(this);
            Dependencies.Register<IRoadPathfinder>(this);
            Dependencies.Register<IRoadPathfinderBlocked>(new BlockedProxy(this));
            Dependencies.Register<IRoadGridLinker>(this);

            _combinedNetwork = new RoadNetwork(PathfindingSettings, null);
            _roadNetworks = createNetworks();
        }

        protected virtual void Start()
        {
            _roadNetworks.Values.ForEach(n => n.Initialize());
            _roadNetworks.Values.ForEach(r =>
            {
                _combinedNetwork.DefaultPathfinding.Add(r.DefaultPathfinding.GetPoints());
                _combinedNetwork.BlockedPathfinding.Add(r.DefaultPathfinding.GetPoints());
            });
        }

        protected virtual void Update()
        {
            _combinedNetwork.Calculate();
            _roadNetworks.Values.ForEach(n => n.Calculate());
        }

        protected virtual void OnDestroy()
        {
            _combinedNetwork?.Dispose();
            _roadNetworks?.Values.ForEach(n => n.Dispose());
        }

        public bool HasPoint(Vector2Int point, Road road = null) => getNetwork(road).DefaultPathfinding.HasPoint(point);

        public void Add(IEnumerable<Vector2Int> points, Road road)
        {
            var validPositions = _roadNetworks[road].Add(points, null);
            if (validPositions == null)
                return;

            _combinedNetwork.DefaultPathfinding.Add(validPositions);
            _combinedNetwork.BlockedPathfinding.Add(validPositions);
        }

        public void Register(IEnumerable<Vector2Int> points, Road road)
        {
            _roadNetworks[road].Register(points);
            _combinedNetwork.Register(points);
        }
        public void Deregister(IEnumerable<Vector2Int> points, Road road)
        {
            _roadNetworks[road].Deregister(points);
            _combinedNetwork.Deregister(points);
        }

        public void RegisterSwitch(Vector2Int point, Road roadEntry, Road roadExit)
        {
            var networkEntry = _roadNetworks[roadEntry];
            var networkExit = _roadNetworks[roadExit];

            networkEntry.RegisterSwitch(point, networkExit);
            networkExit.RegisterSwitch(point, networkEntry);

            _combinedNetwork.DefaultPathfinding.Add(point);
            _combinedNetwork.BlockedPathfinding.Add(point);
        }
        public void RegisterSwitch(Vector2Int entry, Vector2Int point, Vector2Int exit, Road roadEntry, Road roadExit)
        {
            var networkEntry = _roadNetworks[roadEntry];
            var networkExit = _roadNetworks[roadExit];

            networkEntry.RegisterSwitch(entry, point, exit, networkExit);
            networkExit.RegisterSwitch(exit, point, entry, networkEntry);

            _combinedNetwork.DefaultPathfinding.Add(point);
            _combinedNetwork.BlockedPathfinding.Add(point);
        }

        public void Block(IEnumerable<Vector2Int> points, Road road = null)
        {
            _combinedNetwork.Block(points);

            if (road == null)
                _roadNetworks.Values.ForEach(m => m.Block(points));
            else
                _roadNetworks[road].Block(points);
        }
        public void Unblock(IEnumerable<Vector2Int> points, Road road = null)
        {
            _combinedNetwork.Unblock(points);

            if (road == null)
                _roadNetworks.Values.ForEach(m => m.Unblock(points));
            else
                _roadNetworks[road].Unblock(points);
        }

        public void BlockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags, Road road = null)
        {
            _combinedNetwork.BlockTags(points, tags);

            if (road == null)
                _roadNetworks.Values.ForEach(m => m.BlockTags(points, tags));
            else
                _roadNetworks[road].BlockTags(points, tags);
        }
        public void UnblockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags, Road road = null)
        {
            _combinedNetwork.UnblockTags(points, tags);

            if (road == null)
                _roadNetworks.Values.ForEach(m => m.UnblockTags(points, tags));
            else
                _roadNetworks[road].UnblockTags(points, tags);
        }

        public void Remove(IEnumerable<Vector2Int> points)
        {
            _roadNetworks.Values.ForEach(r => r.Remove(points));
        }

        public IEnumerable<Vector2Int> GetPoints() => _combinedNetwork.DefaultPathfinding.GetPoints();
        public bool HasPoint(Vector2Int point) => _combinedNetwork.DefaultPathfinding.HasPoint(point);

        public void CheckLayers(IEnumerable<Vector2Int> points) => _roadNetworks.Values.ForEach(r => r.CheckLayers(points));

        public bool CheckRequirement(Vector2Int point, RoadRequirement requirement)
        {
            if (!_combinedNetwork.HasPoint(point))
                return requirement.Check(point, null, null);

            foreach (var network in _roadNetworks.Values)
            {
                if (network.TryGetRoad(point, out Road road, out string stage))
                {
                    return requirement.Check(point, road, stage);
                }
            }

            return false;
        }

        public string GetName() => "Road";

        public bool HasPoint(Vector2Int point, object tag = null) => getNetwork(tag).DefaultPathfinding.HasPoint(point, tag);
        public bool HasPointBlocked(Vector2Int point, object tag = null) => getNetwork(tag).BlockedPathfinding.HasPoint(point, tag);

        public WalkingPath FindPath(Vector2Int[] starts, Vector2Int[] targets, object tag) => getNetwork(tag).DefaultPathfinding.FindPath(starts, targets, tag);
        public WalkingPath FindPathBlocked(Vector2Int[] starts, Vector2Int[] targets, object tag) => getNetwork(tag).BlockedPathfinding.FindPath(starts, targets, tag);

        public PathQuery FindPathQuery(Vector2Int[] starts, Vector2Int[] targets, object tag) => getNetwork(tag).DefaultPathfinding.FindPathQuery(starts, targets, tag);
        public PathQuery FindPathQueryBlocked(Vector2Int[] starts, Vector2Int[] targets, object tag) => getNetwork(tag).BlockedPathfinding.FindPathQuery(starts, targets, tag);

        public void RegisterLink(IGridLink link, object tag)
        {
            var network = getNetwork(tag);
            network.RegisterLink(link);
            if (network != _combinedNetwork)
                _combinedNetwork.RegisterLink(link);
        }
        public void DeregisterLink(IGridLink link, object tag)
        {
            var network = getNetwork(tag);
            network.DeregisterLink(link);
            if (network != _combinedNetwork)
                _combinedNetwork.DeregisterLink(link);
        }
        public IEnumerable<IGridLink> GetLinks(Vector2Int start, object tag) => getNetwork(tag).GetLinks(start);
        public IGridLink GetLink(Vector2Int start, Vector2Int end, object tag) => getNetwork(tag).GetLink(start, end);

        protected abstract Dictionary<Road, RoadNetwork> createNetworks();

        private RoadNetwork getNetwork(object tag)
        {
            Road road = null;
            if (tag is Road r)
                road = r;
            else if (tag is WalkerInfo info && info.PathTag is Road ro)
                road = ro;

            if (road && _roadNetworks.ContainsKey(road))
                return _roadNetworks[road];
            return _combinedNetwork;
        }

        #region Saving
        [Serializable]
        public class MultiRoadsData
        {
            public RoadNetwork.RoadsData[] Networks;
        }

        public string SaveData()
        {
            return JsonUtility.ToJson(new MultiRoadsData()
            {
                Networks = _roadNetworks.Values.Select(r => r.SaveData()).ToArray()
            });
        }

        public void LoadData(string json)
        {
            var multiRoadsData = JsonUtility.FromJson<MultiRoadsData>(json);

            foreach (var network in multiRoadsData.Networks)
            {
                _roadNetworks.Values.FirstOrDefault(n => n.Road.Key == network.Key)?.LoadData(network);

                foreach (var road in network.Roads)
                {
                    _combinedNetwork.DefaultPathfinding.Add(road.Positions);
                    _combinedNetwork.BlockedPathfinding.Add(road.Positions.Except(_combinedNetwork.Blocked));
                }
            }
        }
        #endregion
    }
}