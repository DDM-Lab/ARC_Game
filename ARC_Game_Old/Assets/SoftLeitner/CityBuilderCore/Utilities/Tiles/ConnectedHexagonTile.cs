using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// tile that can be used for connections in hex grids with irregular sizes<br/>
    /// check out CityBuilderCore.Tests/Other/HexRoad for examples
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connected_hexagon_tile.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Tiles/" + nameof(ConnectedHexagonTile))]
    public class ConnectedHexagonTile : ConnectedTileBase
    {
        public Sprite Sprite_None;
        public Sprite[] Sprites_One = new Sprite[2];
        public Sprite[] Sprites_Two = new Sprite[6];
        public Sprite[] Sprites_Three = new Sprite[6];
        public Sprite[] Sprites_Four = new Sprite[6];
        public Sprite[] Sprites_Five = new Sprite[2];
        public Sprite Sprite_Six;

        public override Sprite GetPreviewSprite() => Sprite_Six;

        protected override byte getMask(Vector3Int location, ITilemap tilemap)
        {
            int mask = 0;

            if (location.y % 2 == 0)
            {
                mask += hasConnection(tilemap, location + new Vector3Int(0, 1, 0)) ? 1 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(1, 0, 0)) ? 2 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(0, -1, 0)) ? 4 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(-1, -1, 0)) ? 8 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(-1, 0, 0)) ? 16 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(-1, 1, 0)) ? 32 : 0;
            }
            else
            {
                mask += hasConnection(tilemap, location + new Vector3Int(1, 1, 0)) ? 1 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(1, 0, 0)) ? 2 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(1, -1, 0)) ? 4 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(0, -1, 0)) ? 8 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(-1, 0, 0)) ? 16 : 0;
                mask += hasConnection(tilemap, location + new Vector3Int(0, 1, 0)) ? 32 : 0;
            }

            return (byte)mask;
        }

        protected override Quaternion getRotation(byte mask) => Quaternion.identity;

        private static Vector3 s_n = Vector3.one;
        private static Vector3 s_x = new Vector3(-1, 1, 1);
        private static Vector3 s_y = new Vector3(1, -1, 1);
        private static Vector3 s_xy = new Vector3(-1, -1, 1);

        protected override Vector3 getScale(byte mask)
        {
            switch (mask)
            {
                //NONE
                case 0: return s_n;

                //ONE
                case 2: return s_n;
                case 16: return s_x;
                case 1: return s_n;
                case 4: return s_y;
                case 8: return s_xy;
                case 32: return s_x;

                //TWO
                case 18: return s_n;

                case 33: return s_n;
                case 12: return s_y;

                case 3: return s_n;
                case 6: return s_y;
                case 24: return s_xy;
                case 48: return s_x;

                case 34: return s_n;
                case 10: return s_y;
                case 20: return s_xy;
                case 17: return s_x;

                case 5: return s_n;
                case 40: return s_x;

                case 9: return s_n;
                case 36: return s_x;

                //THREE
                case 19: return s_n;
                case 22: return s_y;
                case 26: return s_xy;
                case 50: return s_x;

                case 37: return s_n;
                case 13: return s_y;
                case 44: return s_xy;
                case 41: return s_x;

                case 35: return s_n;
                case 14: return s_y;
                case 28: return s_xy;
                case 49: return s_x;

                case 11: return s_n;
                case 38: return s_y;
                case 25: return s_xy;
                case 52: return s_x;

                case 7: return s_n;
                case 56: return s_x;

                case 21: return s_n;
                case 42: return s_x;

                //FOUR
                case 51: return s_n;
                case 30: return s_y;

                case 39: return s_n;
                case 15: return s_y;
                case 60: return s_xy;
                case 57: return s_x;

                case 54: return s_n;
                case 27: return s_y;

                case 23: return s_n;
                case 58: return s_x;

                case 46: return s_n;
                case 43: return s_y;
                case 53: return s_xy;
                case 29: return s_x;

                case 45: return s_n;

                //FIVE
                case 31: return s_n;
                case 55: return s_y;
                case 59: return s_xy;
                case 62: return s_x;

                case 47: return s_n;
                case 61: return s_x;

                //SIX
                case 63: return s_n;
            }

            return s_n;
        }

        protected override Sprite getSprite(byte mask)
        {
            switch (mask)
            {
                //NONE
                case 0: return Sprite_None;
                //ONE
                case 2:
                case 16: return Sprites_One[0];
                case 1:
                case 4:
                case 8:
                case 32: return Sprites_One[1];
                //TWO
                case 18: return Sprites_Two[0];
                case 33:
                case 12: return Sprites_Two[1];
                case 3:
                case 6:
                case 24:
                case 48: return Sprites_Two[2];
                case 34:
                case 10:
                case 17:
                case 20: return Sprites_Two[3];
                case 5:
                case 40: return Sprites_Two[4];
                case 9:
                case 36: return Sprites_Two[5];
                //THREE
                case 19:
                case 22:
                case 50:
                case 26: return Sprites_Three[0];
                case 37:
                case 41:
                case 13:
                case 44: return Sprites_Three[1];
                case 35:
                case 49:
                case 14:
                case 28: return Sprites_Three[2];
                case 11:
                case 38:
                case 25:
                case 52: return Sprites_Three[3];
                case 7:
                case 56: return Sprites_Three[4];
                case 21:
                case 42: return Sprites_Three[5];
                //FOUR
                case 51:
                case 30: return Sprites_Four[0];
                case 39:
                case 15:
                case 60:
                case 57: return Sprites_Four[1];
                case 54:
                case 27: return Sprites_Four[2];
                case 23:
                case 58: return Sprites_Four[3];
                case 46:
                case 43:
                case 53:
                case 29: return Sprites_Four[4];
                case 45: return Sprites_Four[5];
                //FIVE
                case 31:
                case 62:
                case 59:
                case 55: return Sprites_Five[0];
                case 47:
                case 61: return Sprites_Five[1];
                //SIX
                case 63: return Sprite_Six;
            }

            return Sprite_None;
        }
    }
}
