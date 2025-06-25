using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// can be put on structures or building parts to get notified when a layer it is on changes<br/>
    /// for example housing or roads that evolve when the area is nice enough
    /// </summary>
    public interface ILayerDependency
    {
        /// <summary>
        /// gets called when 
        /// </summary>
        /// <param name="points"></param>
        void CheckLayers(IEnumerable<Vector2Int> points);
    }
}