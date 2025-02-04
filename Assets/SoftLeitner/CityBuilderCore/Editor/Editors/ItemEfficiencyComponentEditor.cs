using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(ItemEfficiencyComponent), true)]
    [CanEditMultipleObjects]
    public class ItemEfficiencyComponentEditor : BuildingComponentEditor
    {
        protected override void drawDebugGUI()
        {
            base.drawDebugGUI();

            var component = (ItemEfficiencyComponent)target;

            if (component.Recipients.Length > 0)
            {
                EditorGUILayout.LabelField("Items:");
                foreach (var recipient in component.Recipients)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{recipient.Item.Name}({recipient.Quantity})");
                    if (GUILayout.Button("+"))
                        recipient.Modify(1);
                    if (GUILayout.Button("-"))
                        recipient.Modify(-1);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }
}