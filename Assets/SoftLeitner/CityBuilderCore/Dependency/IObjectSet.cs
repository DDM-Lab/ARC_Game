namespace CityBuilderCore
{
    public interface IObjectSet<T>
    {
        T[] Objects { get; }
    }
}