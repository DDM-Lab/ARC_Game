namespace CityBuilderCore
{
    public class RuntimeObjectSet<T> : IObjectSet<T>
    {
        public T[] Objects { get; set; }

        public RuntimeObjectSet(params T[] objects)
        {
            Objects = objects;
        }
        public RuntimeObjectSet()
        {
            Objects = new T[0];
        }
    }
}
