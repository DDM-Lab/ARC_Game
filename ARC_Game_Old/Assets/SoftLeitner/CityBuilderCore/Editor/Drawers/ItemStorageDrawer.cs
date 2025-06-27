using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomPropertyDrawer(typeof(ItemStorage))]
    public class ItemStorageDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var height = EditorGUIUtility.singleLineHeight;

            var modeProperty = property.FindPropertyRelative("Mode");
            var mode = (ItemStorageMode)modeProperty.enumValueIndex;

            switch (mode)
            {
                case ItemStorageMode.Stacked:
                case ItemStorageMode.Store:
                    height += EditorGUIUtility.singleLineHeight;
                    height += EditorGUIUtility.singleLineHeight;
                    break;
                case ItemStorageMode.ItemSpecific:
                    height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("ItemCapacities"), true);
                    break;
                case ItemStorageMode.ItemCapped:
                case ItemStorageMode.UnitCapped:
                case ItemStorageMode.TotalItemCapped:
                case ItemStorageMode.TotalUnitCapped:
                    height += EditorGUIUtility.singleLineHeight;
                    break;
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var modeProperty = property.FindPropertyRelative("Mode");
            var mode = (ItemStorageMode)modeProperty.enumValueIndex;

            position = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(position, modeProperty, new GUIContent("Storage Mode"));
            EditorGUI.indentLevel++;

            switch (mode)
            {
                case ItemStorageMode.Stacked:
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("StackCount"));
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("Capacity"), new GUIContent("Units per Stack"));
                    break;
                case ItemStorageMode.ItemSpecific:
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("ItemCapacities"));
                    break;
                case ItemStorageMode.Store:
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("Store"));
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("PersistStore"));
                    break;
                case ItemStorageMode.ItemCapped:
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("Capacity"));
                    break;
                case ItemStorageMode.UnitCapped:
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("Capacity"));
                    break;
                case ItemStorageMode.TotalItemCapped:
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("Capacity"));
                    break;
                case ItemStorageMode.TotalUnitCapped:
                    position = addLine(position);
                    EditorGUI.PropertyField(position, property.FindPropertyRelative("Capacity"));
                    break;
            }
            EditorGUI.indentLevel--;
        }

        private Rect addLine(Rect position)
        {
            return new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight, position.width, EditorGUIUtility.singleLineHeight);
        }
    }
}
