using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walks to an <see cref="IItemGiver"/> and gives items to the walker
    /// </summary>
    [Serializable]
    public class ItemsGiverAction : WalkerAction, ISerializationCallbackReceiver
    {
        private ItemQuantity _items;
        [SerializeField]
        private ItemQuantity.ItemQuantityData _itemsData;

        private BuildingComponentReference<IItemGiver> _itemGiver;
        [SerializeField]
        private BuildingComponentReferenceData _itemGiverData;

        private WalkingPath _walkingPath;
        [SerializeField]
        private WalkingPath.WalkingPathData _walkingPathData;

        [SerializeField]
        private bool _reserve;

        public ItemsGiverAction()
        {

        }
        public ItemsGiverAction(Walker walker, ItemQuantity items, float maxDistance, bool reserve)
        {
            var giverPath = Dependencies.Get<IGiverPathfinder>().GetGiverPath(walker.Home?.Instance, walker.CurrentPoint, items, maxDistance, walker.PathType, walker.PathTag);

            if (giverPath == null)
                return;

            _items = items;
            _itemGiver = giverPath.Component;
            _walkingPath = giverPath.Path;
            _reserve = reserve;
        }
        public ItemsGiverAction(ItemQuantity items, IItemGiver itemGiver, WalkingPath walkingPath, bool reserve)
        {
            _items = items;
            _itemGiver = itemGiver.Reference;
            _walkingPath = walkingPath;
            _reserve = reserve;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            if (_itemGiver == null || !_itemGiver.HasInstance)
                walker.AdvanceProcess();

            if (_reserve)
                _itemGiver.Instance.ReserveQuantity(_items.Item, _items.Quantity);

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

            if (_itemGiver.HasInstance && _reserve)
                _itemGiver.Instance.UnreserveQuantity(_items.Item, _items.Quantity);
            walker.CancelWalk();
        }
        public override void End(Walker walker)
        {
            base.End(walker);

            if (_itemGiver.HasInstance)
            {
                if (_reserve)
                    _itemGiver.Instance.UnreserveQuantity(_items.Item, _items.Quantity);
                _itemGiver.Instance.Give(walker.ItemStorage, _items.Item, _items.Quantity);
            }
        }

        public void OnBeforeSerialize()
        {
            _itemsData = _items.GetData();
            _itemGiverData = _itemGiver.GetData();
            _walkingPathData = _walkingPath.GetData();
        }
        public void OnAfterDeserialize()
        {
            _items = _itemsData.GetItemQuantity();
            _itemGiver = _itemGiverData.GetReference<IItemGiver>();
            _walkingPath = _walkingPathData.GetPath();
        }
    }
}