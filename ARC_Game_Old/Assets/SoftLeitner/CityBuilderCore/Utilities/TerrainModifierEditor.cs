#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomEditor(typeof(TerrainModifier))]
    public class TerrainModifierEditor : Editor
    {
        private string _elevation = "0";
        private bool _showTools = false;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            _showTools = EditorGUILayout.Foldout(_showTools, "Tools");

            if (_showTools)
            {
                //USE NAVMESHUPDATER INSTEAD
                //if (GUILayout.Button("Bake NavMesh without Trees"))
                //{
                //    var terrain = ((TerrainModifier)target).GetComponent<Terrain>();
                //    var originalData = terrain.terrainData;

                //    terrain.terrainData = Instantiate(terrain.terrainData);
                //    terrain.terrainData.SetTreeInstances(new TreeInstance[] { }, false);

                //    NavMeshBuilder.BuildNavMesh();

                //    terrain.terrainData = originalData;
                //}

                _elevation = GUILayout.TextField(_elevation);

                if (GUILayout.Button("Elevate Heights and Trees") && float.TryParse(_elevation, out float elevation))
                {
                    var terrain = ((TerrainModifier)target).GetComponent<Terrain>();
                    var data = terrain.terrainData;

                    var trees = data.treeInstances.ToList();
                    for (int i = 0; i < trees.Count; i++)
                    {
                        var tree = trees[i];

                        tree.position += Vector3.up * elevation / terrain.terrainData.size.y;

                        trees[i] = tree;
                    }
                    data.SetTreeInstances(trees.ToArray(), false);

                    var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                    for (int x = 0; x < data.heightmapResolution; x++)
                    {
                        for (int y = 0; y < data.heightmapResolution; y++)
                        {
                            heights[x, y] = heights[x, y] + elevation / terrain.terrainData.size.y;
                        }
                    }
                    data.SetHeights(0, 0, heights);
                }
            }
        }
    }
}
#endif