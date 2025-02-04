#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomEditor(typeof(NavMeshUpdater))]
    public class NavMeshUpdaterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Update"))
                ((NavMeshUpdater)target).UpdateNavMesh();
        }
    }
}
#endif