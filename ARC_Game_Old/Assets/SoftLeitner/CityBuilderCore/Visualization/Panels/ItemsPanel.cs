using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// container in unity ui that generates <see cref="ItemPanel"/> for visualizing stored item quantities/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_items_panel.html")]
    public class ItemsPanel : MonoBehaviour
    {
        [Tooltip("will be instantiated for every item as a child of the panel")]
        public ItemPanel ItemPrefab;

        private List<ItemPanel> _panels = new List<ItemPanel>();

        public void SetItems(IItemContainer itemContainer) => SetItems(itemContainer.GetItemQuantities().ToList());
        public void SetItems(ItemStorage storage) => SetItems(storage.GetItems().ToList(), storage);
        public void SetItems(List<Item> items, ItemStorage storage) => SetItems(items.Select(i => Tuple.Create(i, storage)).ToList());
        public void SetItems(List<Tuple<Item, ItemStorage>> items)
        {
            for (int i = 0; i < Math.Max(_panels.Count, items.Count); i++)
            {
                var panel = _panels.ElementAtOrDefault(i);
                var item = items.ElementAtOrDefault(i);

                if (item == null)
                {
                    _panels.Remove(panel);
                    Destroy(panel.gameObject);
                    continue;
                }

                if (panel == null)
                {
                    panel = Instantiate(ItemPrefab, transform);
                    panel.gameObject.SetActive(true);
                    _panels.Add(panel);
                }

                panel.SetItem(item.Item1, item.Item2);
            }
        }
        public void SetItems(List<ItemQuantity> items)
        {
            for (int i = 0; i < Math.Max(_panels.Count, items.Count); i++)
            {
                var panel = _panels.ElementAtOrDefault(i);
                var item = items.ElementAtOrDefault(i);

                if (item == null)
                {
                    _panels.Remove(panel);
                    Destroy(panel.gameObject);
                    continue;
                }

                if (panel == null)
                {
                    panel = Instantiate(ItemPrefab, transform);
                    _panels.Add(panel);
                }

                panel.SetItem(item);
            }
        }
        public void SetItems(List<ItemLevel> levels)
        {
            for (int i = 0; i < Math.Max(_panels.Count, levels.Count); i++)
            {
                var panel = _panels.ElementAtOrDefault(i);
                var level = levels.ElementAtOrDefault(i);

                if (level == null)
                {
                    _panels.Remove(panel);
                    Destroy(panel.gameObject);
                    continue;
                }

                if (panel == null)
                {
                    panel = Instantiate(ItemPrefab, transform);
                    panel.gameObject.SetActive(true);
                    _panels.Add(panel);
                }

                panel.SetItem(level);
            }
        }

        public void Clear()
        {
            _panels.ForEach(p => Destroy(p.gameObject));
            _panels.Clear();
        }
    }
}