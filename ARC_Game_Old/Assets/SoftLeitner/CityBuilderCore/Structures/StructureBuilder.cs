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
    public class StructureBuilder : PointerToolBase
    {
        [Tooltip("the collection that will have points added by the builder(set either Collection or Tiles)")]
        public StructureCollection Collection;
        [Tooltip("the tiles that will have points added by the builder(set either Collection or Tiles)")]
        public StructureTiles Tiles;
        [Tooltip("structure that will have points added by the builder, use when your structure is neither a StructureCollection nor StructureTiles")]
        public Component Structure;
        [Tooltip("item cost per point that will be added")]
        public ItemQuantity[] Cost;
        [Tooltip("create points in a box shape instead of a line")]
        public bool Box;
        [Tooltip("removes blocking structures before building")]
        public bool Override;

        public override string TooltipName
        {
            get
            {
                name = _structure.GetName();

                if (Cost != null && Cost.Length > 0)
                    name += $"({Cost.ToDisplayString()})";

                return name;
            }
        }

        private LazyDependency<IStructureManager> _structureManager = new LazyDependency<IStructureManager>();
        private List<ItemQuantity> _costs = new List<ItemQuantity>();
        private IGlobalStorage _globalStorage;
        private IHighlightManager _highlighting;
        private IStructure _structure;

        private void Awake()
        {
            if (Collection)
            {
                _structure = Collection;
            }
            else if (Tiles)
            {
                _structure = Tiles;
            }
            else if (Structure)
            {
                if (Structure is IStructure structure)
                    _structure = structure;
                else
                    _structure = Structure.GetComponent<IStructure>();
            }
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
                if (_structureManager.Value.CheckAvailability(point, _structure.Level, _structure))
                {
                    validPositions.Add(point);
                }
                else
                {
                    if (Override && _structureManager.Value.HasStructure(point, _structure.Level, null, isDecorator: false))
                        validPositions.Add(point);
                    else
                        invalidPositions.Add(point);
                }
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

                if (Override)
                    _structureManager.Value.Remove(validPositions, _structure.Level, false);

                foreach (var items in Cost)
                {
                    _globalStorage.Items.RemoveItems(items.Item, items.Quantity * validPositions.Count);
                }

                _structure.Add(validPositions);
            }
        }
    }
}