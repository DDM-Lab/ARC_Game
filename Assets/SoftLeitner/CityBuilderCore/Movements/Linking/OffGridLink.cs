using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// creates a link between two points in grid and road pathfinding<br/>
    /// grid links are the grid pathfinding equivalent to off mesh links in navmesh<br/>
    /// basically a grid link registers a link between two points in the pathfinding<br/>
    /// when moving between these two points the linker determines how a walker moves<br/>
    /// this can be used for special movement on bridges, ramps, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_off_grid_link.html")]
    public class OffGridLink : MonoBehaviour, IGridLink
    {
        [Tooltip("whether walkers can only move from start to end or both ways")]
        public bool Bidirectional = true;
        [Tooltip("cost of moving across the link in pathfinding(points moved x 10)")]
        public int Cost = 10;
        [Tooltip("distance of the link used when moving, increasing distance slows down the walker")]
        public float Distance = 2;
        [Tooltip("transform positioned at the desired start of the link, the link will reregister itself if the transform is moved to a different point")]
        public Transform StartTransform;
        [Tooltip("transform positioned at the desired end of the link, the link will reregister itself if the transform is moved to a different point")]
        public Transform EndTransform;
        [Tooltip("additional points walkers move across when walking the link")]
        public Transform[] Path;
        [Tooltip("whether the link should link roads")]
        public bool LinkRoad = true;
        [Tooltip("whether the link should link map points")]
        public bool LinkMapGrid = true;
        [Tooltip("used to provide additional info when the link is registered, for instance the Road when the link should only be registered with a specific road network")]
        public Object Tag;

        bool IGridLink.Bidirectional => Bidirectional;
        int IGridLink.Cost => getCost();
        float IGridLink.Distance => getDistance();

        public Vector2Int StartPoint => _gridPositions.Value.GetGridPoint(StartTransform.position);
        public Vector2Int EndPoint => _gridPositions.Value.GetGridPoint(EndTransform.position);

        public Vector3 StartPosition => _gridPositions.Value.GetWorldPosition(StartTransform.position);
        public Vector3 EndPosition => _gridPositions.Value.GetWorldPosition(EndTransform.position);

        private LazyDependency<IGridPositions> _gridPositions = new LazyDependency<IGridPositions>();
        private IGridHeights _heights;

        private Vector2Int _registeredStartPoint, _registeredEndPoint;
        private Vector3[] _pathForward, _pathBack;
        private float _pathLength;

        protected virtual void Start()
        {
            register();
            initialize();
        }
        protected virtual void Update()
        {
            if (_registeredStartPoint != StartPoint || _registeredEndPoint != EndPoint)
                Reregister();
        }
        protected virtual void OnDestroy()
        {
            if (!gameObject.scene.isLoaded)
                return;
            if (Dependencies.GetOptional<IGameSaver>()?.IsLoading == true)
                return;

            deregister();
        }
        protected virtual void OnDrawGizmosSelected()
        {
            if (Path == null)
                return;

            if (Path.Length == 0)
            {
                if (StartTransform && EndTransform)
                    Gizmos.DrawLine(StartTransform.position, EndTransform.position);
            }
            else
            {
                Gizmos.color = Color.red;
                if (StartTransform && Path.First())
                    Gizmos.DrawLine(StartTransform.position, Path.First().position);
                for (int i = 1; i < Path.Length; i++)
                {
                    if (Path[i - 1] && Path[i])
                        Gizmos.DrawLine(Path[i - 1].position, Path[i].position);
                }
                if (Path.Last() && EndTransform)
                    Gizmos.DrawLine(Path.Last().position, EndTransform.position);
            }
        }

        public void Reregister()
        {
            deregister();
            register();
        }

        protected virtual void register()
        {
            if (LinkRoad)
                Dependencies.Get<IRoadGridLinker>().RegisterLink(this, Tag);
            if (LinkMapGrid)
                Dependencies.Get<IMapGridLinker>().RegisterLink(this, Tag);

            _registeredStartPoint = StartPoint;
            _registeredEndPoint = EndPoint;
        }
        protected virtual void deregister()
        {
            if (LinkRoad)
                Dependencies.Get<IRoadGridLinker>().DeregisterLink(this, Tag);
            if (LinkMapGrid)
                Dependencies.Get<IMapGridLinker>().DeregisterLink(this, Tag);
        }

        protected virtual void initialize()
        {
            _heights = Dependencies.GetOptional<IGridHeights>();

            var path = Path.Select(p => _gridPositions.Value.GetPositionFromCenter(p.position)).Prepend(StartPosition).Append(EndPosition);

            _pathForward = path.ToArray();
            _pathBack = path.Reverse().ToArray();
            _pathLength = _pathForward.GetDistance();
        }

        protected virtual int getCost() => Cost;
        protected virtual float getDistance() => Distance;

        public virtual void Walk(Walker walker, float moved, Vector2Int start)
        {
            Vector3 last, next;
            Vector3[] path = null;

            if (start == StartPoint)
            {
                last = StartPosition;
                next = EndPosition;
                path = _pathForward;
            }
            else
            {
                last = EndPosition;
                next = StartPosition;
                path = _pathBack;
            }

            Vector3 position = walker.transform.position;
            if (path == null)
            {
                position = Vector3.Lerp(last, next, moved / getDistance());
            }
            else
            {
                moved = moved / getDistance() * _pathLength;
                for (int i = 1; i < path.Length; i++)
                {
                    var segment = Vector3.Distance(path[i - 1], path[i]);

                    if (segment > moved)
                    {
                        position = Vector3.Lerp(path[i - 1], path[i], moved / segment);
                        walker.onDirectionChanged(path[i] - path[i - 1]);
                        break;
                    }
                    else
                    {
                        moved -= segment;
                    }
                }
            }

            walker.transform.position = position;
            _heights?.ApplyHeight(walker.Pivot, walker.PathType, walker.HeightOverride);
        }
    }
}
