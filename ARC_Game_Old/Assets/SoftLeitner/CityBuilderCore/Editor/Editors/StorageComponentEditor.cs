using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(StorageComponent), true)]
    [CanEditMultipleObjects]
    public class StorageComponentEditor : BuildingComponentEditor
    {
        private Item _item;
        private int _quantity = 1;

        protected override void drawDebugGUI()
        {
            var storageComponent = (StorageComponent)target;

            EditorGUILayout.LabelField("Items:");

            EditorGUILayout.BeginHorizontal();
            _item = (Item)EditorGUILayout.ObjectField(_item, typeof(Item), false);
            _quantity = EditorGUILayout.IntField(_quantity);
            if (GUILayout.Button("+") && _item != null)
                storageComponent.Storage.AddItems(new ItemQuantity(_item, _quantity));
            if (GUILayout.Button("-") && _item != null)
                storageComponent.Storage.RemoveItems(new ItemQuantity(_item, _quantity));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quantities:");

            foreach (var itemQuantity in storageComponent.Storage.GetItemQuantities())
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(itemQuantity.Item.Name);
                EditorGUILayout.LabelField(itemQuantity.Quantity.ToString());
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Reservations:");

            foreach (var order in storageComponent.Orders)
            {
                var quantity = storageComponent.Storage.GetReservedQuantity(order.Item);
                var capacity = storageComponent.Storage.GetReservedCapacity(order.Item);

                if (quantity == 0 && capacity == 0)
                    continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(order.Item.Name);
                EditorGUILayout.LabelField("Q:" + quantity);
                EditorGUILayout.LabelField("C:" + capacity);
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}