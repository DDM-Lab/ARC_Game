using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(HeightMappedTerrainRenderer), true)]
    [CanEditMultipleObjects]
    public class HeightMappedTerrainRendererEditor:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Assign"))
                ((HeightMappedTerrainRenderer)target).Assign();
        }
    }
}
