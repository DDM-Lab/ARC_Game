using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes a specific item and its stored quantity<br/>
    /// only works in combination with the container <see cref="ItemsPanel"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_item_panel.html")]
    public class ItemPanel : MonoBehaviour
    {
        [Tooltip("item icon")]
        public Image Image;
        [Tooltip("item name")]
        public TMPro.TMP_Text Name;
        [Tooltip("stored item quantity(eg 50/100)")]
        public TMPro.TMP_Text Numbers;

        public void SetItem(Item item, ItemStorage storage)
        {
            if (item == null)
            {
                gameObject.SetActive(false);
            }
            else
            {
                gameObject.SetActive(true);

                string capacityText;
                int capacity = storage.GetItemCapacity(item);
                if (capacity > 0 && capacity < int.MaxValue)
                    capacityText = $"/{storage.GetItemCapacity(item)}";
                else
                    capacityText = string.Empty;

                Image.sprite = item.Icon;
                Name.text = item.Name;
                Numbers.text = storage.GetItemQuantity(item) + capacityText;
            }
        }
        public void SetItem(ItemLevel level) => SetItem(level.Item, level.Quantity, level.Capacity);
        public void SetItem(Item item, int quantity, int capacity)
        {
            if (item == null)
            {
                gameObject.SetActive(false);
            }
            else
            {
                gameObject.SetActive(true);

                string capacityText;
                if (capacity > 0 && capacity < int.MaxValue)
                    capacityText = $"/{capacity}";
                else
                    capacityText = string.Empty;

                Image.sprite = item.Icon;
                Name.text = item.Name;
                Numbers.text = quantity + capacityText;
            }
        }
        public void SetItem(ItemQuantity items)
        {
            if (items == null)
            {
                gameObject.SetActive(false);
            }
            else
            {
                gameObject.SetActive(true);

                Image.sprite = items.Item.Icon;
                Name.text = items.Item.Name;
                Numbers.text = items.Quantity.ToString();
            }
        }
    }
}