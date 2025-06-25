using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(ItemStore), true)]
    [CanEditMultipleObjects]
    public class ItemStoreEditor : BuildingComponentEditor
    {
        private Item _item;
        private int _quantity = 1;

        protected override void drawDebugGUI()
        {
            var store = (ItemStore)target;

            EditorGUILayout.LabelField("Items:");

            EditorGUILayout.BeginHorizontal();
            _item = (Item)EditorGUILayout.ObjectField(_item, typeof(Item), false);
            _quantity = EditorGUILayout.IntField(_quantity);
            if (GUILayout.Button("+") && _item != null)
                store.Storage.AddItems(new ItemQuantity(_item, _quantity));
            if (GUILayout.Button("-") && _item != null)
                store.Storage.RemoveItems(new ItemQuantity(_item, _quantity));
            EditorGUILayout.EndHorizontal();

            foreach (var itemQuantity in store.Storage.GetItemQuantities())
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(itemQuantity.Item.Name);
                EditorGUILayout.LabelField(itemQuantity.Quantity.ToString());
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}