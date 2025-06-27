using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;
using UnityEngine;

namespace CityBuilderCore
{
    public class NavMeshPathfinding : IMapPathfinder
    {
        private class NavMeshPathfindingQuery : PathQuery
        {
            public NavMeshPathfinding Pathfinding { get; set; }

            public Vector2Int[] StartPoints { get; set; }
            public Vector2Int[] TargetPoints { get; set; }
            public object Tag { get; set; }

            public bool IsComplete { get; set; }
            public WalkingPath Result { get; set; }

            public override void Cancel() { IsComplete = true; }
            public override WalkingPath Complete()
            {
                if (!IsComplete)
                    Pathfinding.calculate(this);
                return Result;
            }
        }

        private int _areaMask;
        private bool _allowInvalidPath;

        private IMap _map;
        private IGridPositions _gridPositions;
        private IGridHeights _gridHeights;

        private Queue<NavMeshPathfindingQuery> _queue = new Queue<NavMeshPathfindingQuery>();

        public void Initialize(int areaMask, bool allowInvalidPath)
        {
            _areaMask = areaMask;
            _allowInvalidPath = allowInvalidPath;

            _map = Dependencies.Get<IMap>();
            _gridPositions = Dependencies.Get<IGridPositions>();
            _gridHeights = Dependencies.GetOptional<IGridHeights>();
        }

        public void Calculate(int maxCalculations = PathQuery.DEFAULT_MAX_CALCULATIONS)
        {
            int calculated = 0;
            while (_queue.Count > 0 && calculated < maxCalculations)
            {
                var query = _queue.Dequeue();
                if (query.IsComplete)
                    continue;
                calculate(query);
                calculated++;
            }
        }
        private void calculate(NavMeshPathfindingQuery query)
        {
            query.Result = FindPath(query.StartPoints, query.TargetPoints, query.Tag);
            query.IsComplete = true;
        }

        public bool HasPoint(Vector2Int point, object tag = null)
        {
            var position = _gridPositions.GetWorldCenterPosition(point);

            if (_gridHeights != null)
                position = _gridHeights.ApplyHeight(position, PathType.Map);

            var areaMask = _areaMask;
            if (tag is WalkerAreaMask walkerAreaMask)
            {
                areaMask = walkerAreaMask.AreaMask;
            }

            return NavMesh.SamplePosition(position, out _, 1f, areaMask);
        }

        public WalkingPath FindPath(Vector2Int[] startPoints, Vector2Int[] targetPoints, object tag = null)
        {
            if (startPoints.Length == 0 || targetPoints.Length == 0)
                return null;

            var startPosition = startPoints[0];
            var targetPosition = targetPoints.OrderBy(p => Vector2Int.Distance(startPosition, p)).First();

            var path = new NavMeshPath();

            var areaMask = _areaMask;
            if (tag is WalkerAreaMask walkerAreaMask)
            {
                areaMask = walkerAreaMask.AreaMask;
            }

            var worldStartPosition = _gridPositions.GetWorldCenterPosition(startPosition);
            var worldTargetPosition = _gridPositions.GetWorldCenterPosition(targetPosition);

            if (_gridHeights != null)
            {
                worldStartPosition = _gridHeights.ApplyHeight(worldStartPosition, PathType.Map);
                worldTargetPosition = _gridHeights.ApplyHeight(worldTargetPosition, PathType.Map);
            }
            
            NavMesh.CalculatePath(worldStartPosition, worldTargetPosition, areaMask, path);

            if (path.status == NavMeshPathStatus.PathComplete)
            {
                return new WalkingPath(path.corners.Select(c =>
                {
                    if (_map.IsXY)
                        c = new Vector3(c.x, c.y, 0f);
                    else
                        c = new Vector3(c.x, 0f, c.z);

                    return _gridPositions.GetPositionFromCenter(c);
                }).ToArray());
            }
            else
            {
                if (_allowInvalidPath)
                    return new WalkingPath(new[] { startPosition, targetPosition });
                else
                    return null;
            }
        }

        public PathQuery FindPathQuery(Vector2Int[] starts, Vector2Int[] targets, object tag = null)
        {
            var query = new NavMeshPathfindingQuery()
            {
                Pathfinding = this,
                StartPoints = starts,
                TargetPoints = targets,
                Tag = tag
            };
            _queue.Enqueue(query);
            return query;
        }

    }
}
