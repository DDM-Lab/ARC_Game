namespace CityBuilderCore
{
    /// <summary>
    /// global storage for items<br/>
    /// used for building costs<br/>
    /// filled by buildings with <see cref="ItemStorageMode.Global"/>
    /// </summary>
    public interface IGlobalStorage
    {
        /// <summary>
        /// storage that holds the items of the global storage
        /// </summary>
        ItemStorage Items { get; }
    }
}