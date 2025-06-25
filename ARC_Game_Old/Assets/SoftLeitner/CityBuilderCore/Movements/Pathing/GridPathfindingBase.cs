using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    public abstract class GridPathfindingBase : IRoadPathfinder, IRoadPathfinderBlocked, IMapGridPathfinder
    {
        public const int COSTH = 10;
        public const int COSTD = 14;

        public bool AllowDiagonal { get; set; }
        public bool AllowInvalid { get; set; }

        public abstract void Calculate(int maxCalculations = PathQuery.DEFAULT_MAX_CALCULATIONS);

        public virtual void Add(IEnumerable<Vector2Int> points) => points.ForEach(p => Add(p));
        public abstract void Add(Vector2Int point);
        public virtual void Remove(IEnumerable<Vector2Int> points) => points.ForEach(p => Remove(p));
        public abstract void Remove(Vector2Int point);
        public abstract void Clear();

        public abstract void AddLink(IGridLink link);
        public abstract void RemoveLink(IGridLink link);
        public abstract void AddSwitch(Vector2Int point, GridPathfindingBase pathfinding);
        public abstract void AddSwitch(Vector2Int entry, Vector2Int point, Vector2Int exit, GridPathfindingBase pathfinding);

        public abstract void BlockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags);
        public abstract void UnblockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags);

        public abstract IEnumerable<Vector2Int> GetPoints();
        public abstract bool HasPoint(Vector2Int point, object tag = null);

        public abstract WalkingPath FindPath(Vector2Int[] starts, Vector2Int[] targets, object tag = null);
        public abstract PathQuery FindPathQuery(Vector2Int[] starts, Vector2Int[] targets, object tag = null);

        public abstract void Dispose();
    }
}