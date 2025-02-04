using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes a the total quantity and capacity of an <see cref="ItemStorage"/><br/>
    /// if the storage is stacked it shows the taken vs free stack count
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_storage_capacity_panel.html")]
    public class StorageCapacityPanel : MonoBehaviour
    {
        [Tooltip("fills up as quantity increases and remaining capacity gets lower")]
        public Slider Slider;
        [Tooltip("text of total capacity number(eg 50/100)")]
        public TMPro.TMP_Text Text;

        public void Set(ItemStorage storage)
        {
            gameObject.SetActive(storage != null);
            if (storage == null)
                return;

            int capacity, quantity;

            if (storage.IsStackedStorage)
            {
                capacity = storage.Stacks.Length;
                quantity = storage.Stacks.Count(s => s.HasItems);
            }
            else
            {
                capacity = storage.GetItemCapacity();
                quantity = storage.GetItemQuantity();
            }

            if (Slider)
            {
                Slider.minValue = 0;
                Slider.maxValue = capacity;
                Slider.value = quantity;
            }

            if (Text)
            {
                Text.text = $"{quantity}/{capacity}";
            }
        }
    }
}