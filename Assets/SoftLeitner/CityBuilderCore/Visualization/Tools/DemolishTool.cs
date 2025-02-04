using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// tool that removes structures
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_demolish_tool.html")]
    public class DemolishTool : PointerToolBase
    {
        [Tooltip("is displayed as its tooltip")]
        public string Name;
        [Tooltip("optional effect that gets added for removed buildings")]
        public DemolishVisual Visual;
        [Tooltip("determines which structures are affected by this tool")]
        public StructureLevelMask Level;
        [Tooltip("if the above level affected no structures the first one of these is demolished, then the second and so on(can be used to demolish upper structures before lower ones, check Placement demo scene)")]
        public StructureLevelMask[] LevelsNext;

        public override string TooltipName => Name;

        private IHighlightManager _highlighting;

        protected override void Start()
        {
            base.Start();

            _highlighting = Dependencies.Get<IHighlightManager>();
        }

        protected override void updatePointer(Vector2Int mousePoint, Vector2Int dragStart, bool isDown, bool isApply)
        {
            _highlighting.Clear();

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

            _highlighting.Highlight(points, false);

            if (isApply)
            {
                if (Dependencies.Get<IStructureManager>().Remove(points, Level.Value, false, structure => DemolishVisual.Create(Visual, structure as IBuilding)) > 0)
                {
                    onApplied();
                }
                else if (LevelsNext != null)
                {
                    foreach (var level in LevelsNext)
                    {
                        if(Dependencies.Get<IStructureManager>().Remove(points, level.Value, false, structure => DemolishVisual.Create(Visual, structure as IBuilding)) > 0)
                        {
                            onApplied();
                            break;
                        }
                    }
                }
            }
        }
    }
}