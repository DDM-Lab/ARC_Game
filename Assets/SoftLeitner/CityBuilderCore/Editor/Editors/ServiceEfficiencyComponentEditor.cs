using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(ServiceEfficiencyComponent), true)]
    [CanEditMultipleObjects]
    public class ServiceEfficiencyComponentEditor : BuildingComponentEditor
    {
        protected override void drawDebugGUI()
        {
            base.drawDebugGUI();

            var component = (ServiceEfficiencyComponent)target;

            if (component.ServiceRecipients.Length > 0)
            {
                EditorGUILayout.LabelField("Services:");
                foreach (var recipient in component.ServiceRecipients)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{recipient.Service.Name}({recipient.Value})");
                    if (GUILayout.Button("+"))
                        recipient.Modify(100);
                    if (GUILayout.Button("-"))
                        recipient.Modify(-100);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }
}