using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomPropertyDrawer(typeof(LayerRequirement))]
    public class LayerRequirementDrawer : PropertyDrawer
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

            var layer = property.FindPropertyRelative("Layer");

            if (layer.objectReferenceValue == null)
            {
                EditorGUI.PropertyField(new Rect(position.x, position.y, position.width, position.height), layer, GUIContent.none);
            }
            else
            {
                EditorGUI.PropertyField(new Rect(position.x, position.y, position.width - 94, position.height), layer, GUIContent.none);

                var x = position.x + position.width - 94;
                EditorGUI.PropertyField(new Rect(x+2, position.y, 40, position.height), property.FindPropertyRelative("MinValue"), GUIContent.none);
                EditorGUI.LabelField(new Rect(x + 44, position.y, 10, position.height), "-");
                EditorGUI.PropertyField(new Rect(x + 54, position.y, 40, position.height), property.FindPropertyRelative("MaxValue"), GUIContent.none);
            }

            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }
    }
}
