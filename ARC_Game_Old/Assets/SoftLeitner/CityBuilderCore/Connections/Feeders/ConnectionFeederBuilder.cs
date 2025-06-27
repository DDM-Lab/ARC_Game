using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// special building builder that registers as a preview feeder with its view connection
    /// this allows showing how connections would be changed without actually affecting them
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection_feeder_builder.html")]
    public class ConnectionFeederBuilder : BuildingBuilder, IConnectionFeeder
    {
        [Tooltip("preview feeder is added to this view")]
        public ViewConnection ViewConnection;
        [Tooltip("value at the point of the feeder")]
        public int Value;
        [Tooltip("how far the value of the feeder carries without falling off")]
        public int Range;
        [Tooltip("value subtracted for every step outside the range")]
        public int Falloff;

        int IConnectionFeeder.Value => Value;
        int IConnectionFeeder.Range => Range;
        int IConnectionFeeder.Falloff => Falloff;
        Connection IConnectionPasser.Connection => ViewConnection.Connection;
        bool IConnectionPasser.IsConsumer => false;

#pragma warning disable 0067
        public event Action<IConnectionFeeder> FeederValueChanged;
#pragma warning restore 0067
        public event Action<PointsChanged<IConnectionPasser>> PointsChanged;

        private List<Vector2Int> _points = new List<Vector2Int>();

        public IEnumerable<Vector2Int> GetPoints() => _points;
        public void ValueChanged(Vector2Int point, int value) { }

        public override void ActivateTool()
        {
            base.ActivateTool();

            ViewConnection.AddPreviewFeeder(this);
        }
        public override void DeactivateTool()
        {
            base.DeactivateTool();

            ViewConnection.RemovePreviewFeeder(this);
            _points.Clear();
        }

        protected override void updatePreview(List<Vector2Int> buildPoints, Vector2Int size, Func<Vector2Int, bool> validityChecker)
        {
            base.updatePreview(buildPoints, size, validityChecker);

            var oldPoints = _points.ToList();

            _points.Clear();

            if (buildPoints != null && buildPoints.Count > 0 && validityChecker(buildPoints[0]))
                _points.AddRange(PositionHelper.GetStructurePositions(buildPoints[0], size).ToList());

            PointsChanged?.Invoke(new PointsChanged<IConnectionPasser>(this, oldPoints, _points));
        }

        protected override void build(IEnumerable<Vector2Int> points)
        {
            ViewConnection.RemovePreviewFeeder(this);
            _points.Clear();

            base.build(points);

            Dependencies.Get<IConnectionManager>().Calculate();

            ViewConnection.AddPreviewFeeder(this);
        }
    }
}
