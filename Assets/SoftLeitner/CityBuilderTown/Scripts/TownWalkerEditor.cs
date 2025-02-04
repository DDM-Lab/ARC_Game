#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderTown
{
    [CustomEditor(typeof(TownWalker))]
    public class TownWalkerEditor : Editor
    {
        private bool _foldout;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (EditorApplication.isPlaying)
            {
                _foldout = EditorGUILayout.Foldout(_foldout, "DEBUG", true);
                if (_foldout)
                {
                    var walker = (TownWalker)target;

                    EditorGUILayout.Space();

                    drawLabelColumns("Job", walker.Job?.Name ?? "-", "Process", walker.CurrentProcess?.Key ?? "-");
                    drawLabelColumns("Task", walker.CurrentTask?.name ?? "-");
                    drawLabelColumns("Age", walker.Age.ToString(), "Energy", walker.Energy.ToString());
                    drawLabelColumns("Food", walker.Food.ToString(), "Warmth", walker.Warmth.ToString());

                    EditorGUILayout.Space();

                    drawLabelColumns("Name", walker.Identity.FullName, "Life", walker.Identity.Lifespan.ToString());
                    drawLabelColumns("Height", walker.Identity.Heigth.ToString(), "Width", walker.Identity.Width.ToString());
                    drawLabelColumns("EnergyCap", walker.Identity.EnergyCapacity.ToString(), "EnergyRec", walker.Identity.EnergyRecovery.ToString());
                    drawLabelColumns("FoodCap", walker.Identity.FoodCapacity.ToString(), "FoodLos", walker.Identity.FoodLoss.ToString());
                    drawLabelColumns("WarmthCap", walker.Identity.WarmthCapacity.ToString(), "WarmthLos", walker.Identity.WarmthLoss.ToString());

                    EditorGUILayout.Space();

                    if (GUILayout.Button("Kill"))
                        (walker).Finish();
                }
            }
        }

        private void drawLabelColumns(params string[] texts)
        {
            var width = (Screen.width - 50) / texts.Length;

            EditorGUILayout.BeginHorizontal();
            foreach (var text in texts)
            {
                EditorGUILayout.LabelField(text, GUILayout.Width(width));
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif