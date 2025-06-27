namespace CityBuilderCore
{
    /// <summary>
    /// a building component that gets items, stores them and then distributes them to <see cref="ItemRecipient"/>
    /// </summary>
    public interface IDistributionComponent : IBuildingComponent, IItemOwner
    {
        /// <summary>
        /// the storage used by the component, needed to calculate ratioed capacity
        /// </summary>
        ItemStorage Storage { get; }
        /// <summary>
        /// the orders in the distributor, determines how much, if any, of an item will be purchased
        /// </summary>
        DistributionOrder[] Orders { get; }
    }
}