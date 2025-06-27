using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// tracks all the <see cref="IStructure"/>s on the map<br/>
    /// used to check and retrieve structures at any point on the map
    /// </summary>
    public interface IStructureManager : ISaveData
    {
        /// <summary>
        /// fired whenever the points occupied by structures change
        /// </summary>
        event Action Changed;

        /// <summary>
        /// retrieves all the structures on the map that have any of the levels in the mask
        /// </summary>
        /// <param name="mask">the structure level to check, 0 for all</param>
        /// <returns>all the structures on the map that collide with the mask</returns>
        IEnumerable<IStructure> GetStructures(int mask);
        /// <summary>
        /// retrieves all the structures at a point on the map that fit some criteria
        /// </summary>
        /// <param name="point">a point on the map</param>
        /// <param name="mask">structure level to check, o for all</param>
        /// <param name="isWalkable">if you only want to check for structures that are walkable or non walkable</param>
        /// <param name="isUnderlying">whether to check for underlying structures</param>
        /// <param name="isDecorator">if you only want to check for decorators or non decorators</param>
        /// <returns></returns>
        IEnumerable<IStructure> GetStructures(Vector2Int point, int mask = 0, bool? isWalkable = null, bool? isUnderlying = null, bool? isDecorator = null);

        /// <summary>
        /// retrieves a structure by its unique key
        /// </summary>
        /// <param name="key">key of the structure, often set in the inspector</param>
        /// <returns>the structure if any was found</returns>
        IStructure GetStructure(string key);

        /// <summary>
        /// removes points from any structure that occupies that point<br/>
        /// for some structures that will remove the entire thing(buildings)
        /// </summary>
        /// <param name="points">the points on the map from which to remove structures</param>
        /// <param name="mask">structure levels to remove, 0 for all</param>
        /// <param name="decoratorsOnly">whether to only remove decorators, for example when building on top of them</param>
        /// <param name="removing">action invoked for any structure that will have points removed, can be used to create dust or place demolishing animations instead of the structure</param>
        /// <returns>how many structures were affected</returns>
        int Remove(IEnumerable<Vector2Int> points, int mask, bool decoratorsOnly, Action<IStructure> removing = null);

        /// <summary>
        /// adds a structure into the managers care, others will now be able to retrieve it from the manager<br/>
        /// structures on the map should always be registered with the manager, 
        /// </summary>
        /// <param name="structure">the structure that will be added to the manager</param>
        /// <param name="isUnderlying">whether the structure shold be stored in a special collection instead of a point dictionary, recommended for structures with low number but many points like road networks</param>
        void RegisterStructure(IStructure structure, bool isUnderlying = false);
        /// <summary>
        /// removes a previously registered structure from the manager
        /// </summary>
        /// <param name="structure">the structure that will be added to the manager</param>
        /// <param name="isUnderlying">should be the same as when <see cref="RegisterStructure(IStructure, bool)"/> was called</param>
        void DeregisterStructure(IStructure structure, bool isUnderlying = false);
    }
}