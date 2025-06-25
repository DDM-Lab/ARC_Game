using CityBuilderCore;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// task that makes walkers deliver whatever items are needed by the <see cref="IItemReceiver"/> of the building under the task<br/>
    /// used to supply food and wood to <see cref="TownMarketComponent"/>, to get building materials to <see cref="TownConstructionComponent"/> and for raw materials needed in <see cref="TownProductionComponent"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_deliver_task.html")]
    public class TownDeliverTask : TownTask
    {
        [Tooltip("minimum quantity a giver needs to have before a walker is send")]
        public int MinQuantity = 1;
        [Tooltip("maximum quantity a walker can bring in one trip")]
        public int MaxQuantity = 1000;

        public override IEnumerable<TownWalker> Walkers => _walkers;

        private IItemReceiver _receiver;
        public IItemReceiver Receiver
        {
            get
            {
                if (_receiver == null)
                    _receiver = Dependencies.Get<IBuildingManager>().GetBuilding(Point).First().GetBuildingComponent<IItemReceiver>();
                return _receiver;
            }
        }

        private List<TownWalker> _walkers = new List<TownWalker>();

        private Item _currentItem;
        private int _currentQuantity;
        private IItemGiver _currentGiver;
        private WalkingPath _currentPath;

        protected override void Update()
        {
            base.Update();

            if (Visual)
                Visual.gameObject.SetActive(Receiver.GetReceiveItems().Any(i => Receiver.GetReceiveCapacityRemaining(i) > 0));
        }

        public override bool CanStartTask(TownWalker walker)
        {
            if (_currentItem != null)
            {
                if (_currentGiver.GetGiveQuantity(_currentItem) <= 0)
                    _currentItem = null;
                else if (Receiver.GetReceiveCapacityRemaining(_currentItem) <= 0)
                    _currentItem = null;
            }

            if (_currentItem == null)
            {
                foreach (var item in Receiver.GetReceiveItems())
                {
                    if (_receiver.GetReceiveCapacityRemaining(item) <= 0)
                        continue;

                    var giverPath = Dependencies.Get<IGiverPathfinder>().GetGiverPath(_receiver.Building, null, new ItemQuantity(item, MinQuantity), 1000, walker.PathType, walker.PathTag);
                    if (giverPath == null)
                        continue;

                    _currentItem = item;
                    _currentGiver = giverPath.Component.Instance;
                    _currentPath = giverPath.Path.GetReversed();
                }
            }

            return _currentItem != null;
        }
        public override WalkerAction[] StartTask(TownWalker walker)
        {
            var item = _currentItem;

            var walkerPath = PathHelper.FindPath(walker.CurrentPoint, _currentPath.StartPoint, walker.PathType, walker.PathTag);

            var receiveCapacity = Receiver.GetReceiveCapacityRemaining(item);
            var walkerCapacity = walker.Storage.GetItemCapacity(item);
            var giverQuantity = _currentGiver.GetGiveQuantity(item);

            _currentQuantity = Mathf.Min(receiveCapacity, walkerCapacity, giverQuantity, MaxQuantity);

            _currentGiver.ReserveQuantity(item, _currentQuantity);
            Receiver.ReserveCapacity(item, _currentQuantity);

            _walkers.Add(walker);

            var items = new ItemQuantity(item, _currentQuantity);

            return new WalkerAction[]
            {
                new WalkPathAction(walkerPath),
                new GiveItemsAction(_currentGiver, items, true),
                new WalkPathAction(_currentPath),
                new ReceiveItemsAction(Receiver,items, true)
            };
        }
        public override void ContinueTask(TownWalker walker)
        {
            _walkers.Add(walker);
        }
        public override void FinishTask(TownWalker walker, ProcessState process)
        {
            _walkers.Remove(walker);

            if (process.IsCanceled)
            {
                //unreserve quantities
                if (process.CurrentIndex < 1)
                    process.Actions[1].Cancel(walker);

                if (process.CurrentIndex < 3)
                    process.Actions[3].Cancel(walker);
            }
        }

        public void CheckDone()
        {
            if (Receiver.ItemContainer.GetItemCapacity() - Receiver.ItemContainer.GetItemQuantity() <= 0)
                Terminate();
        }

        public override string GetDescription()
        {
            if (_currentItem == null || _receiver == null)
                return string.Empty;
            return $"delivering items to the {Receiver.Building.GetName()}";
        }
    }
}
