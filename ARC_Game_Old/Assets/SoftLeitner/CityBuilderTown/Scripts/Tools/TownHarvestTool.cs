using CityBuilderCore;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// tool that places <see cref="TownHarvestTask"/> at points that have structures that match a harvest tasks PrimaryStructureKey<br/>
    /// harvest tasks have to be known to <see cref="TownManager"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_harvest_tool.html")]
    public class TownHarvestTool : PointerToolBase
    {
        [Tooltip("is displayed as its tooltip")]
        public string Name;

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

            List<Vector2Int> validPoints = new List<Vector2Int>();
            List<Vector2Int> invalidPoints = new List<Vector2Int>();

            foreach (var point in points)
            {
                if (TownManager.Instance.CheckHarvest(point))
                    validPoints.Add(point);
                else
                    invalidPoints.Add(point);
            }

            _highlighting.Highlight(validPoints, true);
            _highlighting.Highlight(invalidPoints, false);

            if (isApply && validPoints.Count > 0)
            {
                foreach (var point in validPoints)
                {
                    TownManager.Instance.CreateHarvest(point);
                }

                onApplied();
            }
        }
    }
}
