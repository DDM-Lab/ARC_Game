using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// manages one variant of structure pathing(there is only more than one if there are <see cref="StructurePathOption"/>s)<br/>
    /// used by <see cref="StructurePaths"/> which manages the structure pathing for <see cref="DefaultStructureManager"/>
    /// </summary>
    public class StructurePathManager
    {
        public GridPathfindingBase GridPathfinding { get; private set; }

        private int _level;
        private Object[] _groundOptions;
        private GridLinks _links;

        public StructurePathManager(GridPathfindingSettings pathfindingSettings, StructurePathOption option)
        {
            _level = option?.Level?.Value ?? 0;
            _groundOptions = option?.GroundOptions;
            _links = new GridLinks();

            GridPathfinding = pathfindingSettings.Create();
        }

        public void Dispose()
        {
            GridPathfinding.Dispose();
        }

        public void Add(IMap map, Vector2Int point)
        {
            if (checkPoint(map, point))
                GridPathfinding.Add(point);
        }

        public void CheckPoints(IStructureManager manager, IMap map, IEnumerable<Vector2Int> points)
        {
            foreach (var point in points)
            {
                if (checkPoint(map, point))
                {
                    if (manager.HasStructure(point, _level, false))
                        GridPathfinding.Remove(point);
                    else
                        GridPathfinding.Add(point);
                }
                else
                {
                    if (manager.HasStructure(point, _level, true))
                        GridPathfinding.Add(point);
                    else
                        GridPathfinding.Remove(point);
                }
            }
        }

        public void RegisterLink(IGridLink link)
        {
            GridPathfinding.AddLink(link);
            _links.Add(link);
        }
        public void DeregisterLink(IGridLink link)
        {
            GridPathfinding.RemoveLink(link);
            _links.Remove(link);
        }
        public IEnumerable<IGridLink> GetLinks(Vector2Int start) => _links.Get(start);
        public IGridLink GetLink(Vector2Int start, Vector2Int end) => _links.Get(start, end);

        private bool checkPoint(IMap map, Vector2Int point)
        {
            if (!map.IsWalkable(point))
                return false;
            if (_groundOptions != null && !map.CheckGround(point, _groundOptions))
                return false;
            return true;
        }
    }
}
