using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class for <see cref="Road"/> which combines a layer requirement with road visuals<br/>
    /// this is useful for roads that may change appearance whensome layer requirement is met(THREE demo)<br/>
    /// if no such behaviour is needed just add a single stage and leave the requirement empty
    /// </summary>
    [Serializable]
    public class RoadStage
    {
        [Tooltip("requiremten that has to be met for the road to upgrade to this stage")]
        public LayerRequirement[] LayerRequirements;
        [Tooltip("key used to identify this road stage(in save games for example)")]
        public string Key;
        [Tooltip("tile used to visualize this road")]
        public TileBase Tile;
        [Tooltip("index used for road visualization(for example layer index on terrains)")]
        public int Index;
    }
}