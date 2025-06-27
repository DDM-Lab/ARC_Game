using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// structure that adds and removes terrain trees using a <see cref="TerrainModifier"/><br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/structures">https://citybuilder.softleitner.com/manual/structures</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_structure_terrain_trees.html")]
    public class StructureTerrainTrees : KeyedBehaviour, IStructure
    {
        [Tooltip("name of the structure for UI purposes")]
        public string Name;
        [Tooltip("the terrain modifier used to retrieve and change the trees")]
        public TerrainModifier TerrainModifier;
        [Tooltip("the index of the tree prototype, -1 for all")]
        public int Index = -1;
        [Header("Structure Setting")]
        [Tooltip("whether the structure can be removed by the player")]
        public bool IsDestructible = true;
        [Tooltip("whether the structure can be moved by the MoveTool")]
        public bool IsMovable = true;
        [Tooltip("whether the structure is automatically removed when something is built on top of it")]
        public bool IsDecorator = false;
        [Tooltip("whether walkers can pass the points of this structure")]
        public bool IsWalkable = false;
        [Tooltip("determines which other structures can reside in the same points")]
        public StructureLevelMask Level;
        [Header("Tree Randomization")]
        [Tooltip("minimum value for tree size when a tree is added")]
        public float MinHeight = 1;
        [Tooltip("maximum value for tree size when a tree is added")]
        public float MaxHeight = 1;
        [Tooltip("how much the color may differ at most from the original color")]
        [Range(0, 1)]
        public float ColorVariation;

        bool IStructure.IsDestructible => IsDestructible;
        bool IStructure.IsMovable => IsMovable;
        bool IStructure.IsDecorator => IsDecorator;
        bool IStructure.IsWalkable => IsWalkable;
        int IStructure.Level => Level.Value;

        public StructureReference StructureReference { get; set; }

        public event Action<PointsChanged<IStructure>> PointsChanged;

        private HashSet<Vector2Int> _points;
        private UnityAction _terrainLoadedAction;

        private void Start()
        {
            _points = new HashSet<Vector2Int>(TerrainModifier.GetTreePoints(Index).Distinct());

            StructureReference = new StructureReference(this);
            Dependencies.Get<IStructureManager>().RegisterStructure(this, true);

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, Enumerable.Empty<Vector2Int>(), _points));
        }

        private void OnEnable()
        {
            if (_terrainLoadedAction == null)
                _terrainLoadedAction = new UnityAction(terrainLoaded);

            TerrainModifier.Loaded.AddListener(_terrainLoadedAction);
        }
        private void OnDisable()
        {
            TerrainModifier.Loaded.RemoveListener(_terrainLoadedAction);
        }

        private void OnDestroy()
        {
            if (gameObject.scene.isLoaded)
                Dependencies.Get<IStructureManager>().DeregisterStructure(this);
        }

        public string GetName() => Name;

        public IEnumerable<Vector2Int> GetPoints() => _points;
        public bool HasPoint(Vector2Int point) => _points.Contains(point);

        public void Add(IEnumerable<Vector2Int> points)
        {
            foreach (var point in points)
            {
                var size = UnityEngine.Random.Range(MinHeight, MaxHeight);
                var color = 1f - UnityEngine.Random.Range(0, ColorVariation);

                TerrainModifier.AddTree(point, Index, size, size, color);
                _points.Add(point);
            }

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, Enumerable.Empty<Vector2Int>(), points));
        }
        public void Add(Vector2Int point, TreeInstance template, int? prototypeIndex = null)
        {
            TerrainModifier.AddTree(point, template, prototypeIndex ?? Index);
            _points.Add(point);

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, Enumerable.Empty<Vector2Int>(), new Vector2Int[] { point }));
        }
        public void Remove(IEnumerable<Vector2Int> points)
        {
            foreach (var point in points)
            {
                TerrainModifier.RemoveTrees(point, Index);
                _points.Remove(point);
            }

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, points, Enumerable.Empty<Vector2Int>()));
        }

        public TreeInstance Get(Vector2Int point)
        {
            return TerrainModifier.GetTree(point, Index);
        }

        private void terrainLoaded()
        {
            if (_points == null)
                return;

            var previousPoint = _points.ToList();
            _points = new HashSet<Vector2Int>(TerrainModifier.GetTreePoints(Index).Distinct());

            PointsChanged?.Invoke(new PointsChanged<IStructure>(this, previousPoint, _points));
        }
    }
}
