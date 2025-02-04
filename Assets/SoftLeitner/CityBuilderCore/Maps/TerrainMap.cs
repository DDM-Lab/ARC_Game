using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// terrain based map implementation, whether map points are buildable depends on the terrain<br/>
    /// currently only checks if the height of the terrain is in an acceptable range to see if it can be built on
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_terrain_map.html")]
    [RequireComponent(typeof(Grid))]
    public class TerrainMap : DefaultMap
    {
        [System.Serializable]
        public class TaggedLimit
        {
            public Object Tag;
            [Tooltip("inclusive minimum height that is acceptable for building")]
            public float MinHeight;
            [Tooltip("inclusive maximum height that is acceptable for building")]
            public float MaxHeight;
            [Range(0, 90)]
            [Tooltip("inclusive minimum steepness that is acceptable for building")]
            public float MinSteepness;
            [Range(0, 90)]
            [Tooltip("inclusive maximum steepness that is acceptable for building")]
            public float MaxSteepness = 90;
        }

        [Header("Terrain")]
        [Tooltip("terrain that will be sampled when height is checked")]
        public Terrain Terrain;
        [Tooltip("inclusive minimum height that is acceptable for building")]
        public float MinHeight = -1000;
        [Tooltip("inclusive maximum height that is acceptable for building")]
        public float MaxHeight = 1000;
        [Range(0, 90)]
        [Tooltip("inclusive minimum steepness that is acceptable for building")]
        public float MinSteepness;
        [Range(0, 90)]
        [Tooltip("inclusive maximum steepness that is acceptable for building")]
        public float MaxSteepness = 90;
        [Tooltip("allows defining different requirements depending on the Building, Road, Structure, ...")]
        public TaggedLimit[] TaggedLimits;

        private Dictionary<object, TaggedLimit> _taggedDict;

        public override bool IsBuildable(Vector2Int point, int mask, object tag = null)
        {
            if (!base.IsBuildable(point, mask, tag))
                return false;

            if (tag != null && _taggedDict == null)
            {
                _taggedDict = new Dictionary<object, TaggedLimit>();
                if (TaggedLimits != null && TaggedLimits.Length > 0)
                {
                    foreach (var taggedLimit in TaggedLimits)
                    {
                        _taggedDict.Add(taggedLimit.Tag, taggedLimit);
                    }
                }
            }

            var minHeight = MinHeight;
            var maxHeight = MaxHeight;

            var minSteepness = MinSteepness;
            var maxSteepness = MaxSteepness;

            if (tag != null && _taggedDict.ContainsKey(tag))
            {
                var tagged = _taggedDict[tag];

                minHeight = tagged.MinHeight;
                maxHeight = tagged.MaxHeight;

                minSteepness = tagged.MinSteepness;
                maxSteepness = tagged.MaxSteepness;
            }

            var height = Terrain.SampleHeight(GetCenterFromPosition(GetWorldPosition(point)));
            if (height < minHeight || height > maxHeight)
                return false;

            if (minSteepness != 0 || maxSteepness != 90)
            {
                var steepness = Terrain.terrainData.GetSteepness(GetWorldPosition(point).x / Terrain.terrainData.size.x, GetWorldPosition(point).z / Terrain.terrainData.size.z);
                if (steepness < minSteepness || steepness > maxSteepness)
                    return false;
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (Terrain && (MinHeight != -1000 || MaxHeight != 1000))
            {
                var bottom = WorldCenter + new Vector3(0, Terrain.transform.position.y + MinHeight, 0);
                var top = WorldCenter + new Vector3(0, Terrain.transform.position.y + MaxHeight, 0);

                var center = (bottom + top) / 2;
                var size = new Vector3(WorldSize.x, MaxHeight - MinHeight, WorldSize.z);

                Gizmos.color = new Color(0, 1, 0, 0.2f);
                Gizmos.DrawWireCube(center, size);
            }
        }
    }
}
