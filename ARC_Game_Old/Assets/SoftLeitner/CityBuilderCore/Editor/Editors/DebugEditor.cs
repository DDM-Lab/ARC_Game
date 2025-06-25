using UnityEditor;

namespace CityBuilderCore.Editor
{
    public abstract class DebugEditor : UnityEditor.Editor
    {
        private bool _foldout;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (EditorApplication.isPlaying)
            {
                _foldout = EditorGUILayout.Foldout(_foldout, "DEBUG", true);
                if (_foldout)
                    drawDebugGUI();
            }
            else
            {
                EditorGUILayout.LabelField("...");
            }
        }

        protected abstract void drawDebugGUI();
    }
}