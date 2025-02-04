using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper used by <see cref="DefaultStructureManager"/> to store the structure references for one structure level
    /// </summary>
    public class StructureLevelManager
    {
        private List<StructureReference> _structures = new List<StructureReference>();
        private readonly Dictionary<Vector2Int, StructureReference> _structurePoints = new Dictionary<Vector2Int, StructureReference>();

        public void AddStructure(IStructure structure)
        {
            _structures.Add(structure.StructureReference);

            structure.PointsChanged += structurePointsChanged;
            structure.StructureReference.Replacing += structureReferenceReplacing;

            foreach (var point in structure.GetPoints())
            {
                _structurePoints.Add(point, structure.StructureReference);
            }
        }

        public void RemoveStructure(IStructure structure)
        {
            _structures.Remove(structure.StructureReference);

            structure.PointsChanged -= structurePointsChanged;
            structure.StructureReference.Replacing -= structureReferenceReplacing;

            foreach (var point in structure.GetPoints())
            {
                if (_structurePoints.ContainsKey(point) && _structurePoints[point] == structure.StructureReference)
                    _structurePoints.Remove(point);
            }
        }

        public IEnumerable<StructureReference> GetStructureReferences() => _structures;
        public IEnumerable<IStructure> GetStructures() => _structures.Select(r => r.Instance);
        public IStructure GetStructure(Vector2Int point)
        {
            if (_structurePoints.ContainsKey(point))
                return _structurePoints[point].Instance;
            return null;
        }

        private void structurePointsChanged(PointsChanged<IStructure> change)
        {
            foreach (var point in change.RemovedPoints)
            {
                if (_structurePoints.ContainsKey(point) && _structurePoints[point] == change.Sender.StructureReference)
                    _structurePoints.Remove(point);
            }
            var p = change.AddedPoints.ToList();
            foreach (var point in change.AddedPoints)
            {
                _structurePoints.Add(point, change.Sender.StructureReference);
            }
        }

        private void structureReferenceReplacing(IStructure a, IStructure b)
        {
            a.PointsChanged -= structurePointsChanged;
            b.PointsChanged += structurePointsChanged;
        }
    }
}
