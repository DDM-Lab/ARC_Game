using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// generates visuals for the items stored in a stacked <see cref="IStorageComponent"/><br/>
    /// the visuals defined in <see cref="Item"/> are used
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_storage_quantity_visualizer.html")]
    [RequireComponent(typeof(IStorageComponent))]
    public class StorageQuantityVisualizer : MonoBehaviour
    {
        private class StorageQuantityItem
        {
            public Transform Origin;
            public StorageQuantityVisual Visual;
        }

        [Tooltip("define one transform for every stack in the storage")]
        public Transform[] Origins;
        [Tooltip("items can have multiple visuals for different storages")]
        public int VisualIndex;

        private Dictionary<ItemStack, StorageQuantityItem> _items = new Dictionary<ItemStack, StorageQuantityItem>();

        private void Start()
        {
            var storage = GetComponent<IStorageComponent>().Storage.GetActualStorage();
            for (int i = 0; i < storage.Stacks.Length; i++)
            {
                var stack = storage.Stacks[i];
                _items.Add(stack, new StorageQuantityItem() { Origin = Origins[i] });
                stack.Changed += visualize;
                visualize(stack);
            }
        }

        private void visualize(ItemStack stack)
        {
            var item = _items[stack];

            if (stack.HasItems)
            {
                if (item.Visual == null)
                {
                    var visual = stack.Items.Item.Visuals.ElementAtOrDefault(VisualIndex);
                    if (visual != null)
                    {
                        item.Visual = Instantiate(visual, item.Origin);
                    }
                }

                item.Visual.SetQuantity((int)stack.Items.UnitQuantity);
            }
            else
            {
                if (item.Visual)
                {
                    Destroy(item.Visual.gameObject);
                    item.Visual = null;
                }
            }
        }
    }
}