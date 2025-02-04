using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// simple a* implementation for non diagonal pathfinding between equidistant points, used by road and mapGrid pathfinding
    /// </summary>
    public class GridPathfinding : GridPathfindingBase
    {
        private class GridPathfindingQuery : PathQuery
        {
            public GridPathfinding Pathfinding { get; set; }

            public Vector2Int[] StartPoints { get; set; }
            public Vector2Int[] TargetPoints { get; set; }
            public object Tag { get; set; }

            public bool IsComplete { get; set; }
            public WalkingPath Result { get; set; }

            public override void Cancel()
            {
                IsComplete = true;
            }
            public override WalkingPath Complete()
            {
                if (!IsComplete)
                    Pathfinding.calculate(this);
                return Result;
            }
        }

        private class Node
        {
            public GridPathfinding Grid { get; private set; }
            public Vector2Int Point { get; private set; }

            public int GCost;
            public int HCost;
            public Node Parent;

            public int FCost => GCost + HCost;

            public Node(GridPathfinding grid, Vector2Int point)
            {
                Grid = grid;
                Point = point;
            }

            public virtual IEnumerable<Node> GetNeighbours(bool isHex, object tag)
            {
                Node neighbour;

                if (isHex)
                {
                    bool isEven = Point.y % 2 == 0;

                    neighbour = addNodeNeighbour(1, 0, tag);
                    if (neighbour != null)
                        yield return neighbour;

                    if (isEven)
                    {
                        neighbour = addNodeNeighbour(-1, 1, tag);
                        if (neighbour != null)
                            yield return neighbour;
                    }
                    neighbour = addNodeNeighbour(0, 1, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    if (!isEven)
                    {
                        neighbour = addNodeNeighbour(1, 1, tag);
                        if (neighbour != null)
                            yield return neighbour;
                    }

                    neighbour = addNodeNeighbour(-1, 0, tag);
                    if (neighbour != null)
                        yield return neighbour;

                    if (isEven)
                    {
                        neighbour = addNodeNeighbour(-1, -1, tag);
                        if (neighbour != null)
                            yield return neighbour;
                    }
                    neighbour = addNodeNeighbour(0, -1, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    if (!isEven)
                    {
                        neighbour = addNodeNeighbour(1, -1, tag);
                        if (neighbour != null)
                            yield return neighbour;
                    }
                }
                else
                {
                    neighbour = addNodeNeighbour(1, 0, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(0, 1, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(-1, 0, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(0, -1, tag);
                    if (neighbour != null)
                        yield return neighbour;

                    if (Grid.AllowDiagonal)
                    {
                        neighbour = addNodeNeighbour(1, 1, tag);
                        if (neighbour != null)
                            yield return neighbour;
                        neighbour = addNodeNeighbour(1, -1, tag);
                        if (neighbour != null)
                            yield return neighbour;
                        neighbour = addNodeNeighbour(-1, -1, tag);
                        if (neighbour != null)
                            yield return neighbour;
                        neighbour = addNodeNeighbour(-1, 1, tag);
                        if (neighbour != null)
                            yield return neighbour;
                    }
                }
            }

            protected virtual bool isValidNeighbour(Vector2Int point) => true;

            private Node addNodeNeighbour(int x, int y, object tag)
            {
                if (x == 0 && y == 0)
                    return null;

                var point = new Vector2Int(Point.x + x, Point.y + y);
                if (!Grid._nodes.TryGetValue(point, out Node neighbour))
                    return null;

                if (!neighbour.isValidNeighbour(Point))
                    return null;

                if (Grid._tagBlocks != null && Grid._tagBlocks.ContainsKey(point) && Grid._tagBlocks[point].Contains(tag))
                    return null;

                return neighbour;
            }
        }

        private class SwitchNode : Node
        {
            public bool IsDirectional { get; private set; }
            public Vector2Int Entry { get; private set; }
            public Vector2Int Exit { get; private set; }
            public GridPathfinding ExitGrid { get; private set; }

            public SwitchNode(GridPathfinding entryGrid, Vector2Int point, GridPathfinding exitGrid) : base(entryGrid, point)
            {
                IsDirectional = false;
                ExitGrid = exitGrid;
            }
            public SwitchNode(GridPathfinding entryGrid, Vector2Int entry, Vector2Int point, Vector2Int exit, GridPathfinding exitGrid) : base(entryGrid, point)
            {
                IsDirectional = true;
                Entry = entry;
                Exit = exit;
                ExitGrid = exitGrid;
            }

            public override IEnumerable<Node> GetNeighbours(bool isHex, object tag)
            {
                if (IsDirectional)
                {
                    if (Grid._nodes.TryGetValue(Entry, out Node entryNeighbour))
                        yield return entryNeighbour;
                    if (ExitGrid._nodes.TryGetValue(Exit, out Node exitNeighbour))
                        yield return exitNeighbour;
                }
                else
                {
                    Node neighbour;

                    neighbour = addNodeNeighbour(1, 0, Grid, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(0, 1, Grid, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(-1, 0, Grid, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(0, -1, Grid, tag);
                    if (neighbour != null)
                        yield return neighbour;

                    neighbour = addNodeNeighbour(1, 0, ExitGrid, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(0, 1, ExitGrid, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(-1, 0, ExitGrid, tag);
                    if (neighbour != null)
                        yield return neighbour;
                    neighbour = addNodeNeighbour(0, -1, ExitGrid, tag);
                    if (neighbour != null)
                        yield return neighbour;
                }
            }

            protected override bool isValidNeighbour(Vector2Int point) => IsDirectional ? Entry == point : true;

            private Node addNodeNeighbour(int x, int y, GridPathfinding grid, object tag)
            {
                if (x == 0 && y == 0)
                    return null;

                var point = new Vector2Int(Point.x + x, Point.y + y);
                if (grid._nodes.TryGetValue(point, out Node neighbour))
                    return neighbour;

                if (Grid._tagBlocks != null && Grid._tagBlocks.ContainsKey(point) && Grid._tagBlocks[point].Contains(tag))
                    return null;

                return null;
            }
        }

        private class Link
        {
            public Vector2Int Target { get; private set; }
            public int Cost { get; private set; }

            public Link(Vector2Int target, int cost)
            {
                Target = target;
                Cost = cost;
            }
        }

        private Dictionary<Vector2Int, Node> _nodes = new Dictionary<Vector2Int, Node>();
        private Dictionary<Vector2Int, List<Link>> _links = new Dictionary<Vector2Int, List<Link>>();
        private Dictionary<Vector2Int, List<object>> _tagBlocks;
        private Queue<GridPathfindingQuery> _queue = new Queue<GridPathfindingQuery>();

        public override void Calculate(int maxCalculations = PathQuery.DEFAULT_MAX_CALCULATIONS)
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
        private void calculate(GridPathfindingQuery query)
        {
            query.Result = FindPath(query.StartPoints, query.TargetPoints, query.Tag);
            query.IsComplete = true;
        }

        public override void Add(Vector2Int point)
        {
            if (_nodes.ContainsKey(point))
                return;
            _nodes.Add(point, new Node(this, point));
        }
        public override void Remove(Vector2Int point)
        {
            _nodes.Remove(point);
        }
        public override void Clear()
        {
            _nodes.Clear();
        }

        public override void AddLink(IGridLink link)
        {
            if (!_links.ContainsKey(link.StartPoint))
                _links.Add(link.StartPoint, new List<Link>());
            _links[link.StartPoint].Add(new Link(link.EndPoint, link.Cost));

            if (link.Bidirectional)
            {
                if (!_links.ContainsKey(link.EndPoint))
                    _links.Add(link.EndPoint, new List<Link>());
                _links[link.EndPoint].Add(new Link(link.StartPoint, link.Cost));
            }
        }
        public override void RemoveLink(IGridLink link)
        {
            if (_links.ContainsKey(link.StartPoint))
            {
                _links[link.StartPoint].RemoveAll(l => l.Target == link.EndPoint);
                if (_links[link.StartPoint].Count == 0)
                    _links.Remove(link.StartPoint);
            }

            if (link.Bidirectional)
            {
                if (_links.ContainsKey(link.EndPoint))
                {
                    _links[link.EndPoint].RemoveAll(l => l.Target == link.StartPoint);
                    if (_links[link.EndPoint].Count == 0)
                        _links.Remove(link.EndPoint);
                }
            }
        }
        public override void AddSwitch(Vector2Int point, GridPathfindingBase other)
        {
            if (_nodes.ContainsKey(point))
                return;
            _nodes.Add(point, new SwitchNode(this, point, (GridPathfinding)other));
        }
        public override void AddSwitch(Vector2Int entry, Vector2Int point, Vector2Int exit, GridPathfindingBase other)
        {
            if (_nodes.ContainsKey(point))
                return;
            _nodes.Add(point, new SwitchNode(this, entry, point, exit, (GridPathfinding)other));
        }

        public override void BlockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags)
        {
            if (_tagBlocks == null)
                _tagBlocks = new Dictionary<Vector2Int, List<object>>();

            foreach (var point in points)
            {
                if (!_tagBlocks.ContainsKey(point))
                    _tagBlocks.Add(point, new List<object>());
                _tagBlocks[point].AddRange(tags);
            }
        }
        public override void UnblockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags)
        {
            if (_tagBlocks == null)
                return;

            foreach (var point in points)
            {
                if (!_tagBlocks.ContainsKey(point))
                    continue;
                tags.ForEach(t => _tagBlocks[point].Remove(t));
                if (_tagBlocks[point].Count == 0)
                    _tagBlocks.Remove(point);
            }

            if (_tagBlocks.Count == 0)
                _tagBlocks = null;
        }

        public override IEnumerable<Vector2Int> GetPoints() => _nodes.Keys;

        public override bool HasPoint(Vector2Int point, object tag = null)
        {
            if (!_nodes.ContainsKey(point))
                return false;

            if (tag != null && _tagBlocks != null && _tagBlocks.ContainsKey(point) && _tagBlocks[point].Contains(tag))
                return false;

            return true;
        }

        public override WalkingPath FindPath(Vector2Int[] starts, Vector2Int[] targets, object tag = null)
        {
            var nodes = findNodePath(starts, targets, tag);
            if (nodes == null)
            {
                if (AllowInvalid)
                    return new WalkingPath(new[] { starts[0], targets[0] });
                else
                    return null;
            }
            else
            {
                return new WalkingPath(nodes.Select(n => n.Point).ToArray());
            }
        }

        public override PathQuery FindPathQuery(Vector2Int[] starts, Vector2Int[] targets, object tag = null)
        {
            var query = new GridPathfindingQuery()
            {
                Pathfinding = this,
                StartPoints = starts,
                TargetPoints = targets,
                Tag = tag
            };
            _queue.Enqueue(query);
            return query;
        }

        public override void Dispose()
        {

        }

        private List<Node> findNodePath(Vector2Int[] startPositions, Vector2Int[] targetPositions, object tag = null)
        {
            List<Node> startNodes = new List<Node>();
            List<Node> targetNodes = new List<Node>();

            foreach (var startPosition in startPositions)
            {
                if (_nodes.TryGetValue(startPosition, out Node startNode))
                {
                    if (targetPositions.Contains(startPosition))
                        return new List<Node>() { startNode };
                    startNodes.Add(startNode);
                }
            }

            foreach (var targetPosition in targetPositions)
            {
                if (_nodes.TryGetValue(targetPosition, out Node targetNode))
                    targetNodes.Add(targetNode);
            }

            return findNodePath(startNodes, targetNodes, tag);
        }
        private List<Node> findNodePath(List<Node> startNodes, List<Node> targetNodes, object tag = null)
        {
            if (startNodes.Count == 0 || targetNodes.Count == 0)
                return null;

            var isHex = PathHelper.IsMapHex;

            var open = new List<Node>();
            var closed = new HashSet<Node>();

            open.AddRange(startNodes);

            while (open.Count > 0)
            {
                var currentNode = open[0];
                for (int i = 1; i < open.Count; i++)
                {
                    if (open[i].FCost < currentNode.FCost || open[i].FCost == currentNode.FCost && open[i].HCost < currentNode.HCost)
                    {
                        currentNode = open[i];
                    }
                }

                open.Remove(currentNode);
                closed.Add(currentNode);

                if (targetNodes.Contains(currentNode))//TARGET REACHED
                    return retraceNodePath(startNodes, currentNode);

                foreach (var neighbour in currentNode.GetNeighbours(isHex, tag))
                {
                    if (closed.Contains(neighbour))
                        continue;

                    int newMovementCostToNeighbour = currentNode.GCost + getNodeDistance(currentNode, neighbour);
                    if (newMovementCostToNeighbour < neighbour.GCost || !open.Contains(neighbour))
                    {
                        neighbour.GCost = newMovementCostToNeighbour;
                        neighbour.HCost = getNodeDistance(neighbour, targetNodes);
                        neighbour.Parent = currentNode;

                        if (!open.Contains(neighbour))
                            open.Add(neighbour);
                    }
                }

                if (_links.ContainsKey(currentNode.Point))
                {
                    foreach (var link in _links[currentNode.Point])
                    {
                        if (_nodes.TryGetValue(link.Target, out Node neighbour))
                        {
                            if (closed.Contains(neighbour))
                            {
                                continue;
                            }

                            var newMovementCostToNeighbour = currentNode.GCost + link.Cost;
                            if (newMovementCostToNeighbour < neighbour.GCost || !open.Contains(neighbour))
                            {
                                neighbour.GCost = newMovementCostToNeighbour;
                                neighbour.HCost = getNodeDistance(neighbour, targetNodes);
                                neighbour.Parent = currentNode;

                                if (!open.Contains(neighbour))
                                    open.Add(neighbour);
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static List<Node> retraceNodePath(List<Node> startNodes, Node endNode)
        {
            var path = new List<Node>();
            var currentNode = endNode;

            while (!startNodes.Contains(currentNode))
            {
                path.Add(currentNode);
                currentNode = currentNode.Parent;
            }
            path.Add(currentNode);
            path.Reverse();
            return path;
        }

        private static int getNodeDistance(Node nodeA, Node nodeB)
        {
            var dstX = System.Math.Abs(nodeA.Point.x - nodeB.Point.x);
            var dstY = System.Math.Abs(nodeA.Point.y - nodeB.Point.y);
            return (dstX > dstY) ?
                COSTD * dstY + COSTH * (dstX - dstY) :
                COSTD * dstX + COSTH * (dstY - dstX);
        }
        private static int getNodeDistance(Node node, List<Node> nodes)
        {
            return nodes.Min(n => getNodeDistance(node, n));
        }

    }
}