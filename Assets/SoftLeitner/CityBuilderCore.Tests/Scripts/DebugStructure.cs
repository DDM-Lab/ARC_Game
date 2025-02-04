using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class DebugStructure : KeyedBehaviour, IStructure
    {
        public StructureReference StructureReference { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Transform Root => transform;

        public bool IsDestructible => false;
        public bool IsMovable => false;
        public bool IsDecorator => false;
        public bool IsWalkable => false;
        public int Level => 0;

        public event Action<PointsChanged<IStructure>> PointsChanged
        {
            add { }
            remove { }
        }

        public IEnumerable<Vector2Int> GetPoints() => new Vector2Int[] { Dependencies.Get<IGridPositions>().GetGridPoint(transform.position) };

        public bool HasPoint(Vector2Int point) => GetPoints().Contains(point);

        public void Add(IEnumerable<Vector2Int> points) { }
        public void Remove(IEnumerable<Vector2Int> points) { }

        public string GetName() => "DEBUG";

    }
}
