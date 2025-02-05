using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// unity ui panel for visualizing and editing a <see cref="DistributionOrder"/><br/>
    /// only works in combination with the container <see cref="DistributionOrdersPanel"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_distribution_order_panel.html")]
    public class DistributionOrderPanel : MonoBehaviour
    {
        [Tooltip("image will display the icon of the item")]
        public Image Image;
        [Tooltip("item name")]
        public TMPro.TMP_Text Name;
        [Tooltip("order ratio from 0 to 1")]
        public TMPro.TMP_InputField Ratio;
        [Tooltip("current items stored(eg 50/100)")]
        public TMPro.TMP_Text Numbers;

        private DistributionOrder _order;

        public void SetOrder(DistributionOrder order, ItemStorage storage, bool initiate)
        {
            _order = order;

            string capacityText;
            int capacity = storage.GetItemCapacity(order.Item);
            if (capacity > 0 && capacity < int.MaxValue)
                capacityText = $"/{storage.GetItemCapacity(order.Item, order.Ratio)}";
            else
                capacityText = string.Empty;

            Image.sprite = order.Item.Icon;
            Name.text = order.Item.Name;
            Numbers.text = storage.GetItemQuantity(order.Item) + capacityText;

            if (initiate)
            {
                Ratio.SetTextWithoutNotify(order.Ratio.ToString("F2"));
            }
        }

        public void RatioChanged(string text)
        {
            if (float.TryParse(text, out float ratio))
            {
                _order.Ratio = ratio;
            }
        }
    }
}