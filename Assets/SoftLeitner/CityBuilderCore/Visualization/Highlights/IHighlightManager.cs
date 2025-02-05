using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// highlights tiles as valid, invalid or just as info<br/>
    /// primarily used by building tools
    /// </summary>
    public interface IHighlightManager
    {
        /// <summary>
        /// removes any previously made highlights
        /// </summary>
        void Clear();
        /// <summary>
        /// highlights points on the map
        /// </summary>
        /// <param name="points">points on the map to highlight</param>
        /// <param name="valid">true to display as valid(green), false for invalid(red)</param>
        void Highlight(IEnumerable<Vector2Int> points, bool valid);
        /// <summary>
        /// highlights points on the map
        /// </summary>
        /// <param name="points">points on the map to highlight</param>
        /// <param name="type">how the points should be visualized(valid>green, invalid>red, info>blue)</param>
        void Highlight(IEnumerable<Vector2Int> points, HighlightType type);
        /// <summary>
        /// highlights points on the map
        /// </summary>
        /// <param name="points">points on the map to highlight</param>
        /// <param name="color">color to visualize the highlight in</param>
        void Highlight(IEnumerable<Vector2Int> points, Color color);
        /// <summary>
        /// highlights a point on the map
        /// </summary>
        /// <param name="point">point on the map to highlight</param>
        /// <param name="valid">true to display as valid(green), false for invalid(red)</param>
        void Highlight(Vector2Int point, bool isValid);
        /// <summary>
        /// highlights a point on the map
        /// </summary>
        /// <param name="point">point on the map to highlight</param>
        /// <param name="type">how the points should be visualized(valid>green, invalid>red, info>blue)</param>
        void Highlight(Vector2Int point, HighlightType type);
        /// <summary>
        /// highlights points on the map
        /// </summary>
        /// <param name="points">points on the map to highlight</param>
        /// <param name="color">color to visualize the highlight in</param>
        void Highlight(Vector2Int point, Color color);
    }
}