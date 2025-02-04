using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper used to keep track of <see cref="IGridLink"/> so they can be quickly retrieved by their position
    /// </summary>
    public class GridLinks
    {
        private Dictionary<Vector2Int, List<IGridLink>> _byStart = new Dictionary<Vector2Int, List<IGridLink>>();
        private Dictionary<(Vector2Int start, Vector2Int end), IGridLink> _byLink = new Dictionary<(Vector2Int start, Vector2Int end), IGridLink>();

        public void Add(IGridLink link)
        {
            _byLink.Add((link.StartPoint, link.EndPoint), link);
            addByStart(link, link.StartPoint);
            if (link.Bidirectional)
            {
                _byLink.Add((link.EndPoint, link.StartPoint), link);
                addByStart(link, link.EndPoint);
            }
        }
        public void Remove(IGridLink link)
        {
            _byLink.Remove((link.StartPoint, link.EndPoint));
            removeByStart(link, link.StartPoint);
            if (link.Bidirectional)
            {
                _byLink.Remove((link.EndPoint, link.StartPoint));
                removeByStart(link, link.EndPoint);
            }
        }

        public bool Has(IGridLink link) => _byLink.ContainsValue(link);

        public IEnumerable<IGridLink> Get(Vector2Int start)
        {
            if (_byStart.ContainsKey(start))
                return _byStart[start];
            else
                return Enumerable.Empty<IGridLink>();
        }
        public IGridLink Get(Vector2Int start, Vector2Int end)
        {
            return _byLink.TryGetValue((start, end), out IGridLink link) ? link : null;
        }

        private void addByStart(IGridLink link, Vector2Int start)
        {
            if (!_byStart.ContainsKey(start))
                _byStart.Add(start, new List<IGridLink>());
            _byStart[start].Add(link);
        }
        private void removeByStart(IGridLink link, Vector2Int start)
        {
            if (!_byStart.ContainsKey(start))
                return;
            _byStart[start].Remove(link);
            if (_byStart[start].Count == 0)
                _byStart.Remove(start);
        }
    }
}
