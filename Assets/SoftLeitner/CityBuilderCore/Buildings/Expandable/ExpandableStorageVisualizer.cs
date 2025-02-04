using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// generates visuals for the items stored in a stacked <see cref="IStorageComponent"/><br/>
    /// the visuals defined in <see cref="Item"/> are used
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_expandable_storage_visualizer.html")]
    [RequireComponent(typeof(IStorageComponent))]
    [RequireComponent(typeof(ExpandableVisual))]
    public class ExpandableStorageVisualizer : MonoBehaviour
    {
        private class StorageQuantityItem
        {
            public Transform Origin;
            public StorageQuantityVisual Visual;
        }

        [Tooltip("items can have multiple visuals for different storages")]
        public int VisualIndex;

        private ExpandableVisual _expandableVisual;

        private Dictionary<ItemStack, StorageQuantityItem> _items = new Dictionary<ItemStack, StorageQuantityItem>();

        private void Awake()
        {
            _expandableVisual = GetComponent<ExpandableVisual>();

            _expandableVisual.VisualsUpdated += expandableVisualsUpdated;
        }

        private void expandableVisualsUpdated()
        {
            var storage = GetComponent<IStorageComponent>().Storage.GetActualStorage();
            for (int i = 0; i < storage.Stacks.Length; i++)
            {
                var stack = storage.Stacks[i];
                var part = _expandableVisual.RepeatedParts.ElementAtOrDefault(i);
                _items.Add(stack, new StorageQuantityItem() { Origin = part });
                stack.Changed += visualize;
                visualize(stack);
            }

            //expandables with attached storage should not change size
            //changing expansion size is mostly for building ghosts
            _expandableVisual.VisualsUpdated -= expandableVisualsUpdated;
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