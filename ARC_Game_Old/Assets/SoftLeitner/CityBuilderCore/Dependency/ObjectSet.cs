using System;
using System.Linq;

namespace CityBuilderCore
{
    public class ObjectSet<T> : ObjectSetBase, IObjectSet<T>
    {
        public T[] Objects;

        T[] IObjectSet<T>.Objects => Objects;

        public override Type GetObjectType() => typeof(T);
        public override void SetObjects(object[] objects) => Objects = objects.Select(o => (T)o).ToArray();

        public void Add(T o)
        {
            var list = Objects.ToList();
            list.Add(o);
            Objects = list.ToArray(); 
        }
        public void Remove(T o)
        {
            var list = Objects.ToList();
            list.Remove(o);
            Objects = list.ToArray();
        }
    }
}