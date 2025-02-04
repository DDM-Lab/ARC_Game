using UnityEngine;
using UnityEngine.EventSystems;

namespace CityBuilderCore
{
    /// <summary>
    /// selects walkers and buildings under the mouse on click
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_selection_tool.html")]
    public class SelectionTool : BaseTool
    {
        public enum SelectionMethod { None, Raycast, Point }

        [Tooltip("whether and how walkers get selected, by default they are raycast because mutliple walkers can be in the same point, requires a collider on the walker")]
        public SelectionMethod WalkerSelection = SelectionMethod.Raycast;
        [Tooltip("whether and how buildings are selected, by default they are selected by point because only one building can be in the same point most of the time and this check can be done fast without needing a collider")]
        public SelectionMethod BuildingSelection = SelectionMethod.Point;
        [Header("Events")]
        [Tooltip("fired when a building is clicked, use to show building dialogs and such")]
        public BuildingEvent BuildingSelected;
        [Tooltip("fired when a walker is clicked, use to show walker dialogs and such")]
        public WalkerEvent WalkerSelected;
        [Tooltip("fired when a click occured but not building or walker was found")]
        public Vector2IntEvent PointSelected;
        [Header("Higlighting")]
        [Tooltip("color used to highlight what the pointer hovers over(set alpha to 0 to deactivate)")]
        public Color HighlightColor = Color.clear;
        [Tooltip("added to buildings when the mouse hovers over them")]
        public BuildingAddon BuildingAddon;
        [Tooltip("added to walkers when the mouse hovers over them")]
        public WalkerAddon WalkerAddon;
        [Header("Filtering")]
        [Tooltip("if set only buildings of this category can be selected, when empty all buildings are valid")]
        public BuildingCategory BuildingFilter;
        [Tooltip("layer mask used when selecting buildings by raycast")]
        public LayerMask BuildingRaycastMask = -1;
        [Tooltip("objects found when raycasting for buildings need to have this tag, empty to skip check")]
        public string BuildingRaycastTag = "Building";
        [Tooltip("if set only walkers of this category can be selected, when empty all walkers are valid")]
        public WalkerCategory WalkerFilter;
        [Tooltip("layer mask used when selecting walkers by raycast")]
        public LayerMask WalkerRaycastMask = -1;
        [Tooltip("objects found when raycasting for walkers need to have this tag, empty to skip check")]
        public string WalkerRaycastTag = "Walker";

        public override bool ShowGrid => false;
        public override bool IsTouchPanAllowed => true;
        public bool IsHighlighting => HighlightColor.a > 0f;
        public bool IsHovering => IsHighlighting || BuildingAddon || WalkerAddon;

        private float _mouseDown;
        private IMouseInput _mouseInput;
        private IHighlightManager _highlighting;
        private IBuilding _currentAddonBuilding;
        private Walker _currentAddonWalker;

        private void Start()
        {
            _mouseInput = Dependencies.Get<IMouseInput>();
            if (IsHighlighting)
                _highlighting = Dependencies.Get<IHighlightManager>();
        }

        protected override void updateTool()
        {
            base.updateTool();

            if (IsHighlighting)
                _highlighting.Clear();

            if (EventSystem.current.IsPointerOverGameObject())
            {
                setWalkerAddon(null);
                setBuildingAddon(null);
                return;
            }

            if (!_mouseInput.TryGetMouseGridPosition(out Vector2Int mousePoint))
                return;

            if (Input.GetMouseButtonDown(0))
                _mouseDown = Time.unscaledTime;

            var clicked = Input.GetMouseButtonUp(0) && (Time.unscaledTime - _mouseDown) < 0.2f;
            if (clicked)
                onApplied();

            if (!IsHovering && !clicked)
                return;

            var walker = getWalker(mousePoint);
            if (walker)
            {
                setWalkerAddon(walker);
                setBuildingAddon(null);

                if (IsHighlighting)
                    _highlighting.Highlight(walker.GridPoint, HighlightColor);
                if (clicked)
                    WalkerSelected?.Invoke(walker);
                return;
            }

            var building = getBuilding(mousePoint);
            if (building != null)
            {
                setWalkerAddon(null);
                setBuildingAddon(building);

                if (IsHighlighting)
                    _highlighting.Highlight(building.GetPoints(), HighlightColor);
                if (clicked)
                    BuildingSelected?.Invoke(building.BuildingReference);
                return;
            }

            setWalkerAddon(null);
            setBuildingAddon(null);

            if (IsHighlighting)
                _highlighting.Highlight(mousePoint, HighlightColor);

            if (clicked)
                PointSelected?.Invoke(mousePoint);
        }

