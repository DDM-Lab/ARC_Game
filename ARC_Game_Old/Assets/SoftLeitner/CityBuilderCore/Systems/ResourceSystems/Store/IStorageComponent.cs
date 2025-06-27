namespace CityBuilderCore
{
    /// <summary>
    /// building component that stores items, how it does that can be configured in orders
    /// </summary>
    public interface IStorageComponent : IBuildingTrait<IStorageComponent>, IItemReceiver, IItemGiver, IItemOwner
    {
        /// <summary>
        /// the storage that holds the components items
        /// </summary>
        ItemStorage Storage { get; }
        /// <summary>
        /// orders determine how much of which item is stored and how they are treated
        /// </summary>
        StorageOrder[] Orders { get; }
    }
}