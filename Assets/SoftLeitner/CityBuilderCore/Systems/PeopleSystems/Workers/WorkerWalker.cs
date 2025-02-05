using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walks from worker provider to worker user, picks up needed items first if required by the user
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_worker_walker.html")]
    public class WorkerWalker : Walker, IItemOwner
    {
        public enum WorkerState
        {
            Inactive = 0,
            Waiting = 10,
            ToSupply = 20,
            FromSupply = 25,
            ToPlace = 30,
            WalkingIn = 35,
            Work = 40,
            WalkingOut = 45,
            ReturnWaiting = 50,
            Return = 60
        }

        [Tooltip("whether the worker needs to walk back home after he is done working")]
        public bool IsReturn;
        [Tooltip("the maximum distance from home to the workplace as the bird flies")]
        public float MaxDistance = 100;
        [Tooltip("the type of worker the walker provides")]
        public Worker Worker;
        [Tooltip("storage the worker walker uses to store supplies")]
        public ItemStorage Storage;
        [Tooltip("fired whenever the worker starts or stops working")]
        public BoolEvent IsWorkingChanged;

        public override ItemStorage ItemStorage => Storage;
        public IItemContainer ItemContainer => Storage;

        public bool IsWorking => _state == WorkerState.Work;
        public bool IsWorkFinished => _workProgress >= 1f;

        private WorkerState _state;
        private float _workProgress;
        private WorkerPath _workerPath;
        private Vector3 _arrivalPosition;

        private void Update()
        {
            switch (_state)
            {
                case WorkerState.ToPlace:
                case WorkerState.ToSupply:
                case WorkerState.FromSupply:
                case WorkerState.WalkingIn:
                case WorkerState.Work:
                    if (_workerPath.WorkerUser == null || !_workerPath.WorkerUser.HasInstance)
                        Finish();//user has been deleted
                    break;
            }
        }

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            _state = WorkerState.Inactive;

            onIsWorkingChanged();
        }

        public override void Spawned()
        {
            base.Spawned();

            if (_state == WorkerState.Inactive)
            {
                _state = WorkerState.Waiting;
                tryWork();
            }
        }

        public void StartWorking(WorkerPath workerPath)
        {
            _state = WorkerState.Waiting;
            Walk(startWorkingPath(workerPath), finished: tryContinue);
        }

        public void Work(float progress)
        {
            _workProgress = Mathf.Min(1f, _workProgress + progress);
        }

        public void FinishWorking(IEnumerable<Vector3> exitPath = null)
        {
            _workProgress = 0f;

            if (IsReturn)
            {
                if (exitPath != null)
                    walkOut(exitPath, _arrivalPosition);
                else
                    walkHome();
            }
            else
            {
                onFinished();
            }

            onIsWorkingChanged();
        }

        public WorkerPath GetWorkerPath(IBuilding building, Vector2Int? start = null)
        {
            return Dependencies.Get<IWorkplaceFinder>().GetWorkerPath(Home.Instance, start, Worker, Storage, MaxDistance, PathType, PathTag);
        }
        public WorkerPathQuery GetWorkerPathQuery(IBuilding building, Vector2Int? start = null)
        {
            return Dependencies.Get<IWorkplaceFinder>().GetWorkerPathQuery(Home.Instance, start, Worker, Storage, MaxDistance, PathType, PathTag);
        }

        private void walkHome()
        {
            _state = WorkerState.ReturnWaiting;
            tryWalk(() => PathHelper.FindPathQuery(CurrentPoint, Home.Instance, PathType, PathTag), 0f, planned: () => _state = WorkerState.Return);
        }

        private void tryWork()
        {
            tryWalk(() => GetWorkerPathQuery(Home.Instance, _current),
                q => q.Complete(),
                q => startWorkingPath(q),
                finished: tryContinue);
        }

        private WalkingPath startWorkingPath(WorkerPath workerPath)
        {
            _workerPath = workerPath;
            if (_workerPath == null)
                return null;

            _workerPath.WorkerUser.Instance.ReportAssigned(this);

            if (_workerPath.SupplyPath == null)
            {
                _state = WorkerState.ToPlace;
                return _workerPath.PlacePath;
            }
            else
            {
                _workerPath.Giver.Instance.ReserveQuantity(_workerPath.Items.Item, _workerPath.Items.Quantity);

                _state = WorkerState.ToSupply;
                return _workerPath.SupplyPath;
            }
        }

        private void tryContinue()
        {
            switch (_state)
            {
                case WorkerState.Waiting:
                    onFinished();
                    break;
                case WorkerState.ToSupply:
                    _workerPath.Giver.Instance.UnreserveQuantity(_workerPath.Items.Item, _workerPath.Items.Quantity);
                    _workerPath.Giver.Instance.Give(Storage, _workerPath.Items.Item, _workerPath.Items.Quantity);
                    _state = WorkerState.FromSupply;
                    tryWalk(() => PathHelper.FindPathQuery(CurrentPoint, _workerPath.WorkerUser.Instance.Building, PathType, PathTag), finished: tryContinue);
                    break;
                case WorkerState.FromSupply:
                case WorkerState.ToPlace:
                    _arrivalPosition = transform.position;
                    var path = _workerPath.WorkerUser.Instance.ReportArrived(this);
                    if (path == null || path.Length < 1)
                    {
                        _state = WorkerState.Work;
                        onIsWorkingChanged();
                    }
                    else
                    {
                        walkIn(path);
                    }
                    break;
                case WorkerState.WalkingIn:
                    _state = WorkerState.Work;
                    onIsWorkingChanged();
                    _workerPath.WorkerUser.Instance.ReportInside(this);
                    break;
            }
        }

        private void walkIn(IEnumerable<Vector3> positions)
        {
            _state = WorkerState.WalkingIn;
            followPath(new Vector3[] { transform.position }.Union(positions.Select(p => p - Pivot.localPosition)), 0f, tryContinue);
        }
        private void walkOut(IEnumerable<Vector3> positions, Vector3 exit)
        {
            _state = WorkerState.WalkingOut;
            followPath(positions.Select(p => p - Pivot.localPosition).Union(new Vector3[] { exit }), 0f, walkHome);
        }

        private void onIsWorkingChanged()
        {
            IsWorkingChanged?.Invoke(IsWorking);
        }

        protected override void onFinished()
        {
            _state = WorkerState.Inactive;
            base.onFinished();
        }

        #region Saving
        [Serializable]
        public class WorkerWalkerData
        {
            public WalkerData WalkerData;
            public ItemStorage.ItemStorageData Storage;
            public int State;
            public float WorkProgress;
            public WorkerPath.WorkerPathData WorkerPath;
            public Vector3 ArrivalPosition;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new WorkerWalkerData()
            {
                WalkerData = savewalkerData(),
                Storage = Storage.SaveData(),
                State = (int)_state,
                WorkProgress = _workProgress,
                WorkerPath = _workerPath?.GetData(),
                ArrivalPosition = _arrivalPosition
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<WorkerWalkerData>(json);

            loadWalkerData(data.WalkerData);

            Storage.LoadData(data.Storage);

            _state = (WorkerState)data.State;
            _workProgress = data.WorkProgress;
            _workerPath = WorkerPath.FromData(data.WorkerPath);
            _arrivalPosition = data.ArrivalPosition;

            StartCoroutine(loadDelayed());//make sure the worker user has been loaded
        }

        private IEnumerator loadDelayed()
        {
            yield return new WaitForEndOfFrame();

            switch (_state)
            {
                case WorkerState.Waiting:
                    tryWork();
                    break;
                case WorkerState.ToPlace:
                case WorkerState.ToSupply:
                case WorkerState.FromSupply:
                    if (_workerPath.WorkerUser != null && _workerPath.WorkerUser.HasInstance)
                    {
                        _workerPath.WorkerUser.Instance.ReportAssigned(this);
                        ContinueWalk(tryContinue);
                    }
                    else
                    {
                        Finish();
                    }
                    break;
                case WorkerState.WalkingIn:
                    if (_workerPath.WorkerUser != null && _workerPath.WorkerUser.HasInstance)
                    {
                        _workerPath.WorkerUser.Instance.ReportAssigned(this);
                        _workerPath.WorkerUser.Instance.ReportArrived(this);
                        continueFollow(tryContinue);
                    }
                    else
                    {
                        Finish();
                    }
                    break;
                case WorkerState.Work:
                    if (_workerPath.WorkerUser != null && _workerPath.WorkerUser.HasInstance)
                    {
                        _workerPath.WorkerUser.Instance.ReportAssigned(this);
                        _workerPath.WorkerUser.Instance.ReportArrived(this);
                        _workerPath.WorkerUser.Instance.ReportInside(this);
                    }
                    else
                    {
                        Finish();
                    }
                    break;
                case WorkerState.WalkingOut:
                    continueFollow(walkHome);
                    break;
                case WorkerState.ReturnWaiting:
                    walkHome();
                    break;
                case WorkerState.Return:
                    ContinueWalk();
                    break;
            }

            onIsWorkingChanged();
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualWorkerWalkerSpawner : ManualWalkerSpawner<WorkerWalker>
    {
        [Tooltip(@"- Instant
find path on spawn
if none is found the spawn is aborted
- Prepared
the spawner prepares a path query before spawning
no spawn happens if none is found
- Delayed
walker spawns and looks for path while delay runs")]
        public WalkerInitializationMode InitializationMode = WalkerInitializationMode.Instant;

        public void StartWork(MonoBehaviour owner, Vector2Int? accessPoint)
        {
            switch (InitializationMode)
            {
                case WalkerInitializationMode.Instant:
                    var path = Prefab.GetWorkerPath(_building);
                    if (path != null)
                        Spawn(w => w.StartWorking(path), accessPoint);
                    break;
                case WalkerInitializationMode.Prepared:
                    Spawn(owner,
                        () => Prefab.GetWorkerPathQuery(_building, null),
                        q => q.Complete(),
                        (w, p) => w.StartWorking(p));
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
    public class CyclicWorkerWalkerSpawner : CyclicWalkerSpawner<WorkerWalker>
    {
        [Tooltip(@"- Instant
find path on spawn
if none is found the spawn is aborted
- Prepared
the spawner prepares a path query before spawning
no spawn happens if none is found
- Delayed
walker spawns and looks for path while delay runs")]
        public WalkerInitializationMode InitializationMode = WalkerInitializationMode.Instant;

        public void InitializeWork(IBuilding building, MonoBehaviour owner)
        {
            switch (InitializationMode)
            {
                case WalkerInitializationMode.Instant:
                    Initialize(building, w =>
                    {
                        var path = w.GetWorkerPath(building);
                        if (path == null)
                            return false;
                        w.StartWorking(path);
                        return true;
                    });
                    break;
                case WalkerInitializationMode.Prepared:
                    Initialize(building, owner, () => Prefab.GetWorkerPathQuery(building), q => q.Complete(), (w, q) => w.StartWorking(q));
                    break;
                default:
                    Initialize(building);
                    break;
            }
        }
    }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledWorkerWalkerSpawner : PooledWalkerSpawner<WorkerWalker>
    {
        [Tooltip(@"- Instant
find path on spawn
if none is found the spawn is aborted
- Prepared
the spawner prepares a path query before spawning
no spawn happens if none is found
- Delayed
walker spawns and looks for path while delay runs")]
        public WalkerInitializationMode InitializationMode = WalkerInitializationMode.Instant;

        public void InitializeWork(IBuilding building, MonoBehaviour owner)
        {
            switch (InitializationMode)
            {
                case WalkerInitializationMode.Instant:
                    Initialize(building, w =>
                    {
                        var path = w.GetWorkerPath(building);
                        if (path == null)
                            return false;
                        w.StartWorking(path);
                        return true;
                    });
                    break;
                case WalkerInitializationMode.Prepared:
                    Initialize(building, owner, () => Prefab.GetWorkerPathQuery(building), q => q.Complete(), (w, q) => w.StartWorking(q));
                    break;
                default:
                    Initialize(building);
                    break;
            }
        }
    }
}