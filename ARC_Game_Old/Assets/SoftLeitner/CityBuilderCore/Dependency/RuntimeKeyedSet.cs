using System.Linq;

namespace CityBuilderCore
{
    public class RuntimeKeyedSet<T> : RuntimeObjectSet<T>, IKeyedSet<T> where T : IKeyed
    {
        public T GetObject(string key) => Objects.FirstOrDefault(o => o.Key == key);

        public RuntimeKeyedSet(params T[] objects)
        {
            Objects = objects;
        }
        public RuntimeKeyedSet()
        {
            Objects = new T[0];
        }
    }
}
