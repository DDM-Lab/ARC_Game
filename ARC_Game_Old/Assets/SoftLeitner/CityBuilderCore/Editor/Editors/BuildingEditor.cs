using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(Building), true)]
    [CanEditMultipleObjects]
    public class EffectPoolEditor : DebugEditor
    {
        private BuildingAddon _addon;

        protected override void drawDebugGUI()
        {
            var building = (Building)target;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Point:");
            EditorGUILayout.LabelField(building.Point.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Size:");
            EditorGUILayout.LabelField(building.Size.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Rotation:");
            EditorGUILayout.LabelField(building.Rotation.State.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("IsWorking:");
            EditorGUILayout.LabelField(building.IsWorking.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Efficiency:");
            EditorGUILayout.LabelField(building.Efficiency.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Components:");
            foreach (var component in building.Components)
            {
                string text = component.GetType().Name;
                if (component.GetDebugText() != null)
                    text += " (" + component.GetDebugText() + ")";
                EditorGUILayout.LabelField(text);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Addons:");
            foreach (var addon in building.Addons.ToList())
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(addon.GetType().Name);
                if (GUILayout.Button("-"))
                    building.RemoveAddon(addon);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            _addon = (BuildingAddon)EditorGUILayout.ObjectField(_addon, typeof(BuildingAddon), false);
            if (GUILayout.Button("+") && _addon)
                building.AddAddon(_addon);
            EditorGUILayout.EndHorizontal();
        }
    }
}