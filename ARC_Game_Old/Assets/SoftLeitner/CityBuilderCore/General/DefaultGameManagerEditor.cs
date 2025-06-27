#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomEditor(typeof(DefaultGameManager))]
    public class DefaultGameManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            DefaultGameManager manager = (DefaultGameManager)target;

            if (GUILayout.Button("Save"))
            {
                manager.Save();
            }

            if (GUILayout.Button("Load"))
            {
                manager.Load();
            }
        }
    }
}
#endif