using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CityBuilderCore.Tests
{
    /// <summary>
    /// can be used to move walkers for debugging
    /// </summary>
    public class DebugMoveTool : BaseTool
    {
        public override bool ShowGrid => false;
        public override bool IsTouchPanAllowed => true;

        [Tooltip("marks the selected walker")]
        public Transform Marker;
        [Tooltip("start walking as part of a process")]
        public bool Process;

        private IMouseInput _mouseInput;
        private IHighlightManager _highlights;
        private Walker _selectedWalker;

        private Action _nextMove;

        private void Start()
        {
            _mouseInput = Dependencies.Get<IMouseInput>();
            _highlights = Dependencies.GetOptional<IHighlightManager>();
        }

        protected override void updateTool()
        {
            base.updateTool();

            var mousePosition = _mouseInput.GetMouseGridPosition();

            if (_highlights != null)
            {
                _highlights.Clear();
                _highlights.Highlight(mousePosition, HighlightType.Info);
            }

            if (Input.GetMouseButtonUp(0) && !EventSystem.current.IsPointerOverGameObject())
            {
                var walkerObject = Physics.RaycastAll(_mouseInput.GetRay()).Select(h => h.transform.gameObject).FirstOrDefault(g => g.CompareTag("Walker"));
                if (walkerObject)
                {
                    var walker = walkerObject.GetComponent<Walker>();
                    if (walker)
                    {
                        _selectedWalker = walker;

                        Marker.SetParent(_selectedWalker.Pivot, false);
                    }
                }

                onApplied();
            }

            if (Input.GetMouseButtonUp(1) && !EventSystem.current.IsPointerOverGameObject())
            {
                if (_selectedWalker != null)
                {
                    if (Process)
                    {
                        if (Input.GetKey(KeyCode.LeftShift))
                        {
                            _selectedWalker.StartProcess(new WalkerAction[] { new RoamAction(64, 16) });
                        }
                        else
                        {
                            var building = Dependencies.Get<IBuildingManager>().GetBuilding(mousePosition).FirstOrDefault();
                            if (building == null)
                                _selectedWalker.StartProcess(new WalkerAction[] { new WalkPointAction(mousePosition) });
                            else
                                _selectedWalker.StartProcess(new WalkerAction[] { new WalkBuildingAction(building) });
                        }
                    }
                    else
                    {
                        if (Input.GetKey(KeyCode.LeftShift))
                        {
                            _nextMove = () => _selectedWalker.Roam(64, 16, checkNext);
                        }
                        else
                        {
                            var building = Dependencies.Get<IBuildingManager>().GetBuilding(mousePosition).FirstOrDefault();
                            if (building == null)
                                _nextMove = () => _selectedWalker.Walk(mousePosition, checkNext);
                            else
                                _nextMove = () => _selectedWalker.Walk(building, checkNext);
                        }

                        checkNext();
                    }
                }

                onApplied();
            }
        }

        private void checkNext()
        {
            if (_selectedWalker == null || _nextMove == null)
                return;

            if (_selectedWalker.IsWalking)
            {
                if (_selectedWalker.CurrentWalking != null || _selectedWalker.CurrentAgentWalking != null)
                    _selectedWalker.CancelWalk();
                else if (_selectedWalker.CurrentRoaming != null)
                    _selectedWalker.CancelRoam();
            }
            else
            {
                var move = _nextMove;
                _nextMove = null;//avoid stack overflow if move finishes immediately
                move();
            }
        }
    }
}