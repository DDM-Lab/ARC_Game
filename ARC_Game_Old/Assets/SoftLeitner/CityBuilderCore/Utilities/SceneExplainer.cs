#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [ExecuteInEditMode]
    public class SceneExplainer : MonoBehaviour
    {
        [Tooltip("allows disabling all 'About' buttons by setting this in the prefab")]
        public bool Disable;
        [TextArea(20, 50)]
        public string Text;

        public void OnEnable()
        {
            SceneView.duringSceneGui += duringSceneGui;
        }
        public void OnDisable()
        {
            SceneView.duringSceneGui -= duringSceneGui;
        }

        private void duringSceneGui(SceneView sceneview)
        {
            if (Disable)
                return;

            Handles.BeginGUI();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var buttonStyle = new GUIStyle("button");
            buttonStyle.fontSize = 28;
            if (GUILayout.Button("About", buttonStyle, GUILayout.Width(120), GUILayout.Height(48)))
            {
                var window = EditorWindow.GetWindow<SceneExplainerWindow>(true, "About " + gameObject.scene.name, true);
                window.Initialize(Text);
            }
#if UNITY_6000_0_OR_NEWER
            GUILayout.FlexibleSpace();//unity 6 already has another menu in the bottom right by default
#endif

            GUILayout.Space(5);
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
            Handles.EndGUI();
        }
    }
}
#endif