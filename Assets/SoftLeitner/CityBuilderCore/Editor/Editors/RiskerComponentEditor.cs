using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(RiskerComponent), true)]
    [CanEditMultipleObjects]
    public class RiskerComponentEditor : BuildingComponentEditor
    {
        protected override void drawDebugGUI()
        {
            var riskerComponent = (RiskerComponent)target;

            foreach (var recipient in riskerComponent.RiskRecipients)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{recipient.Risk.Name}({recipient.Value})");
                if (recipient.HasTriggered)
                {
                    if (GUILayout.Button("Resolve"))
                        recipient.Modify(-100);
                }
                else
                {
                    if (GUILayout.Button("Trigger"))
                        recipient.Modify(100);
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}