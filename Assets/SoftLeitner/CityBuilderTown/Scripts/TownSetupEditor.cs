#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderTown
{
    [CustomEditor(typeof(TownSetup))]
    public class TownSetupEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Setup"))
                ((TownSetup)target).Setup();
            if (GUILayout.Button("Clear"))
                ((TownSetup)target).Clear();
        }
    }
}
#endif