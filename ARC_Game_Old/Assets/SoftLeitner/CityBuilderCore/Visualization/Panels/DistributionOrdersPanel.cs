using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// container in unity ui that generates <see cref="DistributionOrderPanel"/> for visualizing and editing <see cref="DistributionOrder"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_distribution_orders_panel.html")]
    public class DistributionOrdersPanel : MonoBehaviour
    {
        [Tooltip("prefab that will be instantiated for every order as a child of the panel")]
        public DistributionOrderPanel OrderPrefab;

        private List<DistributionOrderPanel> _panels = new List<DistributionOrderPanel>();

        public void SetOrders(IDistributionComponent distributionComponent, bool initiate) => SetOrders(distributionComponent.Orders, distributionComponent.Storage, initiate);
        public void SetOrders(DistributionOrder[] orders, ItemStorage storage, bool initiate)
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