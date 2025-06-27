using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// connecting tile for rectangle grids, used in the historic demo
    /// also used for the roads in THREE
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connected_rectangle_tile.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Tiles/" + nameof(ConnectedRectangleTile))]
    public class ConnectedRectangleTile : ConnectedTileBase
    {
        [Tooltip("none | S | S-N | S-E | W-S-E | all")]
        public Sprite[] Sprites = new Sprite[6];
        [Tooltip("none | B | B-F | B-R | L-B-R | all")]
        public GameObject[] Prefabs = new GameObject[6];

        public override Sprite GetPreviewSprite() => Sprites.LastOrDefault();

        protected override Quaternion getRotation(byte mask)
        {
            switch (mask)
            {
                //NONE
                case 0: return Quaternion.Euler(0f, 0f, 0f);
                //SINGLE
                case 1: return Quaternion.Euler(0f, 0f, 180f);
                case 2: return Quaternion.Euler(0f, 0f, 90f);
                case 4: return Quaternion.Euler(0f, 0f, 0f);
                case 8: return Quaternion.Euler(0f, 0f, 270f);
                //DOUBLE I
                case 5: return Quaternion.Euler(0f, 0f, 0f);
                case 10: return Quaternion.Euler(0f, 0f, 90f);
                //DOUBLE L
                case 3: return Quaternion.Euler(0f, 0f, 90f);
                case 6: return Quaternion.Euler(0f, 0f, 0f);
                case 9: return Quaternion.Euler(0f, 0f, 180f);
                case 12: return Quaternion.Euler(0f, 0f, 270f);
                //TRIPLE
                case 7: return Quaternion.Euler(0f, 0f, 90f);
                case 11: return Quaternion.Euler(0f, 0f, 180f);
                case 13: return Quaternion.Euler(0f, 0f, 270f);
                case 14: return Quaternion.Euler(0f, 0f, 0f);
                //FULL
                case 15: return Quaternion.Euler(0f, 0f, 0f);
            }

            return Quaternion.Euler(0f, 0f, 0f);
        }

        protected override Vector3 getScale(byte mask) => Vector3.one;

        protected override Sprite getSprite(byte mask) => Sprites.ElementAtOrDefault(getSpriteIndex(mask));
        protected override GameObject getPrefab(byte mask) => Prefabs.ElementAtOrDefault(getSpriteIndex(mask));
        
        private int getSpriteIndex(byte mask)
        {
            switch (mask)
            {
                //NONE
                case 0: return 0;
                //SINGLE
                case 1:
                case 2:
                case 4:
                case 8: return 1;
                //DOUBLE I
                case 5:
                case 10: return 2;
                //DOUBLE L
                case 3:
                case 6:
                case 9:
                case 12: return 3;
                //TRIPLE
                case 7:
                case 11:
                case 13:
                case 14: return 4;
                //FULL
                case 15: return 5;
            }

            return -1;
        }
    }
}
