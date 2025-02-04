#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomEditor(typeof(ConnectableTile), true)]
    [CanEditMultipleObjects]
    internal class ConnectableTileEditor : Editor
    {
        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            var tile = (ConnectableTile)target;

            if (tile.Sprite == null)
                return null;

            return ConnectedTileBaseEditor.RenderTilePreview(width, height, tile.Sprite, tile.IsColored, tile.Color);
        }
    }
}
#endif