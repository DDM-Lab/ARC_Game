using UnityEditor;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(BuildingComponent), true)]
    [CanEditMultipleObjects]
    public class BuildingComponentEditor : DebugEditor
    {
        protected override void drawDebugGUI()
        {
            var buildingComponent = (BuildingComponent)target;

            if (buildingComponent is IEfficiencyFactor efficiency)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"IsWorking:{efficiency.IsWorking}");
                EditorGUILayout.LabelField($"Factor:{efficiency.Factor}");
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();
            }

            if (!string.IsNullOrWhiteSpace(buildingComponent.GetDescription()))
                EditorGUILayout.LabelField(buildingComponent.GetDescription());
            if (!string.IsNullOrWhiteSpace(buildingComponent.GetDebugText()))
                EditorGUILayout.LabelField(buildingComponent.GetDebugText());
        }
    }
}