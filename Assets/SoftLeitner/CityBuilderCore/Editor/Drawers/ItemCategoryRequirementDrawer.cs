using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomPropertyDrawer(typeof(ItemCategoryRequirement))]
    public class ItemCategoryRequirementDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            EditorGUI.PropertyField(new Rect(position.x, position.y, 50, position.height), property.FindPropertyRelative("Quantity"), GUIContent.none);
            EditorGUI.PropertyField(new Rect(position.x + 52, position.y, position.width - 52, position.height), property.FindPropertyRelative("ItemCategory"), GUIContent.none);

            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }
    }
}
