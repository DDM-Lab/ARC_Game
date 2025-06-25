using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// action that gives items from a giver to the walker and immediately ends<br/>
    /// only really makes sense as part of a larger process(walk>give>walk>receive)
    /// </summary>
    [Serializable]
    public class GiveItemsAction : WalkerAction, ISerializationCallbackReceiver
    {
        private ItemQuantity _items;
        [SerializeField]
        private ItemQuantity.ItemQuantityData _itemsData;

        private BuildingComponentReference<IItemGiver> _itemGiver;
        [SerializeField]
        private BuildingComponentReferenceData _itemGiverData;

        [SerializeField]
        private bool _unreserve;

        public GiveItemsAction()
        {

        }
        public GiveItemsAction(IItemGiver giver, ItemQuantity items, bool unreserve = false)
        {
            _itemGiver = giver.Reference;
            _items = items;
            _unreserve = unreserve;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            if (_unreserve)
                _itemGiver.Instance.UnreserveQuantity(_items.Item, _items.Quantity);
            _itemGiver.Instance.Give(walker.ItemStorage, _items.Item, _items.Quantity);

            walker.AdvanceProcess();
        }

        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            //should never happen because the action is active as it takes no time
            //cancel is only implemented here for convenience in task cancel(TownDeliverTask)
            if (_unreserve)
                _itemGiver.Instance.UnreserveQuantity(_items.Item, _items.Quantity);
        }

        public void OnBeforeSerialize()
        {
            _itemsData = _items.GetData();
            _itemGiverData = _itemGiver.GetData();
        }
        public void OnAfterDeserialize()
        {
            _items = _itemsData.GetItemQuantity();
            _itemGiver = _itemGiverData.GetReference<IItemGiver>();
        }
    }
}
