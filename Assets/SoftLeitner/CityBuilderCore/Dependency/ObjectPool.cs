using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// simple object pooling implementation accessible through the dependency system and used in walker spawners for example
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_object_pool.html")]
    public class ObjectPool : MonoBehaviour, IObjectPool
    {
        private class PrefabPool
        {
            private Transform _parent;
            private Component _prefab;
            private Queue<Component> _queue;

            public PrefabPool(Transform parent, Component prefab)
            {
                _parent = parent;
                _prefab = prefab;
                _queue = new Queue<Component>();
            }

            public T Request<T>(Transform parent)
                where T : Component
            {
                T instance = null;

                while (_queue.Count > 0 && instance == null)
                {
                    instance = (T)_queue.Dequeue();
                    if (!instance)//just in case the object has otherwise been destroyed
                    {
                        instance = null;
                    }
                }

                if (instance == null)
                {
                    return (T)Instantiate(_prefab, parent, true);
                }
                else
                {
                    instance.transform.SetParent(parent, true);
                    instance.gameObject.SetActive(true);
                    return instance;
                }
            }

            public T Request<T>(Transform parent, System.Func<T, bool> check)
                where T : Component
            {
                T instance = null;

                while (_queue.Count > 0 && instance == null)
                {
                    instance = (T)_queue.Peek();
                    if (!instance)//just in case the object has otherwise been destroyed
                    {
                        _queue.Dequeue();
                        instance = null;
                    }
                }

                if (instance == null)
                {
                    instance = (T)Instantiate(_prefab, parent);

                    if (check(instance))
                    {
                        return instance;
                    }
                    else
                    {
                        Release(instance);

                        return null;
                    }
                }
                else
                {
                    instance.gameObject.SetActive(true);

                    if (check(instance))
                    {
                        _queue.Dequeue();

                        instance.transform.SetParent(parent);

                        return instance;
                    }
                    else
                    {
                        instance.gameObject.SetActive(false);

                        return null;
                    }
                }
            }

            public void Release(Component instance)
            {
                if (!instance)
                    return;

                instance.gameObject.SetActive(false);
                instance.transform.SetParent(_parent);

                _queue.Enqueue(instance);
            }
        }

        private Dictionary<Component, PrefabPool> _pools = new Dictionary<Component, PrefabPool>();

        private void Awake()
        {
            Dependencies.Register<IObjectPool>(this);
        }

        public T Request<T>(T prefab, Transform parent, System.Func<T, bool> check = null)
            where T : Component
        {
            if (!_pools.ContainsKey(prefab))
                _pools.Add(prefab, new PrefabPool(transform, prefab));

            if (check == null)
                return _pools[prefab].Request<T>(parent);
            else
                return _pools[prefab].Request(parent, check);
        }

        public void Release(Component prefab, Component instance)
        {
            if (!_pools.ContainsKey(prefab))
                _pools.Add(prefab, new PrefabPool(transform, prefab));
            _pools[prefab].Release(instance);
        }
    }
}
