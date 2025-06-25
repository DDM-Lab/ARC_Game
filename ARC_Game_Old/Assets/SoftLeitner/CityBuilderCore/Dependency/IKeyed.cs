namespace CityBuilderCore
{
    /// <summary>
    /// object that can be uniquely identified by a string key<br/>
    /// it can then be retrieved from <see cref="IKeyedSet{T}"/> by its key
    /// </summary>
    public interface IKeyed
    {
        /// <summary>
        /// unique identifier among a type of objects(might be used in savegames, be careful when changing)
        /// </summary>
        string Key { get; }
    }
}