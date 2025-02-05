using System;
using System.Collections.Generic;

namespace CityBuilderCore
{
    /// <summary>
    /// keeps track of all the active walkers in the game
    /// </summary>
    public interface IWalkerManager
    {
        /// <summary>
        /// fired when a new walker is registered(happens during initializion)
        /// </summary>
        event Action<Walker> WalkerRegistered;
        /// <summary>
        /// fired when a walker gets deregistered(happens during finish)
        /// </summary>
        event Action<Walker> WalkerDeregistered;

        /// <summary>
        /// retrieves a walker by its id, can be used to get walker references when loading
        /// </summary>
        /// <param name="id">unique identifier of the walker</param>
        /// <returns>the walker if any walker has the id</returns>
        Walker GetWalker(Guid id);
        /// <summary>
        /// all the walkers currently active
        /// </summary>
        /// <returns>all active walkers in the game</returns>
        IEnumerable<Walker> GetWalkers();

        /// <summary>
        /// registers a walker into the managers responsibility, called by walker on initialization
        /// </summary>
        /// <param name="walker"></param>
        void RegisterWalker(Walker walker);
        /// <summary>
        /// deregisters a walker from the managers responsibility, called by walker on finish
        /// </summary>
        /// <param name="walker"></param>
        void DeregisterWalker(Walker walker);
    }
}