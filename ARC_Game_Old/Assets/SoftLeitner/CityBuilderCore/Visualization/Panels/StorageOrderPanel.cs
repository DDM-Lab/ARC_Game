using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// unity ui panel for visualizing and editing a <see cref="StorageOrder"/><br/>
    /// only works in combination with the container <see cref="StorageOrdersPanel"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_storage_order_panel.html")]
    public class StorageOrderPanel : MonoBehaviour
    {
        [Tooltip("displays the icon of the order item")]
        public Image Image;
        [Tooltip("displays the name of the order item")]
        public TMPro.TMP_Text Name;
        [Tooltip("used to change the mode of the order, ModeChanged has to be wired up in the inspector")]
        public TMPro.TMP_Dropdown Mode;
        [Tooltip("input for the order ration, RatioChanged has to be wired up in the inspector")]
        public TMPro.TMP_InputField Ratio;
        [Tooltip("text field for displaying the current item quantity and capacity in the storage")]
        public TMPro.TMP_Text Numbers;

        private StorageOrder _order;

        public void SetOrder(StorageOrder order, ItemStorage storage, bool initiate)
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
                Mode.SetValueWithoutNotify(toIndex(order.Mode));
                Ratio.SetTextWithoutNotify(order.Ratio.ToString("F2"));
            }
        }

        public void ModeChanged(int index)
        {
            _order.Mode = toMode(index);
        }

        public void RatioChanged(string text)
        {
            if (float.TryParse(text, out float ratio))
            {
                _order.Ratio = ratio;
            }
        }

        private StorageOrderMode toMode(int index)
        {
            switch (index)
            {
                case 0:
                    return StorageOrderMode.Neutral;
                case 1:
                    return StorageOrderMode.Get;
                case 2:
                    return StorageOrderMode.Empty;
                default:
                    return StorageOrderMode.Neutral;
            }
        }

        private int toIndex(StorageOrderMode mode)
        {
            switch (mode)
            {
                case StorageOrderMode.Neutral:
                    return 0;
                case StorageOrderMode.Get:
                    return 1;
                case StorageOrderMode.Empty:
                    return 2;
                default:
                    return 0;
            }
        }
    }
}