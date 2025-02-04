using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// tool that moves one or more structures to a different position<br/>
    /// which structures can be moved is determined by <see cref="IStructure.IsMovable"/><br/>
    /// this can be set in BuildingInfo for buildings, some structures have a checkbox
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_move_tool.html")]
    public class MoveTool : PointerToolBase
    {
        private class MoveBuilding
        {
            public BuildingReference BuildingReference;
            public GameObject Ghost;

            public MoveBuilding(IBuilding building, Transform parent)
            {
                BuildingReference = building.BuildingReference;
                if (building.Info.Ghost)
                {
                    Ghost = Instantiate(building.Info.GetGhost(building.Index), building.Root.position, building.Root.rotation, parent);
                    if (building is ExpandableBuilding expandableBuilding)
                        Ghost.GetComponent<ExpandableVisual>().UpdateVisual(expandableBuilding.Expansion);
                }
                else
                {
                    Ghost = new GameObject();
                    Ghost.transform.SetParent(parent);
                    Ghost.transform.SetPositionAndRotation(building.Root.position, building.Root.rotation);
                }
            }
        }
        private class MoveStructure
        {
            public StructureReference StructureReference;
            public List<Vector2Int> OriginalPoints;
            public List<Vector2Int> Points;
        }

        [Tooltip("is displayed as its tooltip")]
        public string Name;
        [Tooltip("whether buildings can be rotated using R, for example in Isometric games where this does not make sense")]
        public bool AllowRotate = true;

        public override string TooltipName => Name;

        private IHighlightManager _highlighting;
        private IStructureManager _structureManager;
        private IMap _map;
        private IGridPositions _gridPositions;

        private bool _isMoveOnce;
        private Vector2Int _moveOrigin;
        private Transform _moveParent;
        private List<MoveBuilding> _moveBuildings;
        private List<MoveStructure> _moveStructures;

        private void Awake()
        {
            Dependencies.Register(this);
        }

        protected override void Start()
        {
            base.Start();

            _highlighting = Dependencies.Get<IHighlightManager>();
            _structureManager = Dependencies.Get<IStructureManager>();
            _map = Dependencies.Get<IMap>();
            _gridPositions = Dependencies.Get<IGridPositions>();
        }

        public override void DeactivateTool()
        {
            base.DeactivateTool();

            cleanup();
        }

        public void MoveOnce(IBuilding building)
        {
            _isMoveOnce = true;
            _moveOrigin = building.Point;
            _moveParent = new GameObject("Mover").transform;
            _moveBuildings = new List<MoveBuilding>() { new MoveBuilding(building, _moveParent) };
            _moveStructures = new List<MoveStructure>();
        }

        protected override void updatePointer(Vector2Int mousePoint, Vector2Int dragStart, bool isDown, bool isApply)
        {
            _highlighting.Clear();

            if (_moveParent == null)
            {
                IEnumerable<Vector2Int> points;

                if (isDown)
                {
                    points = PositionHelper.GetBoxPositions(dragStart, mousePoint);
                }
                else
                {
                    if (IsTouchActivated)
                        points = new Vector2Int[] { };
                    else
                        points = new Vector2Int[] { mousePoint };
                }

                _highlighting.Highlight(points, HighlightType.Info);

                if (isApply)
                {
                    var buildings = new List<IBuilding>();
                    var structures = new List<MoveStructure>();

                    foreach (var point in points)
                    {
                        foreach (var structure in _structureManager.GetStructures(point))
                        {
                            if (!structure.IsMovable)
                                continue;

                            if (structure is IBuilding building)
                            {
                                if (!buildings.Contains(building))
                                    buildings.Add(building);
                            }
                            else
                            {
                                if (structure is RoadNetwork roadNetwork && !roadNetwork.CheckPoint(point))
                                    continue;

                                var moveStructure = structures.FirstOrDefault(s => s.StructureReference == structure.StructureReference);
                                if (moveStructure == null)
                                {
                                    moveStructure = new MoveStructure()
                                    {
                                        StructureReference = structure.StructureReference,
                                        OriginalPoints = new List<Vector2Int>(),
                                        Points = new List<Vector2Int>(),
                                    };
                                    structures.Add(moveStructure);
                                }
                                moveStructure.OriginalPoints.Add(point);
                                moveStructure.Points.Add(point);
                            }
                        }
                    }

                    if (buildings.Any() || structures.Any())
                    {
                        _isMoveOnce = false;
                        _moveOrigin = mousePoint;
                        _moveParent = new GameObject("Mover").transform;
                        _moveBuildings = buildings.Select(b => new MoveBuilding(b, _moveParent)).ToList();
                        _moveStructures = structures;
                    }
                }
            }
            else
            {
                var pointDelta = mousePoint - _moveOrigin;

                var origin = _gridPositions.GetWorldPosition(_moveOrigin);
                var current = _gridPositions.GetWorldPosition(mousePoint);

                if (!isDown)
                {
                    if (AllowRotate && Input.GetKeyDown(KeyCode.R))
                    {
                        var centerPosition = _gridPositions.GetWorldCenterPosition(mousePoint);
                        var axis = _map.IsXY ? Vector3.back : Vector3.up;
                        var rotation = Quaternion.AngleAxis(90, axis);

                        foreach (var moveBuilding in _moveBuildings)
                        {
                            moveBuilding.Ghost.transform.RotateAround(centerPosition, axis, 90);
                        }

                        var offset = _gridPositions.GetWorldCenterPosition(_moveOrigin);

                        foreach (var moveStructure in _moveStructures)
                        {
                            for (int i = 0; i < moveStructure.Points.Count; i++)
                            {
                                var p = _gridPositions.GetWorldCenterPosition(moveStructure.Points[i]) - offset;

                                p = rotation * p;

                                moveStructure.Points[i] = _gridPositions.GetGridPoint(p + offset);
                            }
                        }
                    }
                }

                var isMoveValid = true;

                _moveParent.position = current - origin;

                var validPoints = new List<Vector2Int>();
                var invalidPoints = new List<Vector2Int>();

                foreach (var moveGhost in _moveBuildings)
                {
                    var building = moveGhost.BuildingReference.Instance;
                    var info = building.Info;
                    
                    var movedRotation = BuildingRotation.Create(moveGhost.Ghost.transform.localRotation);
                    var movedPoint = movedRotation.UnrotateOrigin(_gridPositions.GetGridPoint(moveGhost.Ghost.transform.position), moveGhost.BuildingReference.Instance.RawSize);
                    var movedSize = movedRotation.RotateSize(moveGhost.BuildingReference.Instance.RawSize);

                    var structurePoints = PositionHelper.GetStructurePositions(movedPoint, movedSize);
                    var isInside = structurePoints.All(p => _map.IsInside(p));
                    var isFulfillingRequirements = isInside && checkRequirements(moveGhost.BuildingReference.Instance, info, movedPoint, movedRotation, movedPoint - pointDelta);
                    var isCompletelyValid = true;

                    foreach (var point in structurePoints)
                    {
                        if (isFulfillingRequirements && checkAvailability(point, info.Level.Value))
                        {
                            validPoints.Add(point);
                        }
                        else
                        {
                            invalidPoints.Add(point);
                            isCompletelyValid = false;
                        }
                    }

                    isMoveValid &= isCompletelyValid;
                }

                foreach (var moveStructure in _moveStructures)
                {
                    object tag = null;
                    if (moveStructure.StructureReference.Instance is RoadNetwork roadNetwork)
                        tag = roadNetwork;

                    foreach (var point in moveStructure.Points)
                    {
                        var movedPoint = point + pointDelta;
                        var isAvailable = checkAvailability(movedPoint, moveStructure.StructureReference.Instance.Level, tag);
                        if (isAvailable)
                            validPoints.Add(movedPoint);
                        else
                            invalidPoints.Add(movedPoint);
                        isMoveValid &= isAvailable;
                    }
                }

                foreach (var moveGhost in _moveBuildings)
                {
                    moveGhost.Ghost.SetActive(isMoveValid);
                }

                _highlighting.Highlight(validPoints, true);
                _highlighting.Highlight(invalidPoints, false);

                if (isApply && isMoveValid)
                {
                    foreach (var moveStructure in _moveStructures)
                    {
                        moveStructure.StructureReference.Instance.Remove(moveStructure.OriginalPoints);
                    }

                    foreach (var moveGhost in _moveBuildings)
                    {
                        _structureManager.DeregisterStructure(moveGhost.BuildingReference.Instance);
                    }
                    foreach (var moveGhost in _moveBuildings)
                    {
                        var building = moveGhost.BuildingReference.Instance;
                        var info = building.Info;

                        var movedRotation = BuildingRotation.Create(moveGhost.Ghost.transform.localRotation);
                        var movedPoint = movedRotation.UnrotateOrigin(_gridPositions.GetGridPoint(moveGhost.Ghost.transform.position), moveGhost.BuildingReference.Instance.RawSize);

                        if (info is ExpandableBuildingInfo expandableInfo && building is ExpandableBuilding expandableBuilding)
                            expandableInfo.PrepareExpanded(movedPoint, expandableBuilding.Expansion, movedRotation);
                        else
                            info.Prepare(movedPoint, movedRotation);

                        moveGhost.BuildingReference.Instance.Move(moveGhost.Ghost.transform.position, moveGhost.Ghost.transform.rotation);
                    }
                    foreach (var moveGhost in _moveBuildings)
                    {
                        _structureManager.Remove(moveGhost.BuildingReference.Instance.GetPoints(), moveGhost.BuildingReference.Instance.Info.Level.Value, true);
                        _structureManager.RegisterStructure(moveGhost.BuildingReference.Instance);
                    }

                    foreach (var moveStructure in _moveStructures)
                    {
                        moveStructure.StructureReference.Instance.Add(moveStructure.Points.Select(p => p + pointDelta));
                    }

                    cleanup();
                    onApplied();

                    if (_isMoveOnce)
                    {
                        _isMoveOnce = false;
                        Dependencies.Get<IToolsManager>().DeactivateTool(this);
                    }
                }
            }
        }

        private bool checkRequirements(IBuilding building, BuildingInfo info, Vector2Int point, BuildingRotation rotation, Vector2Int originalPoint)
        {
            if (info is ExpandableBuildingInfo expandableInfo && building is ExpandableBuilding expandableBuilding)
            {
                if (!expandableInfo.CheckExpandedBuildingRequirements(point, expandableBuilding.Expansion, rotation))
                    return false;
                if (expandableInfo.RoadRequirements != null && expandableInfo.RoadRequirements.Length > 0 && !_moveStructures.Any(s => s.Points.Contains(originalPoint)))
                {
                    //if a structure that is moved along contains the point we assume the road is also being moved and road requirements dont need to be checked
                    if (!expandableInfo.CheckExpandedRoadRequirements(point, expandableBuilding.Expansion, rotation))
                        return false;
                }
            }
            else
            {
                if (!info.CheckBuildingRequirements(point, rotation))
                    return false;
                if (info.RoadRequirements != null && info.RoadRequirements.Length > 0 && !_moveStructures.Any(s => s.Points.Contains(originalPoint)))
                {
                    //if a structure that is moved along contains the point we assume the road is also being moved and road requirements dont need to be checked
                    if (!info.CheckRoadRequirements(point, rotation))
                        return false;
                }
            }
            return true;
        }

        private bool checkAvailability(Vector2Int point, int mask, object tag = null)
        {
            if (!_map.IsBuildable(point, mask, tag))
                return false;
            if (!_map.IsInside(point))
                return false;
            if (_structureManager.HasStructure(point, mask, isDecorator: false))
            {
                foreach (var structure in _structureManager.GetStructures(point, mask, isDecorator: false))
                {
                    if (_moveBuildings.Any(g => g.BuildingReference.Instance == structure))
                        continue;
                    var moveStructure = _moveStructures.FirstOrDefault(s => s.StructureReference == structure.StructureReference);
                    if (moveStructure != null && moveStructure.OriginalPoints.Contains(point))
                        continue;
                    return false;
                }
            }
            return true;
        }

        private void cleanup()
        {
            if (_moveParent)
                Destroy(_moveParent.gameObject);

            _moveParent = null;
            _moveBuildings = null;
        }
    }
}
