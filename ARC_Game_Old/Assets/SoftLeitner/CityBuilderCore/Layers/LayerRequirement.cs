using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// defines a valid range for a layer value<br/>
    /// used in <see cref="BuildingRequirement"/> to check if a building can be built on a point<br/>
    /// used by <see cref="EvolutionStage"/> and <see cref="RoadStage"/> to check an evolution
    /// </summary>
    [Serializable]
    public class LayerRequirement
    {
        [Tooltip("the layer whose value will be checked")]
        public Layer Layer;
        [Tooltip("inclusive minimum valid value")]
        public int MinValue = int.MinValue;
        [Tooltip("inclusive maximum valid value")]
        public int MaxValue = int.MaxValue;

        public int GetValue(Vector2Int point)
        {
            return Dependencies.Get<ILayerManager>().GetValue(point, Layer);
        }
        public bool CheckValue(int value)
        {
            return value >= MinValue && value <= MaxValue;
        }
        public bool IsFulfilled(Vector2Int point, ILayerManager layerManager = null)
        {
            var value = (layerManager ?? Dependencies.Get<ILayerManager>()).GetValue(point, Layer);

            return value >= MinValue && value <= MaxValue;
        }
    }
}