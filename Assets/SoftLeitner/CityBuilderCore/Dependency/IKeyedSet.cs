namespace CityBuilderCore
{
    public interface IKeyedSet<T> : IObjectSet<T>
    {
        T GetObject(string key);

        public static T GetKeyedObject(string key)
        {
            var set = Dependencies.GetOptional<IKeyedSet<T>>();
            if (set == null)
                return default;
            return set.GetObject(key);
        }
    }
}