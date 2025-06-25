#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomEditor(typeof(ObjectGenerator))]
    public class ObjectGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Clear"))
            {
                ((ObjectGenerator)target).Clear();
            }

            if (GUILayout.Button("Generate"))
            {
                ((ObjectGenerator)target).Clear();
                ((ObjectGenerator)target).GetComponents<ObjectGenerator>().ForEach(g => g.Generate(false));
            }
        }
    }
}
#endif