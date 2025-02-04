using System.Collections.Generic;

namespace CityBuilderCore
{
    public class KeyedSet<T> : ObjectSet<T>, IKeyedSet<T>
    where T : class, IKeyed
    {
        private Dictionary<string, T> _objectsByKey = new Dictionary<string, T>();

        private void OnEnable()
        {
            _objectsByKey.Clear();
            if (Objects != null)
            {
                foreach (var o in Objects)
                {
                    if (o?.Key == null)
                        continue;
                    if (_objectsByKey.ContainsKey(o.Key))
                        throw new System.Exception($"Keyed Set {name} already contains multiple objects with key:{o.Key}");

                    _objectsByKey.Add(o.Key, o);
                }
            }
        }

        public T GetObject(string key) => _objectsByKey.ContainsKey(key) ? _objectsByKey[key] : null;
    }
}