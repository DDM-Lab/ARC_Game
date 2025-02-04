using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// passes a connection and influences building efficiency from its connection value
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection_passer_component.html")]
    public class ConnectionPasserComponent : BuildingComponent, IConnectionPasser, IEfficiencyFactor
    {
        public override string Key => "CPC";

        [Tooltip("the connection that will be passed and used to influence building efficiency")]
        public Connection Connection;
        [Tooltip(@"makes the connection one-way, values go in but are not passed along further
the highest value going in is used for all points of the building")]
        public bool IsConsumer;
        [Tooltip("the minimum efficiency returned so the building does not stall even in a bad position")]
        public float MinValue = 0;
        [Tooltip("the connection value needed to reach max efficiency")]
        public int MaxConnectionValue = 10;

        [Tooltip("fired for any point of the passer that changes its value")]
        public PointValueEvent PointValueChanged;
        [Tooltip("fired when the connection value at the building point goes to or above 0")]
        public BoolEvent IsWorkingChanged;

        public event Action<PointsChanged<IConnectionPasser>> PointsChanged;
        Connection IConnectionPasser.Connection => Connection;
        bool IConnectionPasser.IsConsumer => IsConsumer;

        public bool IsWorking => _value > 0;
        public float Factor => Mathf.Max(MinValue, Mathf.Min(1f, _value / (float)MaxConnectionValue));

        private int _value;

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Dependencies.Get<IConnectionManager>().Register(this);

            Building.PointsChanged += buildingPointsChanged;
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            Dependencies.Get<IConnectionManager>().Deregister(this);
            var passerReplacement = replacement.GetBuildingComponents<ConnectionPasserComponent>().FirstOrDefault(c => c.Connection == Connection);
            if (passerReplacement != null)
                Dependencies.Get<IConnectionManager>().Register(passerReplacement);
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            Dependencies.Get<IConnectionManager>().Deregister(this);
        }

        public IEnumerable<Vector2Int> GetPoints() => Building.GetPoints();

        public void ValueChanged(Vector2Int point, int value)
        {
            PointValueChanged?.Invoke(point, value);
            if (point == Building.Point)
            {
                bool wasWorking = IsWorking;

                _value = value;

                if (wasWorking != IsWorking)
                    IsWorkingChanged?.Invoke(IsWorking);
            }
        }

        protected virtual void buildingPointsChanged(PointsChanged<IStructure> e)
        {
            PointsChanged?.Invoke(new PointsChanged<IConnectionPasser>(this, e.RemovedPoints, e.AddedPoints));
        }
    }
}