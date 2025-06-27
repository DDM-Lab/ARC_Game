using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(ProductionComponent), true)]
    [CanEditMultipleObjects]
    public class ProductionComponentEditor : BuildingComponentEditor
    {
        protected override void drawDebugGUI()
        {
            var productionComponent = (ProductionComponent)target;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"State:{productionComponent.CurrentProductionState}");
            EditorGUILayout.LabelField($"Progress:{productionComponent.Progress}");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Consumers:");
            foreach (var consumer in productionComponent.Consumers)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{consumer.Items.Item.Name}({consumer.ItemLevel.Quantity}/{consumer.ItemLevel.Capacity})");
                if (GUILayout.Button("+"))
                    consumer.Storage.AddItems(consumer.Items); 
                if (GUILayout.Button("-"))
                    consumer.Storage.RemoveItems(consumer.Items);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField("Producers:");
            foreach (var producers in productionComponent.Producers)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{producers.Items.Item.Name}({producers.ItemLevel.Quantity}/{producers.ItemLevel.Capacity})");
                if (GUILayout.Button("+"))
                    producers.Storage.AddItems(producers.Items);
                if (GUILayout.Button("-"))
                    producers.Storage.RemoveItems(producers.Items);
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}