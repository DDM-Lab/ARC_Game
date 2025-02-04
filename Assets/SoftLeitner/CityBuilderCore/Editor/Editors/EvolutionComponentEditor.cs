using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(EvolutionComponent), true)]
    [CanEditMultipleObjects]
    public class EvolutionComponentEditor : BuildingComponentEditor
    {
        protected override void drawDebugGUI()
        {
            base.drawDebugGUI();

            var evolutionComponent = (EvolutionComponent)target;

            if (evolutionComponent.ServiceRecipients.Length > 0)
            {
                EditorGUILayout.LabelField("Services:");
                foreach (var recipient in evolutionComponent.ServiceRecipients)
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

            if (evolutionComponent.ServiceCategoryRecipients.Length > 0)
            {
                EditorGUILayout.LabelField("Service Categories:");
                foreach (var recipient in evolutionComponent.ServiceCategoryRecipients)
                {
                    EditorGUILayout.LabelField($"{recipient.ServiceCategory.NamePlural}");
                    EditorGUI.indentLevel++;
                    foreach (var service in recipient.ServiceCategory.Services)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{service.Name}({recipient.GetValue(service)})");
                        if (GUILayout.Button("+"))
                            recipient.Modify(service, 100);
                        if (GUILayout.Button("-"))
                            recipient.Modify(service, -100);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel++;
                }
            }

            if (evolutionComponent.ItemsRecipients.Length > 0)
            {
                EditorGUILayout.LabelField("Items:");
                foreach (var recipient in evolutionComponent.ItemsRecipients)
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

            if (evolutionComponent.ItemsCategoryRecipients.Length > 0)
            {
                EditorGUILayout.LabelField("Item Categories:");
                foreach (var recipient in evolutionComponent.ItemsCategoryRecipients)
                {
                    EditorGUILayout.LabelField($"{recipient.ItemCategory.NamePlural}");
                    EditorGUI.indentLevel++;
                    foreach (var item in recipient.ItemCategory.Items)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{item.Name}({recipient.Storage.GetItemQuantity(item)})");
                        if (GUILayout.Button("+"))
                            recipient.Modify(item, 1);
                        if (GUILayout.Button("-"))
                            recipient.Modify(item, -1);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel++;
                }
            }

        }
    }
}