using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walks to an <see cref="IItemReceiver"/> and lets the receiver get items from the walker
    /// </summary>
    [Serializable]
    public class ItemsReceiverAction : WalkerAction, ISerializationCallbackReceiver
    {
        private ItemQuantity _items;
        [SerializeField]
        private ItemQuantity.ItemQuantityData _itemsData;

        private BuildingComponentReference<IItemReceiver> _itemReceiver;
        [SerializeField]
        private BuildingComponentReferenceData _itemReceiverData;

        private WalkingPath _walkingPath;
        [SerializeField]
        private WalkingPath.WalkingPathData _walkingPathData;

        [SerializeField]
        private bool _reserve;

        public ItemsReceiverAction()
        {

        }
        public ItemsReceiverAction(Walker walker, ItemQuantity items, float maxDistance, bool reserve)
        {
            var ReceiverPath = Dependencies.Get<IReceiverPathfinder>().GetReceiverPath(walker.Home?.Instance, walker.CurrentPoint, items, maxDistance, walker.PathType, walker.PathTag);

            if (ReceiverPath == null)
                return;

            _items = items;
            _itemReceiver = ReceiverPath.Component;
            _walkingPath = ReceiverPath.Path;
            _reserve = reserve;
        }
        public ItemsReceiverAction(ItemQuantity items, IItemReceiver itemReceiver, WalkingPath walkingPath, bool reserve)
        {
            _items = items;
            _itemReceiver = itemReceiver.Reference;
            _walkingPath = walkingPath;
            _reserve = reserve;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            if (_itemReceiver == null || !_itemReceiver.HasInstance)
                walker.AdvanceProcess();

            if (_reserve)
                _itemReceiver.Instance.ReserveCapacity(_items.Item, _items.Quantity);

            walker.Walk(_walkingPath, walker.AdvanceProcess);
        }
        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            walker.ContinueWalk(walker.AdvanceProcess);
        }
        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            if (_itemReceiver.HasInstance && _reserve)
                _itemReceiver.Instance.UnreserveCapacity(_items.Item, _items.Quantity);
            walker.CancelWalk();
        }
        public override void End(Walker walker)
        {
            base.End(walker);

            if (_itemReceiver.HasInstance)
            {
                if (_reserve)
                    _itemReceiver.Instance.UnreserveCapacity(_items.Item, _items.Quantity);
                _itemReceiver.Instance.Receive(walker.ItemStorage, _items.Item, _items.Quantity);
            }
        }

        public void OnBeforeSerialize()
        {
            _itemsData = _items.GetData();
            _itemReceiverData = _itemReceiver.GetData();
            _walkingPathData = _walkingPath.GetData();
        }
        public void OnAfterDeserialize()
        {
            _items = _itemsData.GetItemQuantity();
            _itemReceiver = _itemReceiverData.GetReference<IItemReceiver>();
            _walkingPath = _walkingPathData.GetPath();
        }
    }
}
