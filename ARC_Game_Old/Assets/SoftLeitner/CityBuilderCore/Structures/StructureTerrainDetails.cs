using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// structure that removes terrain detail using a <see cref="TerrainModifier"/><br/>
    /// this structure is very special in that it does not have any concrete points<br/>
    /// <see cref="GetPoints"/> will return nothing but <see cref="HasPoint(Vector2Int)"/> will always return true<br/>
    /// since it is a decorator it will not block building but because of its special setup it will always be told to remove points when something is built<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/structures">https://citybuilder.softleitner.com/manual/structures</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_structure_terrain_details.html")]
    public class StructureTerrainDetails : KeyedBehaviour, IStructure
    {
        [Tooltip("name of the structure for UI purposes")]
        public string Name;
        [Tooltip("the terrain modifier used to retrieve and change the detail layer")]
        public TerrainModifier TerrainModifier;
        [Tooltip("the index of the detail prototype, -1 for all")]
        public int Index = -1;

        [Tooltip("determines which other structures can reside in the same points")]
        public StructureLevelMask Level;

        bool IStructure.IsDestructible => true;
        bool IStructure.IsMovable => false;
        bool IStructure.IsDecorator => true;
        bool IStructure.IsWalkable => true;
        int IStructure.Level => Level.Value;

        public StructureReference StructureReference { get; set; }

        public event Action<PointsChanged<IStructure>> PointsChanged;

        private void Start()
        {
            StructureReference = new StructureReference(this);
            Dependencies.Get<IStructureManager>().RegisterStructure(this, true);
        }

        private void OnDestroy()
        {
            if (gameObject.scene.isLoaded)
                Dependencies.Get<IStructureManager>().DeregisterStructure(this);
        }

        public string GetName() => Name;

        public IEnumerable<Vector2Int> GetPoints() => Enumerable.Empty<Vector2Int>();
        public bool HasPoint(Vector2Int point) => true;

        public void Add(IEnumerable<Vector2Int> points)
        {
            throw new NotImplementedException();
        }
        public void Remove(IEnumerable<Vector2Int> points)
        {
            foreach (var point in points)
            {
                TerrainModifier.RemoveDetails(point, Index);
            }

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, points, Enumerable.Empty<Vector2Int>()));
        }
    }
}
