using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// defines a layer value and falloff for a tile on a tilemap<br/>
    /// is used by <see cref="DefaultLayerManager"/> to establish the base layer values
    /// </summary>
    [Serializable]
    public class LayerAffectorTile
    {
        [Tooltip("the tile that is checked against")]
        public TileBase Tile;
        [Tooltip("the layer the value will be added to")]
        public Layer Layer;
        [Tooltip("value inside the affector")]
        public int Value;
        [Tooltip("range of points outside the affector")]
        public int Range;
        [Tooltip("value subtracted for every step outside the affector")]
        public int Falloff;

        private List<Vector2Int> _positions = new List<Vector2Int>();

        public void AddPosition(Vector2Int point)
        {
            _positions.Add(point);
        }

        public int GetValue(Vector2Int point)
        {
            if (_positions.Count == 0)
                return 0;
            if (_positions.Contains(point))
                return Value;
            var distance = _positions.Min(p => p.GetMaxAxisDistance(point));
            if (distance > Range)
                return 0;
            else
                return Value - distance * Falloff;
        }
    }
}