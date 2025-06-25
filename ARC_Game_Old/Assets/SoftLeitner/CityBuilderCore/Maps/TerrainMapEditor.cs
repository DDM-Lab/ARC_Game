#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomEditor(typeof(TerrainMap))]
    public class TerrainMapEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Paint Buildable Heights");
            if (GUILayout.Button("GO"))
            {
                var map = (TerrainMap)target;
                var data = map.Terrain.terrainData;

                float[,,] splat = new float[data.alphamapWidth, data.alphamapHeight, data.alphamapLayers];

                float range = (map.MaxHeight - map.MinHeight) / 2f;
                float mid = (map.MinHeight + map.MaxHeight) / 2f;
                float neutral = range / 2f;

                range -= neutral;

                for (int y = 0; y < data.alphamapWidth; y++)
                {
                    for (int x = 0; x < data.alphamapHeight; x++)
                    {
                        float h_x = x / (float)data.alphamapWidth;
                        float h_y = y / (float)data.alphamapHeight;

                        float height = data.GetHeight(Mathf.RoundToInt(h_y * data.heightmapResolution), Mathf.RoundToInt(h_x * data.heightmapResolution));

                        float floor;
                        float lava;

                        if (height > map.MaxHeight || height < map.MinHeight)
                        {
                            floor = 0f;
                            lava = 1f;
                        }
                        else
                        {
                            var delta = (Mathf.Abs(height - mid) - neutral) / range;

                            if (delta < 0)
                            {
                                floor = 1f;
                                lava = 0f;
                            }
                            else
                            {
                                floor = 1 - delta;
                                lava = delta;
                            }
                        }

                        splat[x, y, 0] = floor;
                        splat[x, y, 1] = lava;
                    }
                }

                data.SetAlphamaps(0, 0, splat);

                EditorUtility.SetDirty(data);
            }
            GUILayout.EndHorizontal();
        }
    }
}
#endif