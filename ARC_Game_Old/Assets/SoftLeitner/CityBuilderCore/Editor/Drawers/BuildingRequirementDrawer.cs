using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomPropertyDrawer(typeof(BuildingRequirement))]
    public class BuildingRequirementDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var height = EditorGUIUtility.singleLineHeight;

            var modeProperty = property.FindPropertyRelative("Mode");
            var mode = (BuildingRequirementMode)modeProperty.enumValueIndex;

            if (mode == BuildingRequirementMode.Specific || mode == BuildingRequirementMode.AnySpecific)
            {
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Points"), true);
            }

            if (mode == BuildingRequirementMode.Any || mode == BuildingRequirementMode.AnySpecific)
            {
                height += EditorGUIUtility.singleLineHeight;
            }

            height += EditorGUIUtility.singleLineHeight;
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("GroundOptions"), true);

            var building = property.FindPropertyRelative("Building");
            height += EditorGUIUtility.singleLineHeight;

            if (building.objectReferenceValue != null)
            {
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("BuildingPoints"), true);
            }

            return height + EditorGUIUtility.standardVerticalSpacing * 7;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            position = addHeight(position, EditorGUIUtility.standardVerticalSpacing);

            var modeProperty = property.FindPropertyRelative("Mode");
            var mode = (BuildingRequirementMode)modeProperty.enumValueIndex;

            position = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(position, modeProperty);
            EditorGUI.indentLevel++;

            position = addLine(position);

            if (mode == BuildingRequirementMode.Specific || mode == BuildingRequirementMode.AnySpecific)
            {
                var points = property.FindPropertyRelative("Points");
                EditorGUI.PropertyField(position, points);
                position = addHeight(position, EditorGUI.GetPropertyHeight(points, true));
            }

            if (mode == BuildingRequirementMode.Any || mode == BuildingRequirementMode.AnySpecific)
            {
                var count = property.FindPropertyRelative("Count");
                EditorGUI.PropertyField(position, count, count.intValue == 0 ? new GUIContent("Count(!!!)") : new GUIContent("Count"));
                position = addLine(position);
            }

            position = addHeight(position, EditorGUIUtility.standardVerticalSpacing * 2);

            EditorGUI.indentLevel--;

            EditorGUI.PropertyField(position, property.FindPropertyRelative("LayerRequirement"));
            position = addLine(position);

            position = addHeight(position, EditorGUIUtility.standardVerticalSpacing);

            var groundOptions = property.FindPropertyRelative("GroundOptions");
            EditorGUI.indentLevel++;
            EditorGUI.PropertyField(position, groundOptions);
            EditorGUI.indentLevel--;
            position = addHeight(position, EditorGUI.GetPropertyHeight(groundOptions, true));

            position = addHeight(position, EditorGUIUtility.standardVerticalSpacing * 2);

            var building = property.FindPropertyRelative("Building");
            EditorGUI.PropertyField(position, building);
            position = addLine(position);

            if (building.objectReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("BuildingPoints"));
                EditorGUI.indentLevel--;
            }
        }

        private Rect addLine(Rect position)
        {
            return new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight, position.width, EditorGUIUtility.singleLineHeight);
        }

        private Rect addHeight(Rect position, float height)
        {
            return new Rect(position.x, position.y + height, position.width, EditorGUIUtility.singleLineHeight);
        }
    }
}
