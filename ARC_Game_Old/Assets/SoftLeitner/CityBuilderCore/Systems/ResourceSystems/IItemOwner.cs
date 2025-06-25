namespace CityBuilderCore
{
    /// <summary>
    /// any component that has items in an <see cref="IItemContainer"/>
    /// </summary>
    public interface IItemOwner
    {
        /// <summary>
        /// holds and manages the items for the owner
        /// </summary>
        IItemContainer ItemContainer { get; }
    }
}