using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// action that lets a receiver get items from the walker and immediately ends<br/>
    /// only really makes sense as part of a larger process(walk>give>walk>receive)
    /// </summary>
    [Serializable]
    public class ReceiveItemsAction : WalkerAction, ISerializationCallbackReceiver
    {
        private ItemQuantity _items;
        [SerializeField]
        private ItemQuantity.ItemQuantityData _itemsData;

        private BuildingComponentReference<IItemReceiver> _itemReceiver;
        [SerializeField]
        private BuildingComponentReferenceData _itemReceiverData;

        [SerializeField]
        private bool _unreserve;

        public ReceiveItemsAction()
        {

        }
        public ReceiveItemsAction(IItemReceiver Receiver, ItemQuantity items, bool unreserve = false)
        {
            _itemReceiver = Receiver.Reference;
            _items = items;
            _unreserve = unreserve;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            if (_unreserve)
                _itemReceiver.Instance.UnreserveCapacity(_items.Item, _items.Quantity);
            _itemReceiver.Instance.Receive(walker.ItemStorage, _items.Item, _items.Quantity);

            walker.AdvanceProcess();
        }

        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            //should never happen because the action is active as it takes no time
            //cancel is only implemented here for convenience in task cancel(TownDeliverTask)
            if (_unreserve)
                _itemReceiver.Instance.UnreserveCapacity(_items.Item, _items.Quantity);
        }

        public void OnBeforeSerialize()
        {
            _itemsData = _items.GetData();
            _itemReceiverData = _itemReceiver.GetData();
        }
        public void OnAfterDeserialize()
        {
            _items = _itemsData.GetItemQuantity();
            _itemReceiver = _itemReceiverData.GetReference<IItemReceiver>();
        }
    }
}
