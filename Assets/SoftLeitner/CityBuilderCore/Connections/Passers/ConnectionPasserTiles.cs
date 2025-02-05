using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// passes a connection on every point that a tile has on a tilemap, only evaluated which points those are at the start
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection_passer_tiles.html")]
    public class ConnectionPasserTiles : ConnectionPasserBase
    {
        [Tooltip("the tilemap that contains the pertinent tiles")]
        public Tilemap Tilemap;
        [Tooltip("the tiles at the points where we want the connection to pass")]
        public TileBase Tile;

        private List<Vector2Int> _points = new List<Vector2Int>();

        protected override void Start()
        {
            foreach (var position in Tilemap.cellBounds.allPositionsWithin)
            {
                if (Tile == null && Tilemap.HasTile(position) || Tilemap.GetTile(position) == Tile)
                {
                    _points.Add((Vector2Int)position);
                }
            }

            base.Start();
        }

        public override IEnumerable<Vector2Int> GetPoints() => _points;
    }
}
