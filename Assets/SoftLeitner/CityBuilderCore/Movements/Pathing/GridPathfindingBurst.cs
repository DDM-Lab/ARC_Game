using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CityBuilderCore
{
    public class GridPathfindingBurst : GridPathfindingBase
    {
        private class UnnecessaryPathQuery : PathQuery
        {
            private WalkingPath _path;

            public UnnecessaryPathQuery(WalkingPath path)
            {
                _path = path;
            }

            public override void Cancel() { }
            public override WalkingPath Complete() => _path;
        }

        private class GridPathfindingBurstQuery : PathQuery
        {
            public bool IsCompleted => _handle.IsCompleted;
            public bool IsCanceled {  get; private set; }

            private GridPathfindingBurst _owner;
            private FindPathJob _job;
            private JobHandle _handle;
            private bool _allowInvalid;

            public GridPathfindingBurstQuery(GridPathfindingBurst owner, FindPathJob job, bool allowInvalid)
            {
                _owner = owner;
                _job = job;
                _handle = job.Schedule();
                _allowInvalid = allowInvalid;
            }

            public override void Cancel() { IsCanceled = true; }
            public override WalkingPath Complete()
            {
                _owner._queries.Remove(this);
                _handle.Complete();
                return createPath(_job, _allowInvalid);
            }

            public void Dispose()
            {
                _handle.Complete();
                _job.Dispose();
            }
        }

        private NativeList<int2> _points;
        private HashSet<Vector2Int> _pointsSet;

        private NativeList<int2> _linkStarts;
        private NativeList<int> _linkCosts;
        private NativeList<int2> _linkEnds;

        private NativeArray<int2> _neighbours;
        private bool _isHex;

        private Dictionary<object, NativeList<int2>> _blocked;
        private NativeList<int2> _none;

        private List<GridPathfindingBurstQuery> _queries = new List<GridPathfindingBurstQuery>();

        public GridPathfindingBurst()
        {
            _points = new NativeList<int2>(Allocator.Persistent);
            _pointsSet = new HashSet<Vector2Int>();

            _linkStarts = new NativeList<int2>(Allocator.Persistent);
            _linkCosts = new NativeList<int>(Allocator.Persistent);
            _linkEnds = new NativeList<int2>(Allocator.Persistent);

            _none = new NativeList<int2>(0, Allocator.Persistent);
        }

        public override void Calculate(int maxCalculations = 16)
        {
            //actual calculations are done in burst
            //we're only doing cleanup here

            for (int i = _queries.Count - 1; i >= 0; i--)
            {
                if (_queries[i].IsCanceled && _queries[i].IsCompleted)
                {
                    _queries[i].Dispose();
                    _queries.RemoveAt(i);//cleanup canceled jobs
                }
            }
        }

        public override void Add(Vector2Int point)
        {
            if (_pointsSet.Contains(point))
                return;
            _points.Add(new int2(point.x, point.y));
            _pointsSet.Add(point);
        }
        public override void Remove(Vector2Int point)
        {
            var index = _points.IndexOf(new int2(point.x, point.y));
            if (index >= 0)
                _points.RemoveAtSwapBack(index);
            _pointsSet.Remove(point);
        }
        public override void Clear()
        {
            _points.Clear();
            _pointsSet.Clear();
        }

        public override void AddLink(IGridLink link)
        {
            var start = new int2(link.StartPoint.x, link.StartPoint.y);
            var end = new int2(link.EndPoint.x, link.EndPoint.y);

            _linkStarts.Add(start);
            _linkCosts.Add(link.Cost);
            _linkEnds.Add(end);

            if (link.Bidirectional)
            {
                _linkStarts.Add(end);
                _linkCosts.Add(link.Cost);
                _linkEnds.Add(start);
            }
        }
        public override void RemoveLink(IGridLink link)
        {
            var start = new int2(link.StartPoint.x, link.StartPoint.y);
            var end = new int2(link.EndPoint.x, link.EndPoint.y);

            for (int i = _linkStarts.Length - 1; i >= 0; i--)
            {
                if (_linkStarts[i].Equals(start) && _linkEnds[i].Equals(end))
                {
                    _linkStarts.RemoveAtSwapBack(i);
                    _linkCosts.RemoveAtSwapBack(i);
                    _linkEnds.RemoveAtSwapBack(i);
                }
            }

            if (link.Bidirectional)
            {
                for (int i = _linkStarts.Length - 1; i >= 0; i--)
                {
                    if (_linkStarts[i].Equals(end) && _linkEnds[i].Equals(start))
                    {
                        _linkStarts.RemoveAtSwapBack(i);
                        _linkCosts.RemoveAtSwapBack(i);
                        _linkEnds.RemoveAtSwapBack(i);
                    }
                }

            }
        }

        public override void AddSwitch(Vector2Int point, GridPathfindingBase grid)
        {
            throw new NotImplementedException("Road Switching only works between grids using Default Pathfinding, Burst is not supported!");
        }
        public override void AddSwitch(Vector2Int entry, Vector2Int point, Vector2Int exit, GridPathfindingBase grid)
        {
            throw new NotImplementedException("Road Switching only works between grids using Default Pathfinding, Burst is not supported!");
        }

        public override void BlockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags)
        {
            if (_blocked == null)
                _blocked = new Dictionary<object, NativeList<int2>>();

            foreach (var tag in tags)
            {
                if (!_blocked.ContainsKey(tag))
                    _blocked.Add(tag, new NativeList<int2>(Allocator.Persistent));
                points.ForEach(p => _blocked[tag].Add(new int2(p.x, p.y)));
            }
        }
        public override void UnblockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags)
        {
            if (_blocked == null)
                return;

            foreach (var tag in tags)
            {
                if (!_blocked.ContainsKey(tag))
                    continue;

                var list = _blocked[tag];
                foreach (var point in points)
                {
                    var index = list.IndexOf(new int2(point.x, point.y));
                    if (index < 0)
                        continue;

                    list.RemoveAtSwapBack(index);
                }
            }
        }

        public override IEnumerable<Vector2Int> GetPoints() => _pointsSet;
        public override bool HasPoint(Vector2Int point, object tag = null)
        {
            if (!_pointsSet.Contains(point))
                return false;

            if (tag != null && _blocked != null && _blocked.ContainsKey(tag))
            {
                var blocks = _blocked[tag];
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (blocks[i].x == point.x && blocks[i].y == point.y)
                        return false;
                }
            }

            return true;
        }

        public override WalkingPath FindPath(Vector2Int[] starts, Vector2Int[] targets, object tag = null)
        {
            for (int i = 0; i < starts.Length; i++)
            {
                if (targets.Contains(starts[i]))
                {
                    return new WalkingPath(new Vector2Int[] { starts[i] });
                }
            }

            var job = findPath(starts, targets, tag);
            job.Schedule().Complete();
            return createPath(job, AllowInvalid);
        }
        public override PathQuery FindPathQuery(Vector2Int[] starts, Vector2Int[] targets, object tag = null)
        {
            for (int i = 0; i < starts.Length; i++)
            {
                if (targets.Contains(starts[i]))
                {
                    return new UnnecessaryPathQuery(new WalkingPath(new Vector2Int[] { starts[i] }));
                }
            }

            var query = new GridPathfindingBurstQuery(this, findPath(starts, targets, tag), AllowInvalid);
            _queries.Add(query);
            return query;
        }

        public override void Dispose()
        {
            _queries.ForEach(q => q.Dispose());

            _points.Dispose();

            _linkStarts.Dispose();
            _linkCosts.Dispose();
            _linkEnds.Dispose();

            if (_neighbours.Length > 0)
                _neighbours.Dispose();

            _blocked?.Values.ForEach(b => b.Dispose());
            _none.Dispose();
        }

        private FindPathJob findPath(Vector2Int[] starts, Vector2Int[] targets, object tag = null)
        {
            if (_neighbours.Length == 0)
            {
                if (PathHelper.IsMapHex)
                {
                    _isHex = true;

                    _neighbours = new NativeArray<int2>(8, Allocator.Persistent);
                    _neighbours[0] = new int2(+1, +0); // Right
                    _neighbours[1] = new int2(-1, +1); // Left Up EVEN
                    _neighbours[2] = new int2(+0, +1); // Up
                    _neighbours[3] = new int2(+1, +1); // Right Up UNEVEN
                    _neighbours[4] = new int2(-1, +0); // Left
                    _neighbours[5] = new int2(-1, -1); // Left Down EVEN
                    _neighbours[6] = new int2(+0, -1); // Down
                    _neighbours[7] = new int2(+1, -1); // Right Down UNEVEN
                }
                else
                {
                    if (AllowDiagonal)
                    {
                        _neighbours = new NativeArray<int2>(8, Allocator.Persistent);
                        _neighbours[0] = new int2(-1, 0); // Left
                        _neighbours[1] = new int2(+1, 0); // Right
                        _neighbours[2] = new int2(0, +1); // Up
                        _neighbours[3] = new int2(0, -1); // Down
                        _neighbours[4] = new int2(+1, +1); // Right Up
                        _neighbours[5] = new int2(+1, -1); // Right Down
                        _neighbours[6] = new int2(-1, -1); // Left Down
                        _neighbours[7] = new int2(-1, +1); // Left Up
                    }
                    else
                    {
                        _neighbours = new NativeArray<int2>(4, Allocator.Persistent);
                        _neighbours[0] = new int2(-1, 0); // Left
                        _neighbours[1] = new int2(+1, 0); // Right
                        _neighbours[2] = new int2(0, +1); // Up
                        _neighbours[3] = new int2(0, -1); // Down
                    }
                }
            }

            var s = new NativeArray<int2>(starts.Length, Allocator.Persistent);
            var t = new NativeArray<int2>(targets.Length, Allocator.Persistent);

            for (int i = 0; i < starts.Length; i++)
            {
                s[i] = new int2(starts[i].x, starts[i].y);
            }
            for (int i = 0; i < targets.Length; i++)
            {
                t[i] = new int2(targets[i].x, targets[i].y);
            }

            NativeList<int2> blocked;
            if (_blocked != null && _blocked.ContainsKey(tag))
                blocked = _blocked[tag];
            else
                blocked = _none;

            return new FindPathJob()
            {
                Points = _points,
                Blocked = blocked,
                Neighbours = _neighbours,
                IsHex = _isHex,

                LinkStarts = _linkStarts,
                LinkCosts = _linkCosts,
                LinkEnds = _linkEnds,

                Starts = s,
                Targets = t,

                Path = new NativeList<int2>(Allocator.Persistent)
            };
        }

        private static WalkingPath createPath(FindPathJob job, bool allowInvalid)
        {
            Vector2Int[] points = null;
            if (job.Path.Length > 0)
            {
                points = new Vector2Int[job.Path.Length];
                for (int i = 0; i < job.Path.Length; i++)
                {
                    var p = job.Path[job.Path.Length - 1 - i];
                    points[i] = new Vector2Int(p.x, p.y);
                }
            }
            else if (allowInvalid)
            {
                points = new Vector2Int[]
                {
                    new Vector2Int(job.Starts[0].x, job.Starts[0].y),
                    new Vector2Int(job.Targets[0].x, job.Targets[0].y)
                };
            }

            job.Dispose();

            if (points == null)
                return null;
            return new WalkingPath(points);
        }

        [BurstCompile]
        public struct FindPathJob : IJob
        {
            public struct PathNode
            {
                public int GCost;
                public int HCost;
                public int FCost => GCost + HCost;

                public int ParentIndex;
            }

            [ReadOnly]
            public NativeList<int2> Points;
            [ReadOnly]
            public NativeList<int2> Blocked;
            [ReadOnly]
            public NativeArray<int2> Neighbours;
            [ReadOnly]
            public bool IsHex;

            [ReadOnly]
            public NativeList<int2> LinkStarts;
            [ReadOnly]
            public NativeList<int> LinkCosts;
            [ReadOnly]
            public NativeList<int2> LinkEnds;

            [ReadOnly]
            public NativeArray<int2> Starts;
            [ReadOnly]
            public NativeArray<int2> Targets;

            [WriteOnly]
            public NativeList<int2> Path;

            public void Execute()
            {
                var nodes = new NativeArray<PathNode>(Points.Length, Allocator.Temp);

                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = new PathNode();

                    node.GCost = int.MaxValue;
                    node.HCost = getNodeDistance(Points[i], Targets);

                    node.ParentIndex = -1;

                    nodes[i] = node;
                }

                var endNodeIndex = -1;

                var open = new NativeList<int>(Allocator.Temp);
                var closed = new NativeList<int>(Allocator.Temp);

                for (int i = 0; i < Starts.Length; i++)
                {
                    var startNodeIndex = Points.IndexOf(Starts[i]);
                    var startNode = nodes[startNodeIndex];
                    startNode.GCost = 0;
                    nodes[startNodeIndex] = startNode;
                    open.Add(startNodeIndex);
                }

                while (open.Length > 0)
                {
                    var currentNodeIndex = GetLowestCostFNodeIndex(open, nodes);
                    var currentNodePoint = Points[currentNodeIndex];
                    var currentNode = nodes[currentNodeIndex];

                    if (Targets.Contains(currentNodePoint))
                    {
                        endNodeIndex = currentNodeIndex;//TARGET REACHED
                        break;
                    }

                    // Remove current node from Open List
                    for (int i = 0; i < open.Length; i++)
                    {
                        if (open[i] == currentNodeIndex)
                        {
                            open.RemoveAtSwapBack(i);
                            break;
                        }
                    }

                    closed.Add(currentNodeIndex);

                    for (int i = 0; i < Neighbours.Length; i++)
                    {
                        if (IsHex)
                        {
                            if (currentNodePoint.y % 2 == 0)
                            {
                                //EVEN
                                if (i == 3 || i == 7)
                                    continue;//skip uneven
                            }
                            else
                            {
                                //UNEVEN
                                if (i == 1 || i == 5)
                                    continue;//skip even
                            }
                        }

                        var neighbourPosition = currentNodePoint + Neighbours[i];
                        var neighbourNodeIndex = Points.IndexOf(neighbourPosition);

                        if (neighbourNodeIndex < 0)
                            continue;

                        if (closed.Contains(neighbourNodeIndex))
                            continue;// already searched this node

                        if (Blocked.Contains(neighbourPosition))
                            continue;// blocked by tag

                        var neighbourNode = nodes[neighbourNodeIndex];

                        int tentativeGCost = currentNode.GCost + getNodeDistance(currentNodePoint, neighbourPosition);
                        if (tentativeGCost < neighbourNode.GCost)
                        {
                            neighbourNode.ParentIndex = currentNodeIndex;
                            neighbourNode.GCost = tentativeGCost;
                            nodes[neighbourNodeIndex] = neighbourNode;

                            if (!open.Contains(neighbourNodeIndex))
                                open.Add(neighbourNodeIndex);
                        }
                    }

                    for (int i = 0; i < LinkStarts.Length; i++)
                    {
                        if (LinkStarts[i].Equals(currentNodePoint))
                        {
                            var neighbourPosition = LinkEnds[i];
                            var neighbourNodeIndex = Points.IndexOf(neighbourPosition);

                            if (neighbourNodeIndex < 0)
                                continue;

                            if (closed.Contains(neighbourNodeIndex))
                                continue;// Already searched this node

                            var neighbourNode = nodes[neighbourNodeIndex];

                            int tentativeGCost = currentNode.GCost + LinkCosts[i];
                            if (tentativeGCost < neighbourNode.GCost)
                            {
                                neighbourNode.ParentIndex = currentNodeIndex;
                                neighbourNode.GCost = tentativeGCost;
                                nodes[neighbourNodeIndex] = neighbourNode;

                                if (!open.Contains(neighbourNodeIndex))
                                    open.Add(neighbourNodeIndex);
                            }
                        }
                    }
                }

                if (endNodeIndex >= 0)
                {
                    PathNode endNode = nodes[endNodeIndex];
                    int2 endNodePosition = Points[endNodeIndex];
                    if (endNode.ParentIndex >= 0)
                    {
                        Path.Add(endNodePosition);

                        PathNode currentNode = endNode;
                        while (currentNode.ParentIndex != -1)
                        {
                            PathNode parentNode = nodes[currentNode.ParentIndex];
                            int2 parentNodePosition = Points[currentNode.ParentIndex];
                            Path.Add(parentNodePosition);
                            currentNode = parentNode;
                        }
                    }
                }

                nodes.Dispose();
                open.Dispose();
                closed.Dispose();
            }

            public void Dispose()
            {
                Path.Dispose();
                Starts.Dispose();
                Targets.Dispose();
            }

            private static int getNodeDistance(int2 a, int2 b)
            {
                int dstX = Math.Abs(a.x - b.x);
                int dstY = Math.Abs(a.y - b.y);
                return (dstX > dstY) ?
                    COSTD * dstY + COSTH * (dstX - dstY) :
                    COSTD * dstX + COSTH * (dstY - dstX);
            }
            private static int getNodeDistance(int2 a, NativeArray<int2> targets)
            {
                int min = int.MaxValue;
                for (int i = 0; i < targets.Length; i++)
                {
                    int distance = getNodeDistance(a, targets[i]);
                    if (distance < min)
                        min = distance;
                }
                return min;
            }

            private int GetLowestCostFNodeIndex(NativeList<int> openList, NativeArray<PathNode> pathNodeArray)
            {
                int lowestCostIndex = openList[0];
                PathNode lowestCostPathNode = pathNodeArray[lowestCostIndex];
                for (int i = 1; i < openList.Length; i++)
                {
                    var openIndex = openList[i];
                    PathNode testPathNode = pathNodeArray[openIndex];
                    if (testPathNode.FCost < lowestCostPathNode.FCost)
                    {
                        lowestCostIndex = openIndex;
                        lowestCostPathNode = testPathNode;

                    }
                }
                return lowestCostIndex;
            }
        }
    }
}
