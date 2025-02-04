using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// links two points on the map or a road network<br/>
    /// registered with a <see cref="IGridLinker"/> so it can be found and added to pathfinding<br/>
    /// when a walker actually moves across it control is handed to the link
    /// </summary>
    public interface IGridLink
    {
        /// <summary>
        /// start point of the link, if it changes the link has to be reregistered
        /// </summary>
        Vector2Int StartPoint { get; }
        /// <summary>
        /// end point of the link, if it changes the link has to be reregistered
        /// </summary>
        Vector2Int EndPoint { get; }
        
        /// <summary>
        /// whether walkers can also move from end to start
        /// </summary>
        bool Bidirectional { get; }
        /// <summary>
        /// cost of traversing the link in pathfinding, by default moving 1 point has cost 10
        /// </summary>
        int Cost { get; }
        /// <summary>
        /// length of the link when a walker moves through it, longer distance can make the walker slower
        /// </summary>
        float Distance { get; }

        /// <summary>
        /// walker movement is done here while a walker is traversing a link<br/>
        /// this can be used to move walkers in atypical ways<br/>
        /// for example when moving up ramps or across bridges
        /// </summary>
        /// <param name="walker">the walker moving across the link</param>
        /// <param name="moved">how much the walker has already moved</param>
        /// <param name="start">the point the walker started at, relevant in bidirectional links</param>
        void Walk(Walker walker, float moved, Vector2Int start);
    }
}
