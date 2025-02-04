using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walker that retrieves items from <see cref="IItemsDispenser"/> filtered by their key<br/>
    /// does not adjust the path when the dispenser moves<br/>
    /// if it arrives at its destination and the dispenser is out of <see cref="RetrieveDistance"/> it will move again to get close enough
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_items_retriever_walker.html")]
    public class ItemsRetrieverWalker : Walker, IItemOwner
    {
        public enum ItemsRetrieverWalkerState
        {
            Inactive = 0,
            Waiting = 5,
            Approaching = 10,
            Retrieving = 20,
            Returning = 30
        }

        [Tooltip("key of the dispensert this walker targets")]
        public string DispenserKey;
        [Tooltip("maximum distance from home to dispenser")]
        public float MaxDistance = 100;
        [Tooltip("maximum distance for the retriever use a dispenser")]
        public float RetrieveDistance = 1;
        [Tooltip("how long the retriever waits after dispensing")]
        public float RetrieveTime;
        [Tooltip("how fast the walker moves when approaching, regular speed is used returning")]
        public float ApproachSpeed;
        [Tooltip("storage used to store items while carrying them home from the dispenser")]
        public ItemStorage Storage;
        [Tooltip(@"fires whenever the state of the walker changes, useful for animation
Idle        0
Approaching 10
Retrieving  20
Returning   30")]
        public IntEvent IsStateChanged;

        public override ItemStorage ItemStorage => Storage;
        public IItemContainer ItemContainer => Storage;
        public override float Speed => _state == ItemsRetrieverWalkerState.Approaching && ApproachSpeed > 0 ? ApproachSpeed : base.Speed;

        private ItemsRetrieverWalkerState _state;
        private IItemsDispenser _dispenser;

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            setState(ItemsRetrieverWalkerState.Inactive);
        }

        public override void Spawned()
        {
            base.Spawned();

            if (_state == ItemsRetrieverWalkerState.Inactive)
            {
                setState(ItemsRetrieverWalkerState.Waiting);
                approach();
            }
        }

        public void StartRetrieving(WalkingPath path, IItemsDispenser dispenser)
        {
            _dispenser = dispenser;
            setState(ItemsRetrieverWalkerState.Waiting);
            Walk(path, finished: tryDispense);
        }

        public IItemsDispenser GetDispenser(IBuilding building)
        {
            return Dependencies.Get<IItemsDispenserManager>().GetDispenser(DispenserKey, building.WorldCenter, MaxDistance);
        }
        public WalkingPath GetDispenserPath(Vector2Int point, IItemsDispenser itemsDispenser)
        {
            return PathHelper.FindPath(point, Dependencies.Get<IGridPositions>().GetGridPoint(_dispenser.Position), PathType, PathTag);
        }
        public WalkingPath GetDispenserPath(IBuilding building, IItemsDispenser itemsDispenser)
        {
            return PathHelper.FindPath(building, Dependencies.Get<IGridPositions>().GetGridPoint(itemsDispenser.Position), PathType, PathTag);
        }
        public PathQuery GetDispenserPathQuery(Vector2Int point, IItemsDispenser itemsDispenser)
        {
            return PathHelper.FindPathQuery(point, Dependencies.Get<IGridPositions>().GetGridPoint(_dispenser.Position), PathType, PathTag);
        }
        public PathQuery GetDispenserPathQuery(IBuilding building, IItemsDispenser itemsDispenser)
        {
            return PathHelper.FindPathQuery(building, Dependencies.Get<IGridPositions>().GetGridPoint(itemsDispenser.Position), PathType, PathTag);
        }

        private void approach()
        {
            tryWalk(() =>
            {
                if (!(_dispenser as UnityEngine.Object))
                    _dispenser = GetDispenser(Home.Instance);

                if (!(_dispenser as UnityEngine.Object))
                    return null;

                return GetDispenserPathQuery(_current, _dispenser);
            }, () =>
            {
                setState(ItemsRetrieverWalkerState.Approaching);
            }, finished: () => tryDispense(), canceled: onFinished);
        }

        private void tryDispense()
        {
            if (!(_dispenser as UnityEngine.Object))
            {
                _dispenser = GetDispenser(Home.Instance);
                if (!(_dispenser as UnityEngine.Object))
                {
                    setState(ItemsRetrieverWalkerState.Retrieving);
                    Wait(returnHome, RetrieveTime);
                    return;
                }
            }

            var worldPosition = Dependencies.Get<IGridPositions>().GetWorldPosition(_current);
            if (Vector3.Distance(_dispenser.Position, worldPosition) > RetrieveDistance)
            {
                setState(ItemsRetrieverWalkerState.Inactive);
                approach();
                return;
            }

            Storage.AddItems(_dispenser.Dispense());

            setState(ItemsRetrieverWalkerState.Retrieving);

            Wait(returnHome, RetrieveTime);
        }

        private void returnHome()
        {
            setState(ItemsRetrieverWalkerState.Returning);

            var path = PathHelper.FindPath(_current, Home.Instance, PathType, PathTag);
            if (path == null)
                onFinished();
            else
                Walk(path, 0f);
        }

        private void setState(ItemsRetrieverWalkerState state)
        {
            _state = state;
            IsStateChanged?.Invoke((int)_state);
        }

        public override string GetDebugText() => Storage.GetDebugText();

        #region Saving
        [Serializable]
        public class ItemsRetrieverWalkerData
        {
            public WalkerData WalkerData;
            public int State;
            public ItemStorage.ItemStorageData Storage;
            public ItemQuantity.ItemQuantityData Order;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new ItemsRetrieverWalkerData()
            {
                WalkerData = savewalkerData(),
                Storage = Storage.SaveData(),
                State = (int)_state,
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<ItemsRetrieverWalkerData>(json);

            loadWalkerData(data.WalkerData);

            Storage.LoadData(data.Storage);

            _state = (ItemsRetrieverWalkerState)data.State;

            switch (_state)
            {
                case ItemsRetrieverWalkerState.Inactive:
                    approach();
                    break;
                case ItemsRetrieverWalkerState.Approaching:
                    ContinueWalk(approach);
                    break;
                case ItemsRetrieverWalkerState.Retrieving:
                    ContinueWait(returnHome);
                    break;
                case ItemsRetrieverWalkerState.Returning:
                    ContinueWalk();
                    break;
                default:
                    break;
            }

            IsStateChanged?.Invoke((int)_state);
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualItemsRetrieverWalkerSpawner : ManualWalkerSpawner<ItemsRetrieverWalker>
    {
        [Tooltip(@"- Instant
finds dispenser and path to it during spawn
if none is found the spawn is aborted
- Prepared
finds dispenser and prepares path query during spawn
if none is found the spawn is aborted
- Delayed
walker spawns and looks for dispenser while delay runs")]
        public WalkerInitializationMode InitializationMode = WalkerInitializationMode.Instant;

        public void StartRetrieving(MonoBehaviour owner, Vector2Int? accessPoint)
        {
            switch (InitializationMode)
            {
                case WalkerInitializationMode.Instant:
                    var dispenser = Prefab.GetDispenser(_building);
                    if (dispenser == null)
                        return;
                    var path = Prefab.GetDispenserPath(_building, dispenser);
                    if (path == null)
                        return;
                    Spawn(w => w.StartRetrieving(path, dispenser), start: accessPoint);
                    break;
                case WalkerInitializationMode.Prepared:
                    Spawn(owner, () =>
                    {
                        var dispenser = Prefab.GetDispenser(_building);
                        if (dispenser == null)
                            return null;
                        var query = Prefab.GetDispenserPathQuery(_building, dispenser);
                        query.ExtraData = dispenser;
                        return query;
                    }, q =>
                    {
                        var path = q.Complete();
                        if (path == null)
                            return null;
                        return Tuple.Create((IItemsDispenser)q.ExtraData, path);
                    }, (w, r) =>
                    {
                        w.StartRetrieving(r.Item2, r.Item1);
                    }, start: accessPoint);
                    break;
                default:
                    Spawn(start: accessPoint);
                    break;
            }
        }
    }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicItemsRetrieverWalkerSpawner : CyclicWalkerSpawner<ItemsRetrieverWalker>
    {
        [Tooltip(@"- Instant
finds dispenser and path to it during spawn
if none is found the spawn is aborted
- Prepared
finds dispenser and prepares path query during spawn
if none is found the spawn is aborted
- Delayed
walker spawns and looks for dispenser while delay runs")]
        public WalkerInitializationMode InitializationMode = WalkerInitializationMode.Instant;

        public void InitializeRetrieving(IBuilding building, MonoBehaviour owner, Action<ItemsRetrieverWalker> onFinished = null)
        {
            switch (InitializationMode)
            {
                case WalkerInitializationMode.Instant:
                    Initialize(building, w =>
                    {
                        var dispenser = w.GetDispenser(building);
                        if (dispenser == null)
                            return false;
                        var path = w.GetDispenserPath(building, dispenser);
                        if (path == null)
                            return false;
                        w.StartRetrieving(path, dispenser);
                        return true;
                    }, onFinished);
                    break;
                case WalkerInitializationMode.Prepared:
                    Initialize(building, owner, () =>
                    {
                        var dispenser = Prefab.GetDispenser(building);
                        if (dispenser == null)
                            return null;
                        var query = Prefab.GetDispenserPathQuery(building, dispenser);
                        query.ExtraData = dispenser;
                        return query;
                    }, q =>
                    {
                        var path = q.Complete();
                        if (path == null)
                            return null;
                        return Tuple.Create((IItemsDispenser)q.ExtraData, path);
                    }, (w, r) =>
                    {
                        w.StartRetrieving(r.Item2, r.Item1);
                    }, onFinished);
                    break;
                default:
                    Initialize(building, onFinished: onFinished);
                    break;
            }
        }
    }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledItemsRetrieverWalkerSpawner : PooledWalkerSpawner<ItemsRetrieverWalker>
    {
        [Tooltip(@"- Instant
finds dispenser and path to it during spawn
if none is found the spawn is aborted
- Prepared
finds dispenser and prepares path query during spawn
if none is found the spawn is aborted
- Delayed
walker spawns and looks for dispenser while delay runs")]
        public WalkerInitializationMode InitializationMode = WalkerInitializationMode.Instant;

        public void InitializeRetrieving(IBuilding building, MonoBehaviour owner, Action<ItemsRetrieverWalker> onFinished = null)
        {
            switch (InitializationMode)
            {
                case WalkerInitializationMode.Instant:
                    Initialize(building, w =>
                    {
                        var dispenser = w.GetDispenser(building);
                        if (dispenser == null)
                            return false;
                        var path = w.GetDispenserPath(building, dispenser);
                        if (path == null)
                            return false;
                        w.StartRetrieving(path, dispenser);
                        return true;
                    }, onFinished);
                    break;
                case WalkerInitializationMode.Prepared:
                    Initialize(building, owner, () =>
                    {
                        var dispenser = Prefab.GetDispenser(building);
                        if (dispenser == null)
                            return null;
                        var query = Prefab.GetDispenserPathQuery(building, dispenser);
                        query.ExtraData = dispenser;
                        return query;
                    }, q =>
                    {
                        var path = q.Complete();
                        if (path == null)
                            return null;
                        return Tuple.Create((IItemsDispenser)q.ExtraData, path);
                    }, (w, r) =>
                    {
                        w.StartRetrieving(r.Item2, r.Item1);
                    }, onFinished);
                    break;
                default:
                    Initialize(building, onFinished: onFinished);
                    break;
            }
        }
    }
}