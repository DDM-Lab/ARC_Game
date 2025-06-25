using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// common interface for objects that handle grid links<br/>
    /// made for convenience so road and map linking is interchangable
    /// </summary>
    public interface IGridLinker
    {
        /// <summary>
        /// adds a link to the linker
        /// </summary>
        /// <param name="link">the link to be added</param>
        /// <param name="tag">additional information, for example the Road when the link should be added to a specific road network</param>
        void RegisterLink(IGridLink link, object tag);
        /// <summary>
        /// removes a link from the linker
        /// </summary>
        /// <param name="link">the link to be removed</param>
        /// <param name="tag">additional information, for example the Road when the link should be removed to a specific road network</param>
        void DeregisterLink(IGridLink link, object tag);
        /// <summary>
        /// retrieves all links starting at a specific point
        /// </summary>
        /// <param name="start">the point at which to look for lniks</param>
        /// <param name="tag">additional parameter</param>
        /// <returns></returns>
        IEnumerable<IGridLink> GetLinks(Vector2Int start, object tag);
        /// <summary>
        /// retrieves a link connecting two specific points if one exists
        /// </summary>
        /// <param name="start">start point of the potential link</param>
        /// <param name="end">end point of the potential link</param>
        /// <param name="tag">additional parameter</param>
        /// <returns></returns>
        IGridLink GetLink(Vector2Int start, Vector2Int end, object tag);
    }
    /// <summary>
    /// manages links between roads
    /// </summary>
    public interface IRoadGridLinker : IGridLinker { }
    /// <summary>
    /// manages links between road points
    /// </summary>
    public interface IMapGridLinker : IGridLinker { }
}
