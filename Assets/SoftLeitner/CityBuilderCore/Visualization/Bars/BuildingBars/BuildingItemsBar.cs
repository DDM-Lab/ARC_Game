using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes item quantities by stacking its icon<br/>
    /// used for the food view in THREE
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_items_bar.html")]
    public class BuildingItemsBar : BuildingValueBar
    {
        [Tooltip("prefab for one icon instance")]
        public SpriteRenderer Prefab;
        [Tooltip("offset between icons")]
        public Vector3 Offset;

        private IMainCamera _mainCamera;
        private Item _item;
        private ItemSet _items;
        private ItemCategory _itemCategory;
        private List<SpriteRenderer> _sprites = new List<SpriteRenderer>();

        private void Start()
        {
            _mainCamera = Dependencies.Get<IMainCamera>();

            setBar();
        }

        private void Update()
        {
            setBar();
        }

        public override void Initialize(IBuilding building, IBuildingValue value)
        {
            base.Initialize(building, value);

            _item = value as Item;
            _items = value as ItemSet;
            _itemCategory = value as ItemCategory;
        }

        private void setBar()
        {
            transform.forward = _mainCamera.Camera.transform.forward;

            var startIndex = 0;

            if (_item != null)
            {
                startIndex = setItem(startIndex, _item);
            }
            else if (_items != null)
            {
                foreach (var item in _items.Objects)
                {
                    startIndex = setItem(startIndex, item);
                }
            }
            else if (_itemCategory != null)
            {
                foreach (var item in _itemCategory.Items)
                {
                    startIndex = setItem(startIndex, item);
                }
            }

            for (int i = _sprites.Count - 1; i >= startIndex; i--)
            {
                Destroy(_sprites[i].gameObject);
                _sprites.RemoveAt(i);
            }
        }

        private int setItem(int startIndex, Item item)
        {
            var maximum = item.GetMaximum(Building);
            var value = item.GetValue(Building);
            var scale = 1f;

            if (maximum >= item.UnitSize)
            {
                value /= item.UnitSize;
            }
            else
            {
                scale = 0.5f;
            }

            var count = Mathf.RoundToInt(value);

            for (int i = 0; i < count; i++)
            {
                var sprite = _sprites.ElementAtOrDefault(i + startIndex);
                if (sprite == null)
                {
                    sprite = Instantiate(Prefab, transform);
                    _sprites.Add(sprite);
                }

                sprite.sprite = item.Icon;
                sprite.transform.localPosition = Offset * (i + startIndex) * scale;
                sprite.transform.localScale = Vector3.one * scale;
            }

            return startIndex + count;
        }
    }
}