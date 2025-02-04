using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// tilemap based map implementation<br/>
    /// whether map points are walkable or buildable depends on the tiles on a tilemap<br/>
    /// the <see cref="BuildingRequirement.GroundOptions"/> have to be tile when used with this map
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_map.html")]
    [RequireComponent(typeof(Grid))]
    public class DefaultMap : MapBase
    {
        [System.Serializable]
        public class TaggedTiles
        {
            public Object Tag;
            public TileBase[] NeededTiles;
            public TileBase[] BlockedTiles;
        }

        [Header("Tilemap")]
        [Tooltip("tilemap containing the blocking tiles, if empty nothing is blocked")]
        public Tilemap Ground;
        [Tooltip("tiles that are blocked in mapgrid pathfinding")]
        public TileBase[] WalkingBlockingTiles;
        [Tooltip("tiles that are blocked for building")]
        public BlockingTile[] BuildingBlockingTiles;
        [Tooltip("allows defining tiles that certain buildings, roads or structures need to be built on or cannot be built on")]
        public TaggedTiles[] TaggedBuildingTiles;

        private Dictionary<object, TaggedTiles> _taggedDict;

        public override bool IsBuildable(Vector2Int point, int mask, object tag = null)
        {
            if (Ground)
            {
                var tile = Ground.GetTile((Vector3Int)point);

                if (tag != null)
                {
                    if (_taggedDict == null)
                    {
                        _taggedDict = new Dictionary<object, TaggedTiles>();
                        if (TaggedBuildingTiles != null && TaggedBuildingTiles.Length > 0)
                        {
                            foreach (var taggedTile in TaggedBuildingTiles)
                            {
                                _taggedDict.Add(taggedTile.Tag, taggedTile);
                            }
                        }
                    }

                    if (_taggedDict.ContainsKey(tag))
                    {
                        var tiles = _taggedDict[tag];

                        if (tiles.NeededTiles != null && tiles.NeededTiles.Length > 0 && !tiles.NeededTiles.Contains(tile))
                            return false;

                        if (tiles.BlockedTiles != null && tiles.BlockedTiles.Contains(tile))
                            return false;
                    }
                }

                return !BuildingBlockingTiles.Where(b => b.Level.Check(mask)).Select(b => b.Tile).Contains(tile);
            }
            else
            {
                return true;
            }
        }

        public override bool IsWalkable(Vector2Int point)
        {
            if (Ground)
                return !WalkingBlockingTiles.Contains(Ground.GetTile((Vector3Int)point));
            else
                return true;
        }

        public override bool CheckGround(Vector2Int point, Object[] options)
        {
            if (Ground)
                return options.Contains(Ground.GetTile((Vector3Int)point));
            else
                return true;
        }
    }
}