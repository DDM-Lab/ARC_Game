using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for spawning and keeping track of walkers
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class WalkerSpawner<T>
    where T : Walker
    {
        [Tooltip("the walker prefab that will be instantiated when spawning")]
        public T Prefab;
        [Tooltip("maximum active walkers")]
        public int Count = 1;

        /// <summary>
        /// whether another walker can be spawned
        /// </summary>
        public bool HasWalker => Prefab && (Count == -1 || (_currentWalkers.Count + _preparingWalkers) < Count);

        public IReadOnlyList<T> CurrentWalkers => _currentWalkers;

        protected List<T> _currentWalkers = new List<T>();

        protected Transform _root;
        protected IBuilding _building;

        private MonoBehaviour _owner;
        private Func<object> _preparer;
        private Func<object, object> _planner;
        private Action<T, object> _spawner;
        private int _preparingWalkers;

        private Func<T, bool> _onSpawning;
        private Action<T> _onFinished;

        private LazyDependency<IGridPositions> _gridPositions = new LazyDependency<IGridPositions>();

        public void Initialize(Transform root, Func<T, bool> onSpawning = null, Action<T> onFinished = null)
        {
            _root = root;

            _onSpawning = onSpawning;
            _onFinished = onFinished;

            initialize();
        }
        public void Initialize(IBuilding building, Func<T, bool> onSpawning = null, Action<T> onFinished = null)
        {
            _building = building;

            _onSpawning = onSpawning;
            _onFinished = onFinished;

            initialize();
        }
        public void Initialize<Q, P>(Transform root, MonoBehaviour owner, Func<Q> preparer, Func<Q, P> planner, Action<T, P> spawner = null, Action<T> finished = null)
        {
            _root = root;

            _owner = owner;
            _preparer = () => preparer();
            _planner = q => planner((Q)q);
            _spawner = (w, p) => spawner(w, (P)p);

            _onFinished = finished;

            initialize();
        }
        public void Initialize<Q, P>(IBuilding building, MonoBehaviour owner, Func<Q> preparer, Func<Q, P> planner, Action<T, P> spawner = null, Action<T> finished = null)
        {
            _building = building;

            _owner = owner;
            _preparer = () => preparer();
            _planner = q => planner((Q)q);
            _spawner = (w, p) => spawner(w, (P)p);

            _onFinished = finished;

            initialize();
        }
        protected virtual void initialize()
        {

        }

        public void Integrate(T walker, Action<T> onSpawned = null)
        {
            walker.Initialize(_building?.BuildingReference, walker.GridPoint);
            walker.Spawned();

            _currentWalkers.Add(walker);

            walker.Finished += walkerFinished;

            onSpawned?.Invoke(walker);
        }

        protected void clearWalkers()
        {
            var pool = Dependencies.GetOptional<IObjectPool>();
            var manager = Dependencies.Get<IWalkerManager>();

            _currentWalkers.ForEach(w => manager.DeregisterWalker(w));

            if (pool == null)
                _currentWalkers.ForEach(w => UnityEngine.Object.Destroy(w.gameObject));
            else
                _currentWalkers.ForEach(w => pool.Release(Prefab, w));

            _currentWalkers.Clear();
        }

        protected T reloadActive()
        {
            T walker = UnityEngine.Object.Instantiate(Prefab, _root ? _root : _building.Root);

            Dependencies.Get<IWalkerManager>().RegisterWalker(walker);

            _currentWalkers.Add(walker);

            walker.Finished += walkerFinished;

            return walker;
        }

        protected void spawn(Action<T> onSpawned = null, Vector2Int? start = null)
        {
            if (_preparer == null)
            {
                if (!start.HasValue)
                    start = _building.GetAccessPoint(Prefab.PathType, Prefab.PathTag);

                if (!start.HasValue)
                    return;

                T walker;

                var pool = Dependencies.GetOptional<IObjectPool>();

                if (pool == null)
                {
                    walker = UnityEngine.Object.Instantiate(Prefab, _root ? _root : _building.Root, true);
                    walker.transform.position = _gridPositions.Value.GetWorldPosition(start.Value);
                    walker.Initialize(_building?.BuildingReference, start.Value);

                    if (_onSpawning != null && !_onSpawning(walker))
                    {
                        UnityEngine.Object.Destroy(walker.gameObject);
                        return;
                    }

                    walker.Spawned();
                }
                else
                {
                    walker = pool.Request(Prefab, _root ? _root : _building.Root, w =>
                    {
                        w.transform.rotation = Prefab.transform.rotation;
                        w.transform.position = _gridPositions.Value.GetWorldPosition(start.Value);
                        w.Initialize(_building?.BuildingReference, start.Value);

                        if (_onSpawning != null && !_onSpawning(w))
                        {
                            return false;
                        }

                        w.Spawned();

                        return true;
                    });

                    if (walker == null)
                        return;
                }

                _currentWalkers.Add(walker);

                walker.Finished += walkerFinished;

                onSpawned?.Invoke(walker);
            }
            else
            {
                spawnPrepared(_owner, _preparer, _planner, _spawner, onSpawned, start);
            }
        }
        protected void spawnPrepared(MonoBehaviour owner, Func<object> preparer, Func<object, object> planner, Action<T, object> spawner = null, Action<T> onSpawned = null, Vector2Int? start = null)
        {
            if (!start.HasValue)
                start = _building.GetAccessPoint(Prefab.PathType, Prefab.PathTag);

            if (!start.HasValue)
                return;

            _preparingWalkers++;
            owner.StartCoroutine(spawnPreparedRountine(preparer, planner, spawner, onSpawned, start.Value));
        }
        private IEnumerator spawnPreparedRountine(Func<object> preparer, Func<object, object> planner, Action<T, object> spawner, Action<T> onSpawned, Vector2Int start)
        {
            var query = preparer();
            if (query == null)
                yield break;

            yield return null;

            _preparingWalkers--;

            var plan = planner(query);
            if (plan == null)
                yield break;

            T walker;

            var pool = Dependencies.GetOptional<IObjectPool>();

            if (pool == null)
            {
                walker = UnityEngine.Object.Instantiate(Prefab, _root ? _root : _building.Root, true);
                walker.transform.position = _gridPositions.Value.GetWorldPosition(start);
                walker.Initialize(_building?.BuildingReference, start);

                spawner(walker, plan);

                walker.Spawned();
            }
            else
            {
                walker = pool.Request(Prefab, _root ? _root : _building.Root, w =>
                {
                    w.transform.rotation = Prefab.transform.rotation;
                    w.transform.position = _gridPositions.Value.GetWorldPosition(start);
                    w.Initialize(_building?.BuildingReference, start);

                    spawner(w, plan);

                    w.Spawned();

                    return true;
                });

                if (walker == null)
                    yield break;
            }

            _currentWalkers.Add(walker);

            walker.Finished += walkerFinished;

            onSpawned?.Invoke(walker);
        }

        private void walkerFinished(Walker walker)
        {
            walker.Finished -= walkerFinished;

            _currentWalkers.Remove((T)walker);
            _onFinished?.Invoke((T)walker);

            var pool = Dependencies.GetOptional<IObjectPool>();

            if (pool == null)
                UnityEngine.Object.Destroy(walker.gameObject);
            else
                pool.Release(Prefab, walker);
        }
    }
}