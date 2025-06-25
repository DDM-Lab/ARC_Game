#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomEditor(typeof(ConnectedTileBase), true)]
    [CanEditMultipleObjects]
    internal class ConnectedTileBaseEditor : Editor
    {
        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            var tile = (ConnectedTileBase)target;
            var sprite = tile.GetPreviewSprite();

            if (sprite == null)
                return null;

            return RenderTilePreview(width, height, sprite, tile.IsColored, tile.Color);
        }

        public static Texture2D RenderTilePreview(int width, int height, Sprite sprite, bool isColored, Color color)
        {
            Texture2D spritePreview = AssetPreview.GetAssetPreview(sprite);
            if (spritePreview == null)
                return null;

            Texture2D preview = new Texture2D(width, height);
            EditorUtility.CopySerialized(spritePreview, preview);

            if (isColored)
            {
                Color[] pixels = preview.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = pixels[i] * color;
                }
                preview.SetPixels(pixels);
                preview.Apply();
            }

            return preview;
        }
    }
}
#endif