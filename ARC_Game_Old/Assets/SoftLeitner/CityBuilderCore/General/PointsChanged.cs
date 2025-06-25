using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    public class PointsChanged<T>
    {
        public T Sender { get; }
        public IEnumerable<Vector2Int> RemovedPoints { get; }
        public IEnumerable<Vector2Int> AddedPoints { get; }

        public PointsChanged(T sender, IEnumerable<Vector2Int> removedPoints, IEnumerable<Vector2Int> addedPoints)
        {
            Sender = sender;
            RemovedPoints = removedPoints;
            AddedPoints = addedPoints;
        }
    }
}
