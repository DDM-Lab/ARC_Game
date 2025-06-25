using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for any behaviour that might pass a connection<br/>
    /// passers perpetuate a connection at their points and get notified when any of these changes its connection value<br/>
    /// this can be used for things that link up connections(pipes, wires, ..) and also things that use that connection(households)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection_passer_base.html")]
    public abstract class ConnectionPasserBase : MonoBehaviour, IConnectionPasser
    {
        [Tooltip("the connection that is passed by this behaviour")]
        public Connection Connection;

        [Tooltip("fired for any point of the passer that changes its value")]
        public PointValueEvent PointValueChanged;

        Connection IConnectionPasser.Connection => Connection;
        bool IConnectionPasser.IsConsumer => false;

        public event Action<PointsChanged<IConnectionPasser>> PointsChanged;

        protected virtual void Start()
        {
            Dependencies.Get<IConnectionManager>().Register(this);
        }

        protected virtual void OnDestroy()
        {
            if (gameObject.scene.isLoaded)
                Dependencies.Get<IConnectionManager>().Deregister(this);
        }

        public abstract IEnumerable<Vector2Int> GetPoints();

        public void ValueChanged(Vector2Int point, int value)
        {
            PointValueChanged?.Invoke(point, value);
        }

        protected void onPointsChanged(IEnumerable<Vector2Int> removed, IEnumerable<Vector2Int> added) => PointsChanged?.Invoke(new PointsChanged<IConnectionPasser>(this, removed, added));
    }
}
