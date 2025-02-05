using System.Collections.Generic;
using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// handles connections and their values
    /// </summary>
    public interface IConnectionManager
    {
        /// <summary>
        /// fired whenever the points of a connection have changed
        /// </summary>
        event Action<Connection> Changed;

        /// <summary>
        /// can be used to force calculation if the value of a connection is needed directly after changing it
        /// </summary>
        void Calculate();

        /// <summary>
        /// registers a passer with the manager, the passers points can now be used by its connection and it will be notified if any of its values changed
        /// </summary>
        /// <param name="passer">the passer that should contribute to connections</param>
        void Register(IConnectionPasser passer);
        /// <summary>
        /// removes a passer with its points from connection handling
        /// </summary>
        /// <param name="passer">the passer that should no longer contribute to connections</param>
        void Deregister(IConnectionPasser passer);

        /// <summary>
        /// checks if a connection has a point regardless of its value
        /// </summary>
        /// <param name="connection">the connection to check</param>
        /// <param name="point">the point at which to check</param>
        /// <returns>true if there is a fitting passer at the point</returns>
        bool HasPoint(Connection connection, Vector2Int point);
        /// <summary>
        /// gets the value of the connection at the specified point
        /// </summary>
        /// <param name="connection">the connection to check</param>
        /// <param name="point">the point at which we want to know the value</param>
        /// <returns>the value at the point</returns>
        int GetValue(Connection connection, Vector2Int point);
        /// <summary>
        /// returns all positions and values of a layer where the value differs from 0
        /// </summary>
        /// <param name="layer"></param>
        /// <returns></returns>
        Dictionary<Vector2Int, int> GetValues(Connection connection);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        ConnectionGrid GetGrid(Connection connection);
    }
}