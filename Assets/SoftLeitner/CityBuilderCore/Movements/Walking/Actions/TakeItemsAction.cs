using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// action that removes items from a building and adds them to the walker<br/>
    /// ends immediately so only really makes sense as part of a larger process
    /// </summary>
    [Serializable]
    public class TakeItemsAction : WalkerAction, ISerializationCallbackReceiver
    {
        private ItemQuantity[] _items;
        [SerializeField]
        private ItemQuantity.ItemQuantityData[] _itemsData;

        private BuildingReference _buildingReference;
        [SerializeField]
        private string _buildingId;

        [SerializeField]
        private bool _unreserve;

        public TakeItemsAction()
        {

        }
        public TakeItemsAction(BuildingReference building, IEnumerable<ItemQuantity> items, bool unreserve = false)
        {
            _buildingReference = building;
            _items = items.ToArray();
            _unreserve = unreserve;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            var storage = (ItemStorage)_buildingReference.Instance.GetBuildingParts<IItemOwner>().First(o => o.ItemContainer is ItemStorage)?.ItemContainer;

            foreach (var item in _items)
            {
                if (_unreserve)
                    storage.UnreserveQuantity(item.Item, item.Quantity);
                storage.MoveItemsTo(walker.ItemStorage, item.Item, item.Quantity);
            }

            walker.AdvanceProcess();
        }

        public void OnBeforeSerialize()
        {
            _buildingId = _buildingReference.Id.ToString();
            _itemsData = _items.Select(i => i.GetData()).ToArray();
        }
        public void OnAfterDeserialize()
        {
            _buildingReference = Dependencies.Get<IBuildingManager>().GetBuildingReference(new Guid(_buildingId));
            _items = _itemsData.Select(i => i.GetItemQuantity()).ToArray();
        }
    }
}