        private void setWalkerAddon(Walker walker)
        {
            if (!WalkerAddon)
                return;

            if (_currentAddonWalker == walker)
                return;

            if (_currentAddonWalker != null)
                _currentAddonWalker.RemoveAddon(WalkerAddon.Key);
            _currentAddonWalker = walker;
            if (_currentAddonWalker != null)
                _currentAddonWalker.AddAddon(WalkerAddon);
        }
        private void setBuildingAddon(IBuilding building)
        {
            if (!BuildingAddon)
                return;

            if (_currentAddonBuilding == building)
                return;

            if (_currentAddonBuilding != null)
                _currentAddonBuilding.RemoveAddon(BuildingAddon.Key);
            _currentAddonBuilding = building;
            if (_currentAddonBuilding != null)
                _currentAddonBuilding.AddAddon(BuildingAddon);
        }

        private Walker getWalker(Vector2Int point)
        {
            switch (WalkerSelection)
            {
                case SelectionMethod.Raycast:
                    var ray = _mouseInput.GetRay();
                    if (ray.IsInvalid())
                        return null;

                    foreach (var hit in Physics.RaycastAll(ray, float.MaxValue, WalkerRaycastMask))
                    {
                        if (!string.IsNullOrWhiteSpace(WalkerRaycastTag) && !hit.transform.gameObject.CompareTag(WalkerRaycastTag))
                            continue;

                        var walker = hit.transform.gameObject.GetComponent<Walker>();
                        if (!walker)
                        {
                            walker = hit.transform.gameObject.GetComponentInParent<Walker>();
                            if (!walker)
                                continue;
                        }

                        if (WalkerFilter && !WalkerFilter.Contains(walker.Info))
                            continue;

                        return walker;
                    }
                    break;
                case SelectionMethod.Point:
                    foreach (var walker in Dependencies.Get<IWalkerManager>().GetWalkers())
                    {
                        if (walker.GridPoint != point)
                            continue;
                        if (WalkerFilter && !WalkerFilter.Contains(walker.Info))
                            continue;

                        return walker;
                    }
                    break;
            }

            return null;
        }

        private IBuilding getBuilding(Vector2Int point)
        {
            switch (BuildingSelection)
            {
                case SelectionMethod.Raycast:
                    var ray = _mouseInput.GetRay();
                    if (ray.IsInvalid())
                        return null;

                    foreach (var hit in Physics.RaycastAll(ray, float.MaxValue, BuildingRaycastMask))
                    {
                        if (!string.IsNullOrWhiteSpace(BuildingRaycastTag) && !hit.transform.gameObject.CompareTag(BuildingRaycastTag))
                            continue;

                        var building = hit.transform.gameObject.GetComponent<Building>();
                        if (!building)
                        {
                            building = hit.transform.gameObject.GetComponentInParent<Building>();
                            if (!building)
                                continue;
                        }

                        if (BuildingFilter && !BuildingFilter.Contains(building.Info))
                            continue;

                        return building;
                    }
                    break;
                case SelectionMethod.Point:
                    foreach (var building in Dependencies.Get<IBuildingManager>().GetBuilding(point))
                    {
                        if (BuildingFilter && !BuildingFilter.Contains(building.Info))
                            continue;

                        return building;
                    }
                    break;
            }

            return null;
        }
    }
}