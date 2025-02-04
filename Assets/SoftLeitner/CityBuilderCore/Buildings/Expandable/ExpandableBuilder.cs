using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// special builder that can place <see cref="ExpandableBuilding"/> of dynamic size by dragging out the size
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_expandable_builder.html")]
    public class ExpandableBuilder : PointerToolBase
    {
        [Tooltip("the building that will be created by this builder")]
        public ExpandableBuildingInfo BuildingInfo;

        public override string TooltipName => BuildingInfo.Cost != null && BuildingInfo.Cost.Length > 0 ? $"{BuildingInfo.Name}({BuildingInfo.Cost.ToDisplayString()})" : BuildingInfo.Name;
        public override string TooltipDescription => BuildingInfo.Description;

        private int _index;
        private ExpandableVisual _ghost;

        private List<ItemQuantity> _costs = new List<ItemQuantity>();
        private IGlobalStorage _globalStorage;
        private IHighlightManager _highlighting;
        private IMap _map;

        protected override void Start()
        {
            base.Start();

            _globalStorage = Dependencies.GetOptional<IGlobalStorage>();
            _highlighting = Dependencies.Get<IHighlightManager>();
            _map = Dependencies.Get<IMap>();
        }

        public override void ActivateTool()
        {
            base.ActivateTool();

            _index = 0;
            recreateGhost();
        }

        public override void DeactivateTool()
        {
            if (_ghost)
            {
                Destroy(_ghost.gameObject);
            }

            _costs.Clear();

            base.DeactivateTool();
        }

        public override int GetCost(Item item)
        {
            return _costs.FirstOrDefault(c => c.Item == item)?.Quantity ?? 0;
        }

        protected override void updateTool()
        {
            base.updateTool();

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _index++;
                recreateGhost();
            }
        }

        protected override void updatePointer(Vector2Int mousePoint, Vector2Int dragStart, bool isDown, bool isApply)
        {
            List<Vector2Int> validPoints = new List<Vector2Int>();
            List<Vector2Int> invalidPoints = new List<Vector2Int>();

            Vector2Int drag = mousePoint - dragStart;

            Vector2Int expansion;
            Vector2Int point;
            BuildingRotation rotation;

            if (isDown)
            {
                if (BuildingInfo.IsArea)
                {
                    expansion = new Vector2Int(Mathf.Abs(drag.x), Mathf.Abs(drag.y)) - BuildingInfo.Size - BuildingInfo.SizePost + Vector2Int.one;

                    if (drag.x >= 0)
                    {
                        if (drag.y >= 0)
                        {
                            point = dragStart;
                            rotation = new BuildingRotationRectangle(0);
                        }
                        else
                        {
                            expansion = new Vector2Int(expansion.y, expansion.x);
                            point = new Vector2Int(dragStart.x, mousePoint.y);
                            rotation = new BuildingRotationRectangle(1);
                        }
                    }
                    else
                    {
                        if (drag.y >= 0)
                        {
                            expansion = new Vector2Int(expansion.y, expansion.x);
                            point = new Vector2Int(mousePoint.x, dragStart.y);
                            rotation = new BuildingRotationRectangle(3);
                        }
                        else
                        {
                            point = mousePoint;
                            rotation = new BuildingRotationRectangle(2);
                        }
                    }
                }
                else
                {
                    if (Mathf.Abs(drag.x) >= Mathf.Abs(drag.y))
                    {
                        expansion = new Vector2Int(Mathf.Abs(drag.x) - BuildingInfo.Size.x - BuildingInfo.SizePost.x + 1, 0);
                        point = drag.x > 0 ? dragStart : new Vector2Int(mousePoint.x, dragStart.y);
                        rotation = drag.x >= 0 ? new BuildingRotationRectangle(0) : new BuildingRotationRectangle(2);
                    }
                    else
                    {
                        expansion = new Vector2Int(Mathf.Abs(drag.y) - BuildingInfo.Size.x - BuildingInfo.SizePost.x + 1, 0);
                        point = drag.y > 0 ? dragStart : new Vector2Int(dragStart.x, mousePoint.y);
                        rotation = drag.y >= 0 ? new BuildingRotationRectangle(1) : new BuildingRotationRectangle(3);
                    }
                }
            }
            else
            {
                point = mousePoint;
                rotation = new BuildingRotationRectangle(0);
                expansion = Vector2Int.one - BuildingInfo.Size - BuildingInfo.SizePost;
            }

            var size = BuildingInfo.Size + expansion + BuildingInfo.SizePost;
            var structurePoints = PositionHelper.GetStructurePositions(point, rotation.RotateSize(size));

            if (structurePoints.All(p => _map.IsInside(p)) && BuildingInfo.CheckExpansionLimits(expansion) && BuildingInfo.CheckExpandedRequirements(point, expansion, rotation))
            {
                foreach (var structurePoint in structurePoints)
                {
                    if (BuildingInfo.CheckAvailability(structurePoint))
                        validPoints.Add(structurePoint);
                    else
                        invalidPoints.Add(structurePoint);
                }
            }
            else
            {
                invalidPoints.AddRange(structurePoints);
            }

            if (!checkCost(expansion))
            {
                invalidPoints.AddRange(validPoints);
                validPoints.Clear();
            }

            _highlighting.Clear();
            _highlighting.Highlight(validPoints, true);
            _highlighting.Highlight(invalidPoints, false);

            if (_ghost)
            {
                _ghost.gameObject.SetActive(BuildingInfo.CheckExpansionLimits(expansion));
                _ghost.transform.position = Dependencies.Get<IGridPositions>().GetWorldPosition(rotation.RotateOrigin(point, size));
                _ghost.transform.rotation = rotation.GetRotation();
                _ghost.UpdateVisual(expansion);
            }

            if (isApply)
            {
                if (validPoints.Count > 0 && invalidPoints.Count == 0)
                    build(point, rotation, expansion);
            }
        }

        private void recreateGhost()
        {
            if (_ghost)
                Destroy(_ghost);

            var prefab = BuildingInfo.GetGhost(_index);

            if (prefab)
            {
                _ghost = Instantiate(prefab).GetComponent<ExpandableVisual>();
                _ghost.gameObject.SetActive(false);
            }
        }

        private bool checkCost(Vector2Int expansion)
        {
            bool hasCost = true;
            _costs.Clear();

            foreach (var items in BuildingInfo.Cost)
            {
                _costs.AddQuantity(items.Item, items.Quantity);
                if (_globalStorage != null && !_globalStorage.Items.HasItemsRemaining(items.Item, items.Quantity))
                {
                    hasCost = false;
                }
            }

            var expansionCount = getExpansionCount(expansion);
            foreach (var items in BuildingInfo.ExpansionCost)
            {
                _costs.AddQuantity(items.Item, items.Quantity * expansionCount);
                if (_globalStorage != null && !_globalStorage.Items.HasItemsRemaining(items.Item, items.Quantity * expansionCount))
                {
                    hasCost = false;
                }
            }

            return hasCost;
        }

        private int getExpansionCount(Vector2Int expansion) => expansion.y == 0 ? expansion.x : expansion.x * expansion.y;

        private void build(Vector2Int point, BuildingRotation rotation, Vector2Int expansion)
        {
            var buildingManager = Dependencies.Get<IBuildingManager>();
            var gridPositions = Dependencies.Get<IGridPositions>();
            var size = BuildingInfo.Size + expansion + BuildingInfo.SizePost;

            onApplied();

            if (_globalStorage != null)
            {
                foreach (var items in BuildingInfo.Cost)
                {
                    _globalStorage.Items.RemoveItems(items.Item, items.Quantity);
                }

                var expansionCount = getExpansionCount(expansion);
                foreach (var items in BuildingInfo.ExpansionCost)
                {
                    _globalStorage.Items.RemoveItems(items.Item, items.Quantity * expansionCount);
                }
            }


            BuildingInfo.PrepareExpanded(point, expansion, rotation);
            buildingManager.Add(gridPositions.GetWorldPosition(rotation.RotateOrigin(point, size)), rotation.GetRotation(), BuildingInfo.Prefab, b => ((ExpandableBuilding)b).Expansion = expansion);
        }
    }
}
