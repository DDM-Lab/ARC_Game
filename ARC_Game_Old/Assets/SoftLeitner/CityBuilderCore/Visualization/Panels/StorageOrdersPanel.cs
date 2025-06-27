using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// container in unity ui that generates <see cref="StorageOrderPanel"/> for visualizing and editing <see cref="StorageOrder"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_storage_orders_panel.html")]
    public class StorageOrdersPanel : MonoBehaviour
    {
        [Tooltip("will be instantiated for every order as a child of the panel")]
        public StorageOrderPanel OrderPrefab;

        private List<StorageOrderPanel> _panels = new List<StorageOrderPanel>();

        public void SetOrders(IStorageComponent storageComponent, bool initiate) => SetOrders(storageComponent.Orders, storageComponent.Storage, initiate);
        public void SetOrders(StorageOrder[] orders, ItemStorage storage, bool initiate)
        {
            for (int i = 0; i < Math.Max(_panels.Count, orders.Length); i++)
            {
                var panel = _panels.ElementAtOrDefault(i);
                var order = orders.ElementAtOrDefault(i);

                if (order == null)
                {
                    _panels.Remove(panel);
                    Destroy(panel.gameObject);
                    continue;
                }

                if (panel == null)
                {
                    panel = Instantiate(OrderPrefab, transform);
                    _panels.Add(panel);
                }

                panel.SetOrder(order, storage, initiate);
            }
        }
    }
}