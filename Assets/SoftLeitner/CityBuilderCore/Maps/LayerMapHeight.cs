using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// applies height to objects by changing their layer when there is a height override that fits one of the <see cref="Heights"/><br/>
    /// used in the urban tunnel demo to change cars in the underground to the underground layer
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_layer_map_height.html")]
    public class LayerMapHeight : MonoBehaviour, IGridHeights
    {
        [Serializable]
        public class LayerHeight
        {
            [Layer]
            [Tooltip("the layer that will be applied to signify height")]
            public int Layer;
            [Tooltip("the height override at which this entry is used")]
            public int Height;
        }

        [Layer]
        [Tooltip("the default layer that is applied when there is no height override or when no fitting entry could be found")]
        public int DefaultLayer;
        [Tooltip("the different combinations of override height and layer")]
        public LayerHeight[] Heights;

        private Dictionary<int, int> _layers;

        private void Awake()
        {
            Dependencies.Register<IGridHeights>(this);
        }

        private void Start()
        {
            _layers = new Dictionary<int, int>();
            foreach (var height in Heights)
            {
                _layers.Add(height.Height, height.Layer);
            }
        }

        public float GetHeight(Vector3 position, PathType pathType = PathType.Map)
        {
            return 0f;
        }

        public void ApplyHeight(Transform transform, Vector3 position, PathType pathType = PathType.Map, float? overrideValue = null)
        {
            var layer = getLayer(overrideValue);

            if (transform.gameObject.layer == layer)
                return;

            transform.gameObject.layer = layer;
            foreach (Transform child in transform)
            {
                child.gameObject.layer = layer;
            }
        }

        public Vector3 ApplyHeight(Vector3 position, PathType pathType = PathType.Map, float? overrideValue = null)
        {
            return position;
        }

        private int getLayer(float? height)
        {
            if (!height.HasValue)
                return DefaultLayer;

            if (!_layers.TryGetValue(Mathf.RoundToInt(height.Value), out int layer))
                return DefaultLayer;

            return layer;
        }
    }
}
