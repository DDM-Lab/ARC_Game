using CityBuilderCore;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// tool that places <see cref="TownPathTask"/> at available points
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_path_tool.html")]
    public class TownPathTool : PointerToolBase
    {
        [Tooltip("task that is placed by this tool, the points are checked against the road that will be placed by the task")]
        public TownPathTask Task;

        public override string TooltipName => Task.Road.Name;

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
                points = PositionHelper.GetRoadPositions(dragStart, mousePoint);
            else if (IsTouchActivated)
                points = new Vector2Int[] { };
            else
                points = new Vector2Int[] { mousePoint };

            List<Vector2Int> validPoints = new List<Vector2Int>();
            List<Vector2Int> invalidPoints = new List<Vector2Int>();

            foreach (var point in points)
            {
                if (!TownManager.Instance.CheckTask(point) && Dependencies.Get<IStructureManager>().CheckAvailability(point, Task.Road.Level.Value, Task.Road))
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
                    TownManager.Instance.CreateTask(Task, point);
                }

                onApplied();
            }
        }
    }
}
