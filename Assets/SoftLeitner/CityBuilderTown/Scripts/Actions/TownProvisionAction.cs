using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// action that provisions a <see cref="TownHomeComponent"/> with a type of item like food or wood<br/>
    /// which item that is, as well as the giver and the path to take, has to be provided<br/>
    /// the action reserves the items quantity in the giver and the needed capacity in the home
    /// </summary>
    [Serializable]
    public class TownProvisionAction : WalkerAction, ISerializationCallbackReceiver
    {
        private BuildingComponentReference<TownHomeComponent> _home;
        [SerializeField]
        private BuildingComponentReferenceData _homeData;

        private BuildingComponentReference<IItemGiver> _itemGiver;
        [SerializeField]
        private BuildingComponentReferenceData _itemGiverData;

        private WalkingPath _walkingPath;
        [SerializeField]
        private WalkingPath.WalkingPathData _walkingPathData;

        private ItemQuantity _items;
        [SerializeField]
        private ItemQuantity.ItemQuantityData _itemsData;

        [SerializeField]
        public bool _isHome;
        [SerializeField]
        public bool _isReturning;

        public TownProvisionAction()
        {

        }
        public TownProvisionAction(TownWalker walker, TownHomeComponent home, IItemGiver giver, WalkingPath walkingPath, Item item, bool isHome)
        {
            _home = home.Reference;
            _itemGiver = giver.Reference;
            _walkingPath = walkingPath;
            _items = new ItemQuantity(item, Mathf.Min(
                home.ItemStorage.GetItemCapacityRemaining(item),
                giver.GetGiveQuantity(item),
                walker.ItemStorage.GetItemCapacityRemaining(item)));
            _isHome = isHome;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            walker.Walk(_walkingPath, () => Arrive(walker));

            _itemGiver.Instance.ReserveQuantity(_items.Item, _items.Quantity);
            _home.Instance.ItemStorage.ReserveCapacity(_items.Item, _items.Quantity);
        }
        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            if (_isReturning)
                walker.ContinueWalk(walker.AdvanceProcess);
            else
                walker.Walk(_walkingPath, () => Arrive(walker));
        }
        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            walker.CancelWalk();

            if (_itemGiver.HasInstance && !_isReturning)
                _itemGiver.Instance.UnreserveQuantity(_items.Item, _items.Quantity);
            if (_home.HasInstance)
                _home.Instance.ItemStorage.UnreserveCapacity(_items.Item, _items.Quantity);
        }
        public override void End(Walker walker)
        {
            base.End(walker);

            if (_home.HasInstance)
            {
                _home.Instance.ItemStorage.UnreserveCapacity(_items.Item, _items.Quantity);
                _home.Instance.FillRecipient(walker.ItemStorage);
            }
        }

        public void Arrive(Walker walker)
        {
            if (!_itemGiver.HasInstance || !_home.HasInstance)
            {
                walker.AdvanceProcess();
                return;
            }

            _itemGiver.Instance.UnreserveQuantity(_items.Item, _items.Quantity);
            _itemGiver.Instance.Give(walker.ItemStorage, _items.Item, _items.Quantity);

            if (_isHome)
                _walkingPath = _walkingPath.GetReversed();
            else
                _walkingPath = PathHelper.FindPath(walker.CurrentPoint, walker.Home.Instance, walker.PathType, walker.PathTag);

            _isReturning = true;

            walker.Walk(_walkingPath, walker.AdvanceProcess);
        }

        public void OnBeforeSerialize()
        {
            _homeData = _home.GetData();
            _itemGiverData = _itemGiver.GetData();
            _walkingPathData = _walkingPath.GetData();
            _itemsData = _items.GetData();
        }
        public void OnAfterDeserialize()
        {
            _home = _homeData.GetReference<TownHomeComponent>();
            _itemGiver = _itemGiverData.GetReference<IItemGiver>();
            _walkingPath = _walkingPathData.GetPath();
            _items = _itemsData.GetItemQuantity();
        }
    }
}
