using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for entites moving about the map<br/>
    /// typically created by some kind of <see cref="WalkerSpawner{T}"/> on a building<br/>
    /// some other ways to create walkers are having spawners on a global manager(urban, town)<br/>
    /// or even instantiating and managing the walker save data yourself(<see cref="TilemapSpawner"/> used in defense)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker.html")]
    public abstract class Walker : MessageReceiver, ISaveData, IOverrideHeight
    {
        /// <summary>
        /// unique walker identifier that is also persisted, can be used to save references to walkers
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();
        /// <summary>
        /// reference to the building the walker was spawned from, if any
        /// </summary>
        public BuildingReference Home { get; set; }
        /// <summary>
        /// returns the current <see cref="ProcessState"/> if a process is currently active
        /// </summary>
        public ProcessState CurrentProcess { get; set; }
        /// <summary>
        /// the process that will be started when the current one finishes<br/>
        /// is used when a process is started when another one is still running
        /// </summary>
        public ProcessState NextProcess { get; set; }
        /// <summary>
        /// returns the current <see cref="RoamingState"/> if the walker is currently roaming
        /// </summary>
        public RoamingState CurrentRoaming { get; set; }
        /// <summary>
        /// returns the current <see cref="WalkingState"/> if the walker is currently walking
        /// </summary>
        public WalkingState CurrentWalking { get; set; }
        /// <summary>
        /// returns the current <see cref="WalkingState"/> if the walker is currently walking using a navmesh agent
        /// </summary>
        public WalkingAgentState CurrentAgentWalking { get; set; }
        /// <summary>
        /// returns the current <see cref="WaitingState"/> if the walker is currently waiting
        /// </summary>
        public WaitingState CurrentWaiting { get; set; }

        public float? HeightOverride { get; set; }

        [Tooltip("contains all the meta info that is relevant to every walker of this type like speed or pathing")]
        public WalkerInfo Info;
        [Tooltip("important transform used in rotation and variance, should contain all visuals")]
        public Transform Pivot;
        [Tooltip("used for the paramters in WalkerInfo and when starting animations from WalkerActions(optional)")]
        public Animator Animator;
        [Tooltip("optional agent that can be used to get to the destination when using PathType.Map, otherwise the walker strictly follows the calculated path")]
        public NavMeshAgent Agent;

        /// <summary>
        /// last map point the walker has finished moving to, used whenever a new path needs to be found or just to check where the walker is
        /// </summary>
        public Vector2Int CurrentPoint => _current;
        /// <summary>
        /// map point the walker is currently positioned on by absolute position
        /// </summary>
        public Vector2Int GridPoint => Dependencies.Get<IGridPositions>().GetGridPoint(Pivot.position);

        public virtual float Speed => Info.Speed;
        public virtual PathType PathType => Info.PathType;
        /// <summary>
        /// optional parameter for pathfinding, depends on PathType<br/>
        /// for example a road for road pathing to only walk on that specific road
        /// </summary>
        public virtual UnityEngine.Object PathTag => Info.PathTagSelf ? Info : Info.PathTag;

        /// <summary>
        /// item storage of the walker if there is one
        /// </summary>
        public virtual ItemStorage ItemStorage => null;

        /// <summary>
        /// currently active action when a process is running
        /// </summary>
        public WalkerAction CurrentAction => CurrentProcess?.CurrentAction;

        private bool _isWalking;
        /// <summary>
        /// whether the walker is currently moving(roaming, walking or agent walking)
        /// </summary>
        public bool IsWalking
        {
            get { return _isWalking; }
            set
            {
                _isWalking = value;
                onIsWalkingChanged(value);
            }
        }

        /// <summary>
        /// addons that are queued up when an addon uses <see cref="BuildingAddon.AddonAccumulationMode.Queue"/><br/>
        /// in this case every addon after the first is put into the queue and when an addon is removed the queue is empties first
        /// </summary>
        protected List<WalkerAddon> _addonsQueue = new List<WalkerAddon>();
        /// <summary>
        /// currently active <see cref="WalkerAddon"/>s on this walker
        /// </summary>
        protected List<WalkerAddon> _addons = new List<WalkerAddon>();
        /// <summary>
        /// currently active <see cref="WalkerAddon"/>s on this walker
        /// </summary>
        public IReadOnlyCollection<WalkerAddon> Addons => _addons;

        [Tooltip("fired whenever IsWalking changes, which occurs when the walker starts or stops moving, useful for setting animation parameters(for example in the historic demo)")]
        public BoolEvent IsWalkingChanged;
        [Tooltip("fired whenever the walking direction changes, useful for setting animation parameters(for example in the historic demo)")]
        public Vector3Event DirectionChanged;

        /// <summary>
        /// fired when the walker is finished, used by spawners to destroy/deactivate the walker
        /// </summary>
        public event Action<Walker> Finished;
        /// <summary>
        /// fired whenever the walker has moved to a 'corner' in its path, for every point when roaming
        /// </summary>
        public event Action<Walker> Moved;

        protected float _defaultAgentDistance;
        /// <summary>
        /// map point the walker was initialized on, used in roamers to return back to
        /// </summary>
        protected Vector2Int _start;
        /// <summary>
        /// last map point the walker has finished moving to, used whenever a new path needs to be found or just to check where the walker is
        /// </summary>
        protected Vector2Int _current;
        /// <summary>
        /// whether the walker has finished and is no longer active on the map, can be used to make sure a walker does not perform any further actions once finished
        /// </summary>
        protected bool _isFinished;

        /// <summary>
        /// whether the walker has been loaded as opposed to spawned
        /// </summary>
        private bool _isLoaded;

        protected virtual void Start()
        {
            if (!_isLoaded && Pivot)//pivot position is saved and restored which includes variance, therefore dont vary when loading
                Pivot.localPosition += Dependencies.Get<IMap>().GetVariance();

            if (ItemStorage != null)
            {
                ItemStorage.Changed += onItemStorageChanged;
                onItemStorageChanged(ItemStorage);
            }
        }

        protected virtual void OnDestroy()
        {
            if (!gameObject.scene.isLoaded)
                return;

            //when the walker gets destroy without having finished(reload, attack, ...)
            Dependencies.Get<IWalkerManager>().DeregisterWalker(this);
        }

        /// <summary>
        /// called right after instantiating or reactivating a walker<br/>
        /// buildings have not had a chance to interact with the walker<br/>
        /// when your logic needs something from outside first override <see cref="Spawned"/> instead
        /// </summary>
        /// <param name="home"></param>
        /// <param name="start"></param>
        public virtual void Initialize(BuildingReference home, Vector2Int start)
        {
            _start = start;
            _current = start;

            Home = home;
            IsWalking = false;

            if (Agent)
                _defaultAgentDistance = Agent.stoppingDistance;

            Dependencies.Get<IWalkerManager>().RegisterWalker(this);
        }

        /// <summary>
        /// called after the walker is fully initialized and its spawning has been signaled to the owner
        /// </summary>
        public virtual void Spawned() { }

        /// <summary>
        /// starts a series of <see cref="WalkerAction"/>s as a process<br/>
        /// the actions will be executed in order until the end is reached
        /// if another process is already running it is cancelled<br/>
        /// </summary>
        /// <param name="actions">series of actions that is executed in order as a process</param>
        /// <param name="key">can be used to identify the process, used to check what the walker is currently doing</param>
        public void StartProcess(WalkerAction[] actions, string key = null)
        {
            if (CurrentProcess == null)
            {
                CurrentProcess = new ProcessState(key, actions);
                CurrentProcess.Start(this);
            }
            else
            {
                NextProcess = new ProcessState(key, actions);
                CurrentProcess.Cancel(this);
            }
        }
        /// <summary>
        /// mvoes the process to the next action or finishes it when the last action is finished
        /// </summary>
        public void AdvanceProcess()
        {
            if (CurrentProcess.Advance(this))
                return;
            var process = CurrentProcess;
            CurrentProcess = null;

            if (NextProcess == null)
            {
                onProcessFinished(process);
            }
            else
            {
                StartProcess(NextProcess.Actions, NextProcess.Key);
                NextProcess = null;
            }
        }
        /// <summary>
        /// requests for the current process to be cancelled
        /// </summary>
        public void CancelProcess()
        {
            CurrentProcess?.Cancel(this);
        }
        /// <summary>
        /// called when the walker is loaded
        /// </summary>
        protected internal void continueProcess()
        {
            CurrentProcess.Continue(this);
        }
        /// <summary>
        /// called when the current process is finished, may be overridden to look for something else to do(for example in the town demo)
        /// </summary>
        /// <param name="process"></param>
        protected virtual void onProcessFinished(ProcessState process)
        {
            onFinished();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            try
            {
                string debugText = GetDebugText();
                if (string.IsNullOrWhiteSpace(debugText))
                    return;

                UnityEditor.Handles.Label(Pivot.position, debugText);
            }
            catch
            {
                //dont care
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (CurrentWalking?.WalkingPath != null)
            {
                Gizmos.color = Color.white;

                for (int i = 0; i < CurrentWalking.WalkingPath.Length - 1; i++)
                {
                    Gizmos.DrawLine(CurrentWalking.WalkingPath.GetPreviousPosition(i), CurrentWalking.WalkingPath.GetNextPosition(i));
                }
            }
        }
#endif

        /// <summary>
        /// called whenever movement reaches a point in its path<br/>
        /// these are corner points in regular movement and every path point when roaming
        /// </summary>
        /// <param name="point">the point reached during movement</param>
        protected virtual void onMoved(Vector2Int point)
        {
            _current = point;
            Moved?.Invoke(this);
        }

        /// <summary>
        /// called to remove the walker from the map when it has done its job(or died)
        /// </summary>
        protected virtual void onFinished()
        {
            _isFinished = true;

            Dependencies.Get<IWalkerManager>().DeregisterWalker(this);

            Finished?.Invoke(this);
            if (ItemStorage != null)
                ItemStorage.Clear();
            if (gameObject && gameObject.activeSelf)
                Destroy(gameObject);//in case walker was not released by spawner
        }

        /// <summary>
        /// walker starts walking around semi randomly, visited points are memorized and avoided
        /// </summary>
        /// <param name="memoryLength">how many points the walker will memorize and try to avoid</param>
        /// <param name="range">how many steps the walker roams before returning</param>
        /// <param name="pathType">which kind of pathfinding will be performed to check which points are available</param>
        /// <param name="pathTag">additional parameter for pathfinding</param>
        /// <param name="finished">called when roaming is finished(regularly or canceled)</param>
        public void Roam(int memoryLength, int range, PathType pathType, UnityEngine.Object pathTag, Action finished = null)
        {
            StartCoroutine(WalkingPath.Roam(this, Info.Delay, _current, memoryLength, range, pathType, pathTag, finished ?? onFinished, onMoved));
        }
        /// <summary>
        /// walker starts walking around semi randomly, visited points are memorized and avoided
        /// </summary>
        /// <param name="memoryLength">how many points the walker will memorize and try to avoid</param>
        /// <param name="range">how many steps the walker roams before returning</param>
        /// <param name="finished">called when roaming is finished(regularly or canceled)</param>
        public void Roam(int memoryLength, int range, Action finished = null)
        {
            StartCoroutine(WalkingPath.Roam(this, Info.Delay, _current, memoryLength, range, PathType, PathTag, finished ?? onFinished, onMoved));
        }
        /// <summary>
        /// walker starts walking around semi randomly after waiting for a set duration, visited points are memorized and avoided
        /// </summary>
        /// <param name="delay">duration in seconds the walker waits before starting to roam</param>
        /// <param name="memoryLength">how many points the walker will memorize and try to avoid</param>
        /// <param name="range">how many steps the walker roams before returning</param>
        /// <param name="finished">called when roaming is finished(regularly or canceled)</param>
        public void Roam(float delay, int memoryLength, int range, Action finished = null)
        {
            StartCoroutine(WalkingPath.Roam(this, delay, _current, memoryLength, range, PathType, PathTag, finished ?? onFinished, onMoved));
        }
        /// <summary>
        /// continues roaming when the game is loaded and the walker was roaming previously
        /// uses the default pathing defined in the walkers inspector to get points
        /// </summary>
        /// <param name="memoryLength">how many points the walker will memorize and try to avoid</param>
        /// <param name="range">how many steps the walker roams before returning</param>
        /// <param name="finished">called when roaming is finished(regularly or canceled)</param>
        public void ContinueRoam(int memoryLength, int range, Action finished)
        {
            StartCoroutine(WalkingPath.ContinueRoam(this, memoryLength, range, PathType, PathTag, finished ?? onFinished, onMoved));
        }
        /// <summary>
        /// continues roaming when the game is loaded and the walker was roaming previously
        /// uses the pathing passed in the parameters to get points
        /// </summary>
        /// <param name="memoryLength">how many points the walker will memorize and try to avoid</param>
        /// <param name="range">how many steps the walker roams before returning</param>
        /// <param name="pathType">which kind of pathfinding will be performed to check which points are available</param>
        /// <param name="pathTag">additional parameter for pathfinding</param>
        /// <param name="finished">called when roaming is finished(regularly or canceled)</param>
        public void ContinueRoam(int memoryLength, int range, PathType pathType, UnityEngine.Object pathTag, Action finished)
        {
            StartCoroutine(WalkingPath.ContinueRoam(this, memoryLength, range, pathType, pathTag, finished ?? onFinished, onMoved));
        }
        /// <summary>
        /// requests for the current roaming to be stopped, will not happen immediately but when the next point is reached
        /// </summary>
        public void CancelRoam()
        {
            CurrentRoaming?.Cancel();
        }

        /// <summary>
        /// tries to walk to a building using default pathing defined in the inspector
        /// </summary>
        /// <param name="target">the building to walk to</param>
        /// <param name="finished">callback for when the walking is finished</param>
        /// <returns>whether walking was successfully started, false if no path was found</returns>
        public bool Walk(IBuilding target, Action finished = null)
        {
            var path = PathHelper.FindPath(_current, target, PathType, PathTag);
            if (path == null)
                return false;
            Walk(path, finished);
            return true;
        }
        /// <summary>
        /// tries to walk to a specific point using default pathing defined in the inspector
        /// </summary>
        /// <param name="target">the map point to walk to</param>
        /// <param name="finished">callback for when the walking is finished</param>
        /// <returns>whether walking was successfully started, false if no path was found</returns>
        public bool Walk(Vector2Int target, Action finished = null)
        {
            return Walk(target, PathType, PathTag, finished);
        }
        /// <summary>
        /// tries to walk to a specific point using the pathing passed in the parameters
        /// </summary>
        /// <param name="target">the map point to walk to</param>
        /// <param name="pathType">which kind of pathfinding will be performed to check which points are available</param>
        /// <param name="pathTag">additional parameter for pathfinding</param>
        /// <param name="finished">callback for when the walking is finished</param>
        /// <returns>whether walking was successfully started, false if no path was found</returns>
        public bool Walk(Vector2Int target, PathType pathType, UnityEngine.Object pathTag, Action finished = null)
        {
            var path = PathHelper.FindPath(_current, target, pathType, pathTag);
            if (path == null)
                return false;

            if (pathType == PathType.Map && Agent)
                StartCoroutine(path.WalkAgent(this, Info.Delay, finished ?? onFinished, onMoved));
            else
                StartCoroutine(path.Walk(this, Info.Delay, finished ?? onFinished, onMoved));

            return true;
        }
        /// <summary>
        /// starts walking a set walking path
        /// </summary>
        /// <param name="path">finished walking path that contains all the points the walker needs to walk to in order</param>
        /// <param name="finished">callback for when the walking is finished</param>
        public void Walk(WalkingPath path, Action finished = null)
        {
            Walk(path, Info.Delay, finished);
        }
        /// <summary>
        /// starts walking a set walking path
        /// </summary>
        /// <param name="path">finished walking path that contains all the points the walker needs to walk to in order</param>
        /// <param name="delay">duration to wait before walking is started in seconds</param>
        /// <param name="finished">callback for when the walking is finished</param>
        public void Walk(WalkingPath path, float delay, Action finished = null)
        {
            if (PathType == PathType.Map && Agent)
                StartCoroutine(path.WalkAgent(this, delay, finished ?? onFinished, onMoved));
            else
                StartCoroutine(path.Walk(this, delay, finished ?? onFinished, onMoved));
        }
        /// <summary>
        /// starts walking using the <see cref="Agent"/> of the walker
        /// </summary>
        /// <param name="position">destination position for the walker</param
        /// <param name="finished">callback for when the walking is finished</param>
        public void WalkAgent(Vector3 position, Action finished = null, float? distance = null)
        {
            StartCoroutine(WalkingPath.WalkAgent(this, position, Info.Delay, finished ?? onFinished, onMoved, distance ?? _defaultAgentDistance));
        }
        /// <summary>
        /// starts walking using the <see cref="Agent"/> of the walker
        /// </summary>
        /// <param name="position">destination position for the walker</param
        /// <param name="delay">duration to wait before walking is started in seconds</param>
        /// <param name="finished">callback for when the walking is finished</param>
        public void WalkAgent(Vector3 position, float delay, Action finished = null, float? distance = null)
        {
            StartCoroutine(WalkingPath.WalkAgent(this, position, delay, finished ?? onFinished, onMoved, distance ?? _defaultAgentDistance));
        }
        /// <summary>
        /// when the game is loaded and the walker was previously walking it is continued here
        /// </summary>
        /// <param name="finished">callback for when the walking is finished</param>
        public void ContinueWalk(Action finished = null)
        {
            if (Agent && CurrentAgentWalking != null)
                StartCoroutine(WalkingPath.ContinueWalkAgent(this, finished ?? onFinished, onMoved));
            else
                StartCoroutine(CurrentWalking.WalkingPath.ContinueWalk(this, finished ?? onFinished, onMoved));
        }
        /// <summary>
        /// stops the current walking, walking may not stop immediately but rather when the walker reaches the next full point
        /// </summary>
        public void CancelWalk()
        {
            if (Agent && CurrentAgentWalking != null)
                CurrentAgentWalking.Cancel();
            else
                CurrentWalking.Cancel();
        }

        /// <summary>
        /// starts walking to a random neighbouring point if one is available
        /// </summary>
        /// <param name="finished">called when the point is reached or immediately if none is available</param>
        public void Wander(Action finished)
        {
            var adjacent = PathHelper.GetAdjacent(CurrentPoint, PathType, PathTag);
            if (!adjacent.Any())
            {
                finished();
                return;
            }

            Walk(new WalkingPath(new[] { CurrentPoint, adjacent.Random() }), finished);
        }
        /// <summary>
        /// when the game is loaded and the walker was previously wandering it is continued here
        /// </summary>
        /// <param name="finished">called when the point is reached</param>
        public void ContinueWander(Action finished)
        {
            ContinueWalk(finished);
        }

        /// <summary>
        /// starts waiting for a set duration
        /// </summary>
        /// <param name="finished">called when the duration has passed</param>
        /// <param name="time">duration in seconds</param>
        public void Wait(Action finished, float time)
        {
            StartCoroutine(waitRoutine(finished, time));
        }
        /// <summary>
        /// waits for the duration configured in <see cref="WalkerInfo.Delay"/>
        /// </summary>
        /// <param name="finished">called when the delay has passed</param>
        public void Delay(Action finished)
        {
            StartCoroutine(waitRoutine(finished, Info.Delay));
        }
        private IEnumerator waitRoutine(Action finished, float time)
        {
            CurrentWaiting = new WaitingState(time);
            yield return continueWaitRoutine(finished);
        }
        /// <summary>
        /// when the game is loaded and the walker was previously waiting it is continued here
        /// </summary>
        /// <param name="finished">called when the waiting time has passed</param>
        public void ContinueWait(Action finished)
        {
            StartCoroutine(continueWaitRoutine(finished));
        }
        /// <summary>
        /// coroutine used for waiting
        /// </summary>
        /// <param name="finished">called when the waiting time has passed</param>
        /// <returns>the coroutine enumerator</returns>
        private IEnumerator continueWaitRoutine(Action finished)
        {
            yield return CurrentWaiting.Wait();
            CurrentWaiting = null;

            if (finished == null)
                onFinished();
            else
                finished.Invoke();
        }
        /// <summary>
        /// cancels waiting, remaining time is set to 0 and finished is called
        /// </summary>
        public void CancelWait()
        {
            CurrentWaiting.Cancel();
        }

        /// <summary>
        /// starts walking a set series of points
        /// </summary>
        /// <param name="path">series of points the walker needs to walk to in order</param>
        /// <param name="finished">callback for when the walking is finished</param>
        protected void followPath(IEnumerable<Vector3> path, Action finished)
        {
            followPath(path, Info.Delay, finished);
        }
        /// <summary>
        /// starts walking a set walking path
        /// </summary>
        /// <param name="path">series of points the walker needs to walk to in order</param>
        /// <param name="delay">duration to wait before walking is started in seconds</param>
        /// <param name="finished">callback for when the walking is finished</param>
        protected void followPath(IEnumerable<Vector3> path, float delay, Action finished)
        {
            StartCoroutine(new WalkingPath(path.ToArray()).Walk(this, delay, finished ?? onFinished));
        }
        /// <summary>
        /// continues following a path when a game is loaded and the walker was previously doing so
        /// </summary>
        /// <param name="finished"></param>
        protected void continueFollow(Action finished)
        {
            StartCoroutine(CurrentWalking.WalkingPath.ContinueWalk(this, finished ?? onFinished));
        }

        /// <summary>
        /// tries to walk to a building, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="target">the building the walker will try to reach</param>
        /// <param name="delay">duration in seconds the walker will always wait before moving(instead of the default <see cref="WalkerInfo.Delay"/>)</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk(IBuilding target, float delay, Action planned = null, Action finished = null, Action canceled = null)
        {
            tryWalk(() => PathHelper.FindPathQuery(_current, target, PathType, PathTag), delay, planned, finished, canceled);
        }
        /// <summary>
        /// tries to walk to a point, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="target">the building the walker will try to reach</param>
        /// <param name="delay">duration in seconds the walker will always wait before moving(instead of the default <see cref="WalkerInfo.Delay"/>)</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk(Vector2Int target, float delay, Action planned = null, Action finished = null, Action canceled = null)
        {
            tryWalk(() => PathHelper.FindPathQuery(_current, target, PathType, PathTag), delay, planned, finished, canceled);
        }
        /// <summary>
        /// tries to walk a path that is calculated externaly, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="pathGetter">function that returns the path the walker should walk or null if no path could be found currently</param>
        /// <param name="delay">duration in seconds the walker will always wait before moving(instead of the default <see cref="WalkerInfo.Delay"/>)</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk(Func<WalkingPath> pathGetter, float delay, Action planned = null, Action finished = null, Action canceled = null)
        {
            StartCoroutine(WalkingPath.TryWalk(this, delay, pathGetter, planned, finished ?? onFinished, canceled, onMoved));
        }

        /// <summary>
        /// tries to walk to a building, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out<br/>
        /// </summary>
        /// <param name="target">the building the walker will try to reach</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk(IBuilding target, Action planned = null, Action finished = null, Action canceled = null)
        {
            tryWalk(() => PathHelper.FindPath(_current, target, PathType, PathTag), planned, finished, canceled);
        }
        /// <summary>
        /// tries to walk to a point, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="target">the building the walker will try to reach</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk(Vector2Int target, Action planned = null, Action finished = null, Action canceled = null)
        {
            tryWalk(() => PathHelper.FindPath(_current, target, PathType, PathTag), planned, finished, canceled);
        }
        /// <summary>
        /// tries to walk a path that is calculated externaly, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="pathGetter">function that returns the path the walker should walk or null if no path could be found currently</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk(Func<WalkingPath> pathGetter, Action planned = null, Action finished = null, Action canceled = null)
        {
            StartCoroutine(WalkingPath.TryWalk(this, Info.Delay, pathGetter, planned, finished ?? onFinished, canceled, onMoved));
        }

        /// <summary>
        /// tries to walk a path that is calculated externaly using a query, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="queryGetter">function that returns the query for the path the walker should walk or null if no path could be found currently</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk(Func<PathQuery> queryGetter, Action planned = null, Action finished = null, Action canceled = null)
        {
            tryWalk(queryGetter, Info.Delay, planned, finished, canceled);
        }
        /// <summary>
        /// tries to walk a path that is calculated externaly using a query, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="queryGetter">function that returns the query for the path the walker should walk or null if no path could be found currently</param>
        /// <param name="delay">how long the walker waits before moving, gives pathfinding some time to calculate</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk(Func<PathQuery> queryGetter, float delay, Action planned = null, Action finished = null, Action canceled = null)
        {
            StartCoroutine(WalkingPath.TryWalk(this, delay, queryGetter, planned, finished ?? onFinished, canceled, onMoved));
        }

        /// <summary>
        /// tries to walk a path to a component that is calculated externaly using a query, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="queryGetter">function that returns the query for the component path the walker should walk or null if no path could be found currently</param>
        /// <param name="planned">called when a component path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk<T>(Func<BuildingComponentPathQuery<T>> queryGetter, Action<BuildingComponentPath<T>> planned = null, Action finished = null, Action canceled = null)
                        where T : IBuildingComponent
        {
            tryWalk(queryGetter, Info.Delay, planned, finished, canceled);
        }
        /// <summary>
        /// tries to walk a path to a component that is calculated externaly using a query, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="queryGetter">function that returns the query for the component path the walker should walk or null if no path could be found currently</param>
        /// <param name="delay">how long the walker waits before moving, gives pathfinding some time to calculate</param>
        /// <param name="planned">called when a component path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk<T>(Func<BuildingComponentPathQuery<T>> queryGetter, float delay, Action<BuildingComponentPath<T>> planned = null, Action finished = null, Action canceled = null)
                        where T : IBuildingComponent
        {
            StartCoroutine(WalkingPath.TryWalk(this, delay, queryGetter, planned, finished ?? onFinished, canceled, onMoved));
        }
        /// <summary>
        /// tries to walk a path that created in a two step process, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="preparer">first step, for example to start a query</param>
        /// <param name="planner">second step, result of the first step is passed, for example to complete a query that was prepared</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk<Q, P>(Func<Q> preparer, Func<Q, P> planner, Func<P, WalkingPath> planned, Action finished = null, Action canceled = null)
        {
            tryWalk(preparer, planner, planned, Info.Delay, finished, canceled);
        }
        /// <summary>
        /// tries to walk a path that created in a two step process, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="preparer">first step, for example to start a query</param>
        /// <param name="planner">second step, result of the first step is passed, for example to complete a query that was prepared</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="delay">how long the walker waits before moving, gives pathfinding some time to calculate between preparer and planner</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        protected void tryWalk<Q, P>(Func<Q> preparer, Func<Q, P> planner, Func<P, WalkingPath> planned, float delay, Action finished = null, Action canceled = null)
        {
            StartCoroutine(WalkingPath.TryWalk(this, delay, preparer, planner, planned, finished ?? onFinished, canceled, onMoved));
        }

        protected internal virtual void onIsWalkingChanged(bool value)
        {
            IsWalkingChanged?.Invoke(value);
            Info.SetAnimationWalk(this, value);
        }
        protected internal virtual void onDirectionChanged(Vector3 direction)
        {
            DirectionChanged?.Invoke(direction);
            Info.SetAnimationDirection(this, direction);
            Dependencies.Get<IGridRotations>()?.SetRotation(Pivot, direction);
        }
        protected internal virtual void onItemStorageChanged(ItemStorage itemStorage)
        {
            Info.SetAnimationCarry(this, itemStorage.HasItems());
        }

        /// <summary>
        /// hides the walker by deactivating its pivot and disableing its collider
        /// </summary>
        public void Hide()
        {
            Pivot.gameObject.SetActive(false);
            var collider = GetComponent<Collider>();
            if (collider)
                collider.enabled = false;
        }
        /// <summary>
        /// hides the walker by activating its pivot and enableing its collider
        /// </summary>
        public void Show()
        {
            Pivot.gameObject.SetActive(true);
            var collider = GetComponent<Collider>();
            if (collider)
                collider.enabled = true;
        }

        /// <summary>
        /// Removes the walker from the map(when it has done its job or died for example)
        /// </summary>
        public void Finish() => onFinished();

        public virtual string GetName() => Info.Name;
        /// <summary>
        /// gets displayed in dialogs to show the walker description to players
        /// </summary>
        /// <returns>a descriptive string or an empty one</returns>
        public virtual string GetDescription() => Info.Descriptions.FirstOrDefault();
        /// <summary>
        /// returns text that gets displayed for debugging in the scene editor
        /// </summary>
        /// <returns>a debugging string or null</returns>
        public virtual string GetDebugText() => null;
        /// <summary>
        /// returns a specific description that may explain the current walker status
        /// </summary>
        /// <param name="index">index of the description within the walker info</param>
        /// <returns>an explanatory string meant for players</returns>
        protected string getDescription(int index) => Info.Descriptions.ElementAtOrDefault(index);
        /// <summary>
        /// returns a specific description that may explain the current walker status with placeholders replaced by specified parameters<br/>
        /// for example 'delivering {1} to {2} from {0}' where 0 is the home building name, 1 is the item name and 2 is the target building(DeliveryWalker in ThreeDemo)
        /// </summary>
        /// <param name="index">index of the description within the walker info</param>
        /// <param name="parameters">array of parameters that replace placeholders in the description</param>
        /// <returns>an explanatory string meant for players</returns>
        protected string getDescription(int index, params object[] parameters)
        {
            if (parameters == null)
                return getDescription(index);
            else
                return string.Format(getDescription(index), parameters);
        }

        /// <summary>
        /// adds and initializes an addon onto the walker from a prefab
        /// </summary>
        /// <typeparam name="T">type of the addon</typeparam>
        /// <param name="prefab">prefab that gets instantiated on the walker</param>
        /// <returns>the instantiated addon instance</returns>
        public T AddAddon<T>(T prefab) where T : WalkerAddon
        {
            T addon;

            switch (prefab.Accumulation)
            {
                case BuildingAddon.AddonAccumulationMode.Queue:
                    addon = GetAddon<T>(prefab.Key);
                    if (addon)
                    {
                        _addonsQueue.Add(prefab);
                        return addon;
                    }
                    break;
                case BuildingAddon.AddonAccumulationMode.Replace:
                    addon = GetAddon<T>(prefab.Key);
                    if (addon)
                        RemoveAddon(addon);
                    break;
                case BuildingAddon.AddonAccumulationMode.Single:
                    addon = GetAddon<T>(prefab.Key);
                    if (addon)
                        return addon;
                    break;
            }

            addon = Instantiate(prefab, Pivot);

            _addons.Add(addon);

            addon.Walker = this;
            addon.InitializeAddon();

            return addon;
        }
        /// <summary>
        /// get the first addon of the specified type
        /// </summary>
        /// <typeparam name="T">type of addon to look for</typeparam>
        /// <param name="_">for example the prefab the addon was instantiated from</param>
        /// <returns>a fitting addon or null if none were found</returns>
        public T GetAddon<T>(T _) where T : WalkerAddon
        {
            return _addons.OfType<T>().FirstOrDefault();
        }
        /// <summary>
        /// get the first addon with the specified key
        /// </summary>
        /// <typeparam name="T">type of addon to look for(WalkerAddon for any type)</typeparam>
        /// <param name="key">key of the addon to look for</param>
        /// <returns>a fitting addon or null if none were found</returns>
        public T GetAddon<T>(string key) where T : WalkerAddon
        {
            return _addons.OfType<T>().FirstOrDefault(a => key == null || a.Key == key);
        }
        /// <summary>
        /// checks if a certain addon type is present on the walker
        /// </summary>
        /// <typeparam name="T">type of addon to check</typeparam>
        /// <returns>if at least one of the addon type is active on the walker</returns>
        public bool HasAddon<T>() where T : WalkerAddon
        {
            return _addons.OfType<T>().Any();
        }
        /// <summary>
        /// checks if a certain addon type is present on the walker
        /// </summary>
        /// <typeparam name="T">type of addon to check</typeparam>
        /// <param name="_">for example the prefab the addon was instantiated from</param>
        /// <returns>if at least one of the addon type is active on the walker</returns>
        public bool HasAddon<T>(T _) where T : WalkerAddon
        {
            return _addons.OfType<T>().Any();
        }
        /// <summary>
        /// checks if an addon with a specific key is present on the walker
        /// </summary>
        /// <param name="key">key of the addon to look for</param>
        /// <returns>if at least one of the desired addon is active on the walker</returns>
        public bool HasAddon(string key)
        {
            return _addons.Any(a => key == null || a.Key == key);
        }
        /// <summary>
        /// terminates and removes an addon from the walker
        /// </summary>
        /// <param name="addon">the addon to remove</param>
        public void RemoveAddon(WalkerAddon addon)
        {
            switch (addon.Accumulation)
            {
                case BuildingAddon.AddonAccumulationMode.Queue:
                    var queuedAddon = _addonsQueue.FirstOrDefault(a => a.Key == addon.Key);
                    if (queuedAddon)
                    {
                        _addonsQueue.Remove(queuedAddon);
                        return;
                    }
                    break;
            }

            addon.TerminateAddon();

            _addons.Remove(addon);
        }
        /// <summary>
        /// terminates and removes an addon from the building with the given key or returns false if none were found
        /// </summary>
        /// <param name="key"></param>
        /// <returns>true if an addon was found and removed</returns>
        public bool RemoveAddon(string key)
        {
            var addon = _addons.FirstOrDefault(a => a.Key == key);
            if (addon == null)
                return false;

            RemoveAddon(addon);
            return true;
        }

        #region Saving
        [Serializable]
        public class WalkerData
        {
            public string Id;
            public string HomeId;
            public Vector2Int StartPoint;
            public Vector2Int CurrentPoint;
            public Vector3 Position;
            public Vector3 PivotPosition;
            public Quaternion Rotation;
            public Quaternion PivotRotation;
            public ProcessState.ProcessData CurrentProcess;
            public ProcessState.ProcessData NextProcess;
            public RoamingState.RoamingData CurrentRoaming;
            public WalkingState.WalkingData CurrentWalking;
            public WalkingAgentState.WalkingAgentData CurrentWalkingAgent;
            public WaitingState.WaitingData CurrentWait;
            public float? HeightOverride;
            public string[] AddonsQueue;
            public WalkerAddonMetaData[] Addons;
        }
        [Serializable]
        public class WalkerAddonMetaData
        {
            public string Key;
            public string Data;
        }

        public virtual string SaveData() => JsonUtility.ToJson(savewalkerData());
        public virtual void LoadData(string json) => loadWalkerData(JsonUtility.FromJson<WalkerData>(json));

        protected WalkerData savewalkerData()
        {
            return new WalkerData()
            {
                Id = Id.ToString(),
                HomeId = Home?.Instance.Id.ToString(),
                StartPoint = _start,
                CurrentPoint = _current,
                Position = transform.position,
                Rotation = transform.rotation,
                PivotPosition = Pivot.localPosition,
                PivotRotation = Pivot.localRotation,
                CurrentProcess = CurrentProcess?.GetData(),
                NextProcess = NextProcess?.GetData(),
                CurrentWalking = CurrentWalking?.GetData(),
                CurrentWalkingAgent = CurrentAgentWalking?.GetData(),
                CurrentRoaming = CurrentRoaming?.GetData(),
                CurrentWait = CurrentWaiting?.GetData(),
                HeightOverride = HeightOverride,
                AddonsQueue = _addonsQueue.Where(a => a.Save).Select(a => a.Key).ToArray(),
                Addons = _addons.Where(a => a.Save).Select(a =>
                {
                    return new WalkerAddonMetaData()
                    {
                        Key = a.Key,
                        Data = a.SaveData()
                    };
                }).ToArray()
            };
        }
        protected void loadWalkerData(WalkerData data)
        {
            _isLoaded = true;
            Id = new Guid(data.Id);
            if (!string.IsNullOrWhiteSpace(data.HomeId))
                Home = Dependencies.Get<IBuildingManager>().GetBuildingReference(new Guid(data.HomeId));
            _start = data.StartPoint;
            _current = data.CurrentPoint;
            transform.position = data.Position;
            transform.rotation = data.Rotation;
            Pivot.localPosition = data.PivotPosition;
            Pivot.localRotation = data.PivotRotation;
            CurrentProcess = ProcessState.FromData(data.CurrentProcess);
            NextProcess = ProcessState.FromData(data.NextProcess);
            CurrentRoaming = RoamingState.FromData(data.CurrentRoaming);
            CurrentWalking = WalkingState.FromData(data.CurrentWalking);
            CurrentAgentWalking = WalkingAgentState.FromData(data.CurrentWalkingAgent);
            CurrentWaiting = WaitingState.FromData(data.CurrentWait);
            HeightOverride = data.HeightOverride;

            if (data.Addons != null && data.Addons.Length > 0)
            {
                var addons = Dependencies.Get<IKeyedSet<WalkerAddon>>();

                foreach (var addonMetaData in data.Addons)
                {
                    var addon = AddAddon(addons.GetObject(addonMetaData.Key));
                    if (addon == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(addonMetaData.Data))
                        continue;

                    addon.LoadData(addonMetaData.Data);
                }

                foreach (var queuedAddon in data.AddonsQueue)
                {
                    _addonsQueue.Add(addons.GetObject(queuedAddon));
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class WalkerEvent : UnityEvent<Walker> { }
}