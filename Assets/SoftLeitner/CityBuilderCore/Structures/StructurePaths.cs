using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// manages the different variants of mag grid pathing for <see cref="DefaultStructureManager"/><br/>
    /// there is only one variant if there are no special <see cref="StructurePathOption"/>s defined
    /// </summary>
    public class StructurePaths : IMapGridPathfinder, IMapGridLinker
    {
        private StructurePathManager _defaultManager;
        private Dictionary<object, StructurePathManager> _optionManagers;

        private IMap _map;

        private Dictionary<IGridLink, object> _prematureLinks = new Dictionary<IGridLink, object>();

        public void Initialize(GridPathfindingSettings pathfindingSettings, StructurePathOption[] options)
        {
            if (_map != null)
                return;

            _map = Dependencies.Get<IMap>();
            _defaultManager = new StructurePathManager(pathfindingSettings, null);

            if (options != null && options.Length > 0)
            {
                _optionManagers = new Dictionary<object, StructurePathManager>();
                foreach (var option in options)
                {
                    _optionManagers.Add(option.Tag, new StructurePathManager(pathfindingSettings, option));
                }
            }

            for (int x = 0; x < _map.Size.x; x++)
            {
                for (int y = 0; y < _map.Size.y; y++)
                {
                    var point = new Vector2Int(x, y);

                    _defaultManager.Add(_map, point);

                    if (_optionManagers != null)
                    {
                        foreach (var optionManager in _optionManagers.Values)
                        {
                            optionManager.Add(_map, point);
                        }
                    }
                }
            }

            foreach (var l in _prematureLinks)
            {
                RegisterLink(l.Key, l.Value);
            }
            _prematureLinks = null;
        }

        public void Dispose()
        {
            _defaultManager?.Dispose();
            _optionManagers?.Values.ForEach(m => m.Dispose());
        }

        public void Add(Vector2Int point)
        {
            var map = Dependencies.Get<IMap>();

            _defaultManager.Add(map, point);

            if (_optionManagers != null)
            {
                foreach (var optionManager in _optionManagers.Values)
                {
                    optionManager.Add(map, point);
                }
            }
        }

        public void CheckPoints(IStructureManager manager, IEnumerable<Vector2Int> points)
        {
            var map = Dependencies.Get<IMap>();

            _defaultManager.CheckPoints(manager, map, points);

            if (_optionManagers != null)
            {
                foreach (var optionManager in _optionManagers.Values)
                {
                    optionManager.CheckPoints(manager, map, points);
                }
            }
        }

        public void Calculate(int maxCalculations = PathQuery.DEFAULT_MAX_CALCULATIONS)
        {
            _defaultManager.GridPathfinding.Calculate(maxCalculations);

            if (_optionManagers != null)
            {
                foreach (var optionManager in _optionManagers.Values)
                {
                    optionManager.GridPathfinding.Calculate(maxCalculations);
                }
            }
        }

        public bool HasPoint(Vector2Int point, object tag = null) => getManager(tag).GridPathfinding.HasPoint(point, tag);
        public WalkingPath FindPath(Vector2Int[] starts, Vector2Int[] targets, object tag = null) => getManager(tag).GridPathfinding.FindPath(starts, targets, tag);
        public PathQuery FindPathQuery(Vector2Int[] starts, Vector2Int[] targets, object tag = null) => getManager(tag).GridPathfinding.FindPathQuery(starts, targets, tag);

        public void RegisterLink(IGridLink link, object tag)
        {
            if (_map == null)
            {
                _prematureLinks.Add(link, tag);
                return;
            }

            var network = getManager(tag);
            network.RegisterLink(link);
            if (network != _defaultManager)
                _defaultManager.RegisterLink(link);
        }
        public void DeregisterLink(IGridLink link, object tag)
        {
            if (_map == null)
            {
                _prematureLinks.Remove(link);
                return;
            }

            var network = getManager(tag);
            network.DeregisterLink(link);
            if (network != _defaultManager)
                _defaultManager.DeregisterLink(link);
        }
        public IEnumerable<IGridLink> GetLinks(Vector2Int start, object tag) => getManager(tag).GetLinks(start);
        public IGridLink GetLink(Vector2Int start, Vector2Int end, object tag) => getManager(tag).GetLink(start, end);

        private StructurePathManager getManager(object tag)
        {
            if (tag == null || _optionManagers == null || !_optionManagers.ContainsKey(tag))
                return _defaultManager;
            return _optionManagers[tag];
        }
    }
}
