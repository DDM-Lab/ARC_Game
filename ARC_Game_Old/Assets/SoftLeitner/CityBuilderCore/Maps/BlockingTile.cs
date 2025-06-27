using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class that combines a tile with a structure level<br/>
    /// used in <see cref="DefaultMap"/> to express which tiles block the placement of certain building levels<br/>
    /// for example a lava tile could block all building levels, mountain tiles could allow structures below ground<br/>
    /// basically this can be used as an additional building requirement that applies to all buildings
    /// </summary>
    [Serializable]
    public class BlockingTile
    {
        [Tooltip("the tile that will be looked for on the tilemap")]
        public TileBase Tile;
        [Tooltip("determines which levels get blocked, structures that occupy the same level cannot be built on the tile")]
        public StructureLevelMask Level;
    }
}
