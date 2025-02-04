using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// tool for adding points to a <see cref="StructureCollection"/> or <see cref="StructureTiles"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/structures">https://citybuilder.softleitner.com/manual/structures</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_structure_builder.html")]
    public class StructureReplacer : PointerToolBase
    {
        [Tooltip("structure that will have points removed")]
        public Component StructureA;
        [Tooltip("structure that will have points added")]
        public Component StructureB;
        [Tooltip("item cost per point that will be replaced")]
        public ItemQuantity[] Cost;
        [Tooltip("create points in a box shape instead of a line")]
        public bool Box;
        [Tooltip("whether randomizations like size or rotation are carried over")]
        public bool KeepRandomization = true;
        [Tooltip("whether the variant index is carried over")]
        public bool KeepVariant = true;

        public override string TooltipName
        {
            get
            {
                name = _structureA.GetName();

                if (Cost != null && Cost.Length > 0)
                    name += $"({Cost.ToDisplayString()})";

                return name;
            }
        }

        private LazyDependency<IStructureManager> _structureManager = new LazyDependency<IStructureManager>();
        private List<ItemQuantity> _costs = new List<ItemQuantity>();
        private IGlobalStorage _globalStorage;
        private IHighlightManager _highlighting;
        private IStructure _structureA, _structureB;

        private void Awake()
        {
            if (StructureA is IStructure structureA)
                _structureA = structureA;
            else
                _structureA = StructureA.GetComponent<IStructure>();

            if (StructureB is IStructure structureB)
                _structureB = structureB;
            else
                _structureB = StructureB.GetComponent<IStructure>();
        }

        protected override void Start()
        {
            base.Start();

            _globalStorage = Dependencies.Get<IGlobalStorage>();
            _highlighting = Dependencies.Get<IHighlightManager>();
        }

        public override int GetCost(Item item)
        {
            return _costs.FirstOrDefault(c => c.Item == item)?.Quantity ?? 0;
        }

        protected override void updatePointer(Vector2Int mousePoint, Vector2Int dragStart, bool isDown, bool isApply)
        {
            _highlighting.Clear();

            var validPositions = new List<Vector2Int>();
            var invalidPositions = new List<Vector2Int>();

            IEnumerable<Vector2Int> points;

            if (isDown)
            {
                if (Box)
                    points = PositionHelper.GetBoxPositions(dragStart, mousePoint);
                else
                    points = PositionHelper.GetRoadPositions(dragStart, mousePoint);
            }
            else if (IsTouchActivated)
            {
                points = new Vector2Int[] { };
            }
            else
            {
                points = new Vector2Int[] { mousePoint };
            }

            foreach (var point in points)
            {
                if (_structureA.HasPoint(point))
                    validPositions.Add(point);
                else
                    invalidPositions.Add(point);
            }

            bool hasCost = true;
            _costs.Clear();
            foreach (var items in Cost)
            {
                _costs.Add(new ItemQuantity(items.Item, items.Quantity * validPositions.Count));

                if (!_globalStorage.Items.HasItemsRemaining(items.Item, items.Quantity * validPositions.Count))
                {
                    hasCost = false;
                }
            }

            if (!hasCost)
            {
                invalidPositions.AddRange(validPositions);
                validPositions.Clear();
            }

            _highlighting.Clear();
            _highlighting.Highlight(validPositions, true);
            _highlighting.Highlight(invalidPositions, false);

            if (isApply)
            {
                if (validPositions.Any())
                    onApplied();

                foreach (var items in Cost)
                {
                    _globalStorage.Items.RemoveItems(items.Item, items.Quantity * validPositions.Count);
                }

                IStructure.ReplacePoints(_structureA, _structureB, validPositions, KeepRandomization, KeepVariant);
            }
        }
    }
}