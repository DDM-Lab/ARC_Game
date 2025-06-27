#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomPropertyDrawer(typeof(StructureLevelMask))]
    public class StructureLevelMaskDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var valueProperty = property.FindPropertyRelative("Value");
            var value = valueProperty.intValue;

            EditorGUI.BeginChangeCheck();

            label = EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            
            EditorGUI.indentLevel = 0;

            var left = position.position;
            var height = position.size.y;

            var all = EditorGUI.ToggleLeft(new Rect(left, new Vector2(50, height)), "All", value == 0);
            if (value != 0 && all)
                value = 0;

            left += new Vector2(50, 0);

            int pow = 1;
            for (int i = 0; i < DefaultStructureManager.LEVEL_COUNT; i++)
            {
                var flagValue = (value & pow) == pow;
                var flagResult = EditorGUI.Toggle(new Rect(left, new Vector2(15, height)), flagValue);
                if (flagValue != flagResult)
                {
                    if (flagResult)
                        value |= pow;
                    else
                        value &= ~pow;
                }

                left += new Vector2(15, 0);
                pow *= 2;
            }

            if (EditorGUI.EndChangeCheck())
                valueProperty.intValue = value;

            EditorGUI.EndProperty();
        }
    }
}
#endif