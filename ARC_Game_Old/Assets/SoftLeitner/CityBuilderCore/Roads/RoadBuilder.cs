using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// tool for placing roads
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_road_builder.html")]
    public class RoadBuilder : PointerToolBase
    {
        [Tooltip("the road that will be placed by this builder")]
        public Road Road;
        [Tooltip("whether the points between start and end should be queried from pathfinding, otherwise straight lines are used")]
        public bool Pathfinding;
        [Tooltip("optional extra info passed to pathfinding, just like PathTag on walkers")]
        public Object PathfindingTag;
        [Tooltip("fired whenever a roads are built")]
        public UnityEvent<Vector2Int[]> Built;

        public override string TooltipName => Road.Cost != null && Road.Cost.Length > 0 ? $"{Road.Name}({Road.Cost.ToDisplayString()})" : Road.Name;

        private List<ItemQuantity> _costs = new List<ItemQuantity>();
        private IGlobalStorage _globalStorage;
        private IHighlightManager _highlighting;

        protected override void Start()
        {
            base.Start();

            _globalStorage = Dependencies.GetOptional<IGlobalStorage>();
            _highlighting = Dependencies.Get<IHighlightManager>();
        }

        public override void ActivateTool()
        {
            base.ActivateTool();

            checkCost(1);
        }

        public override int GetCost(Item item)
        {
            return _costs.FirstOrDefault(c => c.Item == item)?.Quantity ?? 0;
        }

        protected override void updatePointer(Vector2Int mousePoint, Vector2Int dragStart, bool isDown, bool isApply)
        {
            _highlighting.Clear();

            List<Vector2Int> validPoints = new List<Vector2Int>();
            List<Vector2Int> invalidPoints = new List<Vector2Int>();

            IEnumerable<Vector2Int> points;

            if (isDown)
            {
                if (Pathfinding)
                {
                    var path = PathHelper.FindPath(dragStart, mousePoint, PathType.MapGrid, PathfindingTag);
                    if (path == null)
                        points = PositionHelper.GetRoadPositions(dragStart, mousePoint);
                    else
                        points = path.GetPoints().ToList();
                }
                else
                {
                    points = PositionHelper.GetRoadPositions(dragStart, mousePoint);
                }
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
                if (Dependencies.Get<IStructureManager>().CheckAvailability(point, Road.Level.Value, Road))
                {
                    validPoints.Add(point);
                }
                else
                {
                    invalidPoints.Add(point);
                }
            }

            if (!checkCost(validPoints.Count))
            {
                invalidPoints.AddRange(validPoints);
                validPoints.Clear();
            }

            _highlighting.Clear();
            _highlighting.Highlight(validPoints, true);
            _highlighting.Highlight(invalidPoints, false);

            if (isApply)
            {
                if (validPoints.Any())
                    onApplied();

                if (_globalStorage != null)
                {
                    foreach (var items in Road.Cost)
                    {
                        _globalStorage.Items.RemoveItems(items.Item, items.Quantity * validPoints.Count);
                    }
                }

                Dependencies.Get<IRoadManager>().Add(validPoints, Road);

                Built?.Invoke(validPoints.ToArray());
            }
        }

        private bool checkCost(int count)
        {
            bool hasCost = true;
            _costs.Clear();
            foreach (var items in Road.Cost)
            {
                _costs.Add(new ItemQuantity(items.Item, items.Quantity * count));

                if (_globalStorage != null && !_globalStorage.Items.HasItemsRemaining(items.Item, items.Quantity * count))
                {
                    hasCost = false;
                }
            }
            return hasCost;
        }
    }
}