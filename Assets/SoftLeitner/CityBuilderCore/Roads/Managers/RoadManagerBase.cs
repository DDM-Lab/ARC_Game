using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for simple road managers that create a single road network out of any <see cref="Road"/> added<br/>
    /// this means walkers will be able to use any road, if you need seperate road networks per road use <see cref="RoadManagerBaseMulti"/><br/>
    /// the base class provides all the plumbing between the network and manager<br/>
    /// the implementations have to provide the actual <see cref="RoadNetwork"/> which determines how roads are visualized
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_road_manager_base.html")]
    public abstract class RoadManagerBase : MonoBehaviour, IRoadManager, IRoadGridLinker
    {
        public GridPathfindingSettings PathfindingSettings;
        [Tooltip("road used when a point is added but the road is not specified(needed for moving)")]
        public Road DefaultRoad;
        [Tooltip("determines which structures can reside in the same points as roads")]
        public StructureLevelMask Level;

        public Transform Root => transform;

        private RoadNetwork _network;

        protected virtual void Awake()
        {
            _network = createNetwork();

            Dependencies.Register<IRoadManager>(this);
            Dependencies.Register<IRoadPathfinder>(_network.DefaultPathfinding);
            Dependencies.Register<IRoadPathfinderBlocked>(_network.BlockedPathfinding);
            Dependencies.Register<IRoadGridLinker>(this);
        }

        protected virtual void Start()
        {
            _network.Initialize();
        }

        protected virtual void Update()
        {
            _network.Calculate();
        }

        protected virtual void OnDestroy()
        {
            _network?.Dispose();
        }

        public void Add(IEnumerable<Vector2Int> positions, Road road) => _network.Add(positions, road);

        public void Register(IEnumerable<Vector2Int> points, Road road) => _network.Register(points);
        public void Deregister(IEnumerable<Vector2Int> points, Road road) => _network.Deregister(points);

        public void RegisterSwitch(Vector2Int point, Road roadA, Road roadB) => throw new System.NotImplementedException("DefaultRoadManager does not support Road Transitions, use MultiRoadManager instead!");
        public void RegisterSwitch(Vector2Int entry, Vector2Int point, Vector2Int exit, Road roadEntry, Road roadExit) => throw new System.NotImplementedException("DefaultRoadManager does not support Road Transitions, use MultiRoadManager instead!");

        public void Block(IEnumerable<Vector2Int> points, Road road = null) => _network.Block(points);
        public void Unblock(IEnumerable<Vector2Int> points, Road road = null) => _network.Unblock(points);

        public void BlockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags, Road road = null) => _network.BlockTags(points, tags);
        public void UnblockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags, Road road = null) => _network.UnblockTags(points, tags);

        public bool CheckRequirement(Vector2Int point, RoadRequirement requirement)
        {
            if (!_network.HasPoint(point))
                return requirement.Check(point, null, null);

            if (_network.TryGetRoad(point, out Road road, out string stage))
            {
                return requirement.Check(point, road, stage);
            }
            else
            {
                return false;
            }
        }

        public void RegisterLink(IGridLink link, object tag) => _network.RegisterLink(link);
        public void DeregisterLink(IGridLink link, object tag) => _network.DeregisterLink(link);
        public IEnumerable<IGridLink> GetLinks(Vector2Int start, object tag) => _network.GetLinks(start);
        public IGridLink GetLink(Vector2Int start, Vector2Int end, object tag) => _network.GetLink(start, end);

        protected abstract RoadNetwork createNetwork();

        #region Saving
        public string SaveData()
        {
            return JsonUtility.ToJson(_network.SaveData());
        }

        public void LoadData(string json)
        {
            _network.LoadData(JsonUtility.FromJson<RoadNetwork.RoadsData>(json));
        }
        #endregion
    }
}