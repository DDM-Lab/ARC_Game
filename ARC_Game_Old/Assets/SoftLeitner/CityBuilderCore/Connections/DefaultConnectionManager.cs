using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// straightforward conn manager implementation, should suffice for most cases
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_connection_manager.html")]
    public class DefaultConnectionManager : MonoBehaviour, IConnectionManager
    {
        public event Action<Connection> Changed;

        private Dictionary<Connection, ConnectionGrid> _grids = new Dictionary<Connection, ConnectionGrid>();

        private void Awake()
        {
            Dependencies.Register<IConnectionManager>(this);
        }

        private void Start()
        {
            this.StartChecker(checkGrids);
        }

        public void Calculate()
        {
            checkGrids(true);
        }

        public void Register(IConnectionPasser passer)
        {
            if (passer is IConnectionFeeder feeder)
                getGrid(passer.Connection, true).RegisterFeeder(feeder);
            else
                getGrid(passer.Connection, true).RegisterPasser(passer);
        }
        public void Deregister(IConnectionPasser passer)
        {
            if (passer is IConnectionFeeder feeder)
                getGrid(passer.Connection, true).DeregisterFeeder(feeder);
            else
                getGrid(passer.Connection, true).DeregisterPasser(passer);
        }

        public bool HasPoint(Connection connection, Vector2Int point) => getGrid(connection, false)?.HasPoint(point) ?? false;
        public int GetValue(Connection connection, Vector2Int point) => getGrid(connection, false)?.GetValue(point) ?? 0;
        public Dictionary<Vector2Int, int> GetValues(Connection connection) => getGrid(connection, false)?.GetValues()??new Dictionary<Vector2Int, int>();
        public ConnectionGrid GetGrid(Connection connection) => getGrid(connection, false);

        private ConnectionGrid getGrid(Connection connection, bool add)
        {
            if (_grids.ContainsKey(connection))
                return _grids[connection];

            if (add)
            {
                var grid = new ConnectionGrid(connection);
                grid.Changed += e =>
                {
                    Changed?.Invoke(connection);
                };
                _grids.Add(connection, grid);
                return grid;
            }
            {
                return null;
            }
        }

        private void checkGrids() => checkGrids(false);
        private void checkGrids(bool force)
        {
            _grids.Values.ForEach(g => g.Check(force));
        }
    }
}
